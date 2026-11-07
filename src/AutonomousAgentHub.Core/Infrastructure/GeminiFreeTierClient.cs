using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using AutonomousAgentHub.Core.Serialization;

namespace AutonomousAgentHub.Core.Infrastructure;

/// <summary>
/// Abstraction over Gemini LLM generation to support both production Google AI Studio calls and deterministic testing.
/// </summary>
public interface IGeminiClient
{
    Task<T> GenerateStructuredAsync<T>(
        string systemInstruction,
        string userPrompt,
        bool useGoogleSearchGrounding = false,
        CancellationToken cancellationToken = default) where T : class;
}

/// <summary>
/// Production Google AI Studio Free-Tier client with inter-stage rate-limit pacing,
/// exponential backoff + jitter (15s, 35s, 70s) on HTTP 429/503, automatic model fallback
/// (<c>gemini-2.5-pro</c> -> <c>gemini-2.5-flash</c>), Google Search Grounding, and structured JSON deserialization.
/// </summary>
public sealed class GeminiFreeTierClient : IGeminiClient, IDisposable
{
    private static readonly int[] RetryBackoffSeconds = [15, 35, 70];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        TypeInfoResolver = HubJsonSerializerContext.Default
    };

    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _primaryModel;
    private readonly string _fallbackModel;
    private readonly TimeSpan _interStageDelay;
    private readonly int _maxOutputTokens;
    private DateTimeOffset _lastRequestCompletedUtc = DateTimeOffset.MinValue;
    private readonly SemaphoreSlim _rateLimitLock = new(1, 1);
    private bool _disposed;

    public GeminiFreeTierClient(
        HttpClient httpClient,
        string? apiKey = null,
        string? primaryModel = null,
        string? fallbackModel = null,
        int interStageDelaySeconds = 6,
        int maxOutputTokens = 16384)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;

        _apiKey = !string.IsNullOrWhiteSpace(apiKey)
            ? apiKey
            : Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? string.Empty;

        string? envModel = Environment.GetEnvironmentVariable("GEMINI_MODEL");
        _primaryModel = !string.IsNullOrWhiteSpace(primaryModel)
            ? primaryModel
            : !string.IsNullOrWhiteSpace(envModel)
                ? envModel
                : "gemini-2.5-flash";

        _fallbackModel = !string.IsNullOrWhiteSpace(fallbackModel)
            ? fallbackModel
            : "gemini-2.5-flash";

        _interStageDelay = TimeSpan.FromSeconds(Math.Max(0, interStageDelaySeconds));
        _maxOutputTokens = Math.Clamp(maxOutputTokens, 512, 16384);
    }

    public string PrimaryModel => _primaryModel;

    public string FallbackModel => _fallbackModel;

    /// <summary>
    /// Calls the Gemini REST API, enforces inter-stage rate limiting and 429/503 exponential backoff,
    /// and deserializes the response into <typeparamref name="T"/>.
    /// </summary>
    public async Task<T> GenerateStructuredAsync<T>(
        string systemInstruction,
        string userPrompt,
        bool useGoogleSearchGrounding = false,
        CancellationToken cancellationToken = default) where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemInstruction);
        ArgumentException.ThrowIfNullOrWhiteSpace(userPrompt);

        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            throw new InvalidOperationException(
                "GEMINI_API_KEY is not configured. Set the GEMINI_API_KEY environment variable with a valid Google AI Studio key.");
        }

        string sanitizedPrompt = SanitizeInputPrompt(userPrompt);

        await EnforceInterStagePacingAsync(cancellationToken).ConfigureAwait(false);

        string activeModel = _primaryModel;
        Exception? lastException = null;

        for (int attempt = 0; attempt <= RetryBackoffSeconds.Length; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                string rawResponseText = await ExecuteGenerateContentCallAsync(
                    activeModel,
                    systemInstruction,
                    sanitizedPrompt,
                    useGoogleSearchGrounding,
                    cancellationToken).ConfigureAwait(false);

                string cleanJson = ExtractCleanJsonPayload(rawResponseText);
                T? result = JsonSerializer.Deserialize<T>(cleanJson, JsonOptions);

                if (result is null)
                {
                    throw new InvalidOperationException(
                        $"Gemini returned an empty or null JSON payload for contract '{typeof(T).Name}'.");
                }

                return result;
            }
            catch ( GeminiQuotaOrTransientException ex ) when (attempt < RetryBackoffSeconds.Length)
            {
                lastException = ex;

                // Automatic fallback from gemini-2.5-pro (or other pro model) to gemini-2.5-flash on quota exhaustion (429)
                if (ex.StatusCode == HttpStatusCode.TooManyRequests &&
                    !string.Equals(activeModel, _fallbackModel, StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine(
                        $"[GeminiGovernor] HTTP 429 on model '{activeModel}'. Automatically falling back to '{_fallbackModel}'.");
                    activeModel = _fallbackModel;
                }

                int baseDelaySec = ex.RetryAfterDelay.HasValue
                    ? (int)Math.Clamp(ex.RetryAfterDelay.Value.TotalSeconds, 1, 120)
                    : RetryBackoffSeconds[attempt];
                int jitterMs = Random.Shared.Next(250, 2250);
                TimeSpan backoff = TimeSpan.FromSeconds(baseDelaySec) + TimeSpan.FromMilliseconds(jitterMs);

                Console.WriteLine(
                    $"[GeminiGovernor] Transient status {(int)ex.StatusCode} ({ex.StatusCode}) on attempt {attempt + 1}/{RetryBackoffSeconds.Length + 1}. " +
                    $"Backing off for {backoff.TotalSeconds:F1}s before retry...");

                await Task.Delay(backoff, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new InvalidOperationException(
            $"Gemini API request failed after {RetryBackoffSeconds.Length + 1} attempts.",
            lastException);
    }

    private async Task EnforceInterStagePacingAsync(CancellationToken cancellationToken)
    {
        await _rateLimitLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_lastRequestCompletedUtc != DateTimeOffset.MinValue && _interStageDelay > TimeSpan.Zero)
            {
                TimeSpan elapsed = DateTimeOffset.UtcNow - _lastRequestCompletedUtc;
                if (elapsed < _interStageDelay)
                {
                    TimeSpan remaining = _interStageDelay - elapsed;
                    await Task.Delay(remaining, cancellationToken).ConfigureAwait(false);
                }
            }

            _lastRequestCompletedUtc = DateTimeOffset.UtcNow;
        }
        finally
        {
            _rateLimitLock.Release();
        }
    }

    private async Task<string> ExecuteGenerateContentCallAsync(
        string model,
        string systemInstruction,
        string userPrompt,
        bool useGoogleSearchGrounding,
        CancellationToken cancellationToken)
    {
        string endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={Uri.EscapeDataString(_apiKey)}";

        JsonObject payload = BuildRequestPayload(systemInstruction, userPrompt, useGoogleSearchGrounding);
        string requestJson = payload.ToJsonString(JsonOptions);

        using HttpRequestMessage request = new(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
        };

        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
        {
            TimeSpan? retryAfter = TryParseRetryAfterHeader(response);
            throw new GeminiQuotaOrTransientException(
                response.StatusCode,
                $"Gemini API returned {(int)response.StatusCode}: {Truncate(responseBody, 400)}",
                retryAfter);
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Gemini API call to model '{model}' failed with HTTP {(int)response.StatusCode}: {Truncate(responseBody, 600)}",
                null,
                response.StatusCode);
        }

        return ParseCandidateTextFromResponse(responseBody);
    }

    internal JsonObject BuildRequestPayload(
        string systemInstruction,
        string userPrompt,
        bool useGoogleSearchGrounding)
    {
        JsonObject generationConfig = new()
        {
            ["temperature"] = 0.1,
            ["maxOutputTokens"] = _maxOutputTokens
        };

        // When not using Google Search tool, enforce native application/json MIME output.
        // When using Google Search grounding, some Gemini v1beta endpoints require text/plain MIME with JSON instructions.
        if (!useGoogleSearchGrounding)
        {
            generationConfig["responseMimeType"] = "application/json";
        }

        JsonObject root = new()
        {
            ["systemInstruction"] = new JsonObject
            {
                ["parts"] = new JsonArray
                {
                    new JsonObject { ["text"] = systemInstruction }
                }
            },
            ["contents"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["parts"] = new JsonArray
                    {
                        new JsonObject { ["text"] = userPrompt }
                    }
                }
            },
            ["generationConfig"] = generationConfig
        };

        if (useGoogleSearchGrounding)
        {
            root["tools"] = new JsonArray
            {
                new JsonObject
                {
                    ["google_search"] = new JsonObject()
                }
            };
        }

        return root;
    }

    internal static string ParseCandidateTextFromResponse(string responseBody)
    {
        JsonNode? root = JsonNode.Parse(responseBody);
        JsonArray? candidates = root?["candidates"]?.AsArray();
        if (candidates is null || candidates.Count == 0)
        {
            throw new InvalidOperationException("Gemini response contained no candidates.");
        }

        JsonArray? parts = candidates[0]?["content"]?["parts"]?.AsArray();
        if (parts is null || parts.Count == 0)
        {
            throw new InvalidOperationException("Gemini candidate contained no content parts.");
        }

        StringBuilder builder = new();
        foreach (JsonNode? part in parts)
        {
            string? text = part?["text"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(text))
            {
                builder.Append(text);
            }
        }

        string combined = builder.ToString().Trim();
        if (string.IsNullOrWhiteSpace(combined))
        {
            throw new InvalidOperationException("Gemini response text was empty.");
        }

        return combined;
    }

    /// <summary>
    /// Strips markdown code fences or surrounding text so structured JSON can be parsed reliably.
    /// </summary>
    public static string ExtractCleanJsonPayload(string rawText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawText);

        string trimmed = rawText.Trim();

        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            int firstNewline = trimmed.IndexOf('\n');
            if (firstNewline > 0)
            {
                trimmed = trimmed[(firstNewline + 1)..];
            }

            int closingFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (closingFence >= 0)
            {
                trimmed = trimmed[..closingFence];
            }

            trimmed = trimmed.Trim();
        }

        int firstBrace = trimmed.IndexOf('{');
        int lastBrace = trimmed.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace > firstBrace)
        {
            return trimmed[firstBrace..(lastBrace + 1)];
        }

        return trimmed;
    }

    private static string SanitizeInputPrompt(string prompt)
    {
        // Guardrail screening for adversarial prompt override directives in untrusted repository text
        string[] suspiciousMarkers =
        [
            "IGNORE ALL PREVIOUS INSTRUCTIONS",
            "DISREGARD SYSTEM PROMPT",
            "SYSTEM OVERRIDE:"
        ];

        string sanitized = prompt;
        foreach (string marker in suspiciousMarkers)
        {
            if (sanitized.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                sanitized = sanitized.Replace(marker, "[REDACTED_PROMPT_INJECTION_ATTEMPT]", StringComparison.OrdinalIgnoreCase);
            }
        }

        return sanitized;
    }

    /// <summary>
    /// Parses the standard HTTP <c>Retry-After</c> response header (delta-seconds or HTTP-date per IETF RFC 9110 Section 10.2.3).
    /// </summary>
    public static TimeSpan? TryParseRetryAfterHeader(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter is null)
        {
            return null;
        }

        if (retryAfter.Delta.HasValue && retryAfter.Delta.Value > TimeSpan.Zero)
        {
            return TimeSpan.FromSeconds(Math.Clamp(retryAfter.Delta.Value.TotalSeconds, 1, 120));
        }

        if (retryAfter.Date.HasValue)
        {
            TimeSpan diff = retryAfter.Date.Value - DateTimeOffset.UtcNow;
            if (diff > TimeSpan.Zero)
            {
                return TimeSpan.FromSeconds(Math.Clamp(diff.TotalSeconds, 1, 120));
            }
        }

        return null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _rateLimitLock.Dispose();
        _disposed = true;
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : string.Concat(value.AsSpan(0, maxLength), "...");

    private sealed class GeminiQuotaOrTransientException(
        HttpStatusCode statusCode,
        string message,
        TimeSpan? retryAfterDelay = null)
        : Exception(message)
    {
        public HttpStatusCode StatusCode { get; } = statusCode;

        public TimeSpan? RetryAfterDelay { get; } = retryAfterDelay;
    }
}

