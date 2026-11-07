using System.Text;
using AutonomousAgentHub.Core.Configuration;
using AutonomousAgentHub.Core.Contracts;
using AutonomousAgentHub.Core.Infrastructure;

namespace AutonomousAgentHub.Core.Pipeline;

/// <summary>
/// Stage 2: Official Authoritative Research Agent (Finder).
/// Uses Gemini with Google Search Grounding (<c>"tools": [{"google_search": {}}]</c>) to retrieve
/// standards and best practices strictly from whitelisted technical and domain authorities.
/// </summary>
public sealed class Stage2FinderAgent(
    IGeminiClient geminiClient,
    ISkillRegistry? skillRegistry = null,
    ExperientialMemoryManager? experientialMemoryManager = null)
{
    private static readonly SkillRegistry DefaultSkillRegistry = new();

    private static readonly string[] BannedUnofficialDomains =
    [
        "medium.com",
        "reddit.com",
        "dev.to",
        "stackoverflow.com",
        "blogspot.com",
        "wordpress.com",
        "substack.com",
        "quora.com",
        "dzone.com",
        "baeldung.com",
        "geeksfor" + "geeks.org",
        "tutorialspoint.com",
        "w3schools.com"
    ];

    public async Task<Stage2FinderArtifact> ExecuteAsync(
        RepositoryEntry repository,
        Stage1DiscoveryArtifact discovery,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(discovery);

        Console.WriteLine($"[Stage 2: Finder] Researching official authoritative standards for '{repository.FullName}' ({discovery.DetectedStack})...");

        List<string> allowedAuthorities = ResolveWhitelistedAuthorities(repository, discovery);

        string baseSystemInstruction =
            """
            You are Stage 2 (Finder Agent) of the Autonomous Evolution Hub.
            You have Google Search Grounding enabled. Your sole duty is to gather verifiable engineering,
            security, performance, and domain standards STRICTLY from Whitelisted Official Authorities.

            WHITELISTED OFFICIAL AUTHORITIES:
            - .NET / C#: Microsoft Learn (learn.microsoft.com), Official .NET Runtime / ASP.NET Core / EF Core Docs, ECMA-334 C# Language Specification.
            - Java / Spring: Official Spring Framework / Spring Boot Docs (docs.spring.io), Oracle / OpenJDK Specifications (docs.oracle.com, openjdk.org), Jakarta EE Specifications (jakarta.ee).
            - Python: Official Python Docs (docs.python.org), Python Enhancement Proposals (peps.python.org), FastAPI / Django / Pydantic Official Docs.
            - Node.js / TypeScript: Official Node.js Docs (nodejs.org), TypeScript Docs (typescriptlang.org), ECMA-262 / MDN Web Standards.
            - DevOps / Cloud-Native Infrastructure: Cloud Native Computing Foundation (cncf.io), Official Kubernetes Docs (kubernetes.io), OpenTelemetry Specifications (opentelemetry.io), CIS Benchmarks (cisecurity.org).
            - General Architecture & Security: OWASP Foundation (owasp.org), IETF RFCs (datatracker.ietf.org / rfc-editor.org), OpenAPI Initiative (spec.openapis.org).
            - Domain-Specific International Bodies:
              * Healthcare / Pediatrics / Clinical: World Health Organization (WHO - who.int), American Academy of Pediatrics (AAP - aap.org), NICE Guidelines (nice.org.uk), CDC (cdc.gov), HL7 / FHIR.
              * FinTech / Payments / Banking: PCI Security Standards Council (PCI-DSS), ISO-20022 (iso20022.org), NIST (nist.gov).

            STRICT PROHIBITIONS:
            - NEVER cite blogs, Medium, Reddit, StackOverflow, dev.to, forums, or unverified third-party tutorials.
            - Every EvidenceBlock MUST include:
              * id (e.g., "EV-01")
              * issuingAuthority (Exact official organization name)
              * documentTitle (Exact title of the specification, RFC, PEP, or official doc)
              * publicationYear (Integer year)
              * officialUrl (Direct https:// URL to the official specification or documentation)
              * governingExcerpt (Concise normative rule or technical directive from the standard)
              * relevanceToRepo (How this standard directly applies to the inspected codebase)

            Return ONLY valid JSON matching the Stage2FinderArtifact contract.
            """;

        string? experientialInjection = experientialMemoryManager?.BuildStagePromptInjection("Stage2Finder", discovery.DetectedStack);
        string systemInstruction = (skillRegistry ?? DefaultSkillRegistry).ComposeStageSystemInstruction(
            baseSystemInstruction,
            "stage2-finder-grounding",
            discovery.DetectedStack,
            experientialInjection);

        StringBuilder prompt = new();
        prompt.AppendLine($"Target Repository: {repository.FullName}");
        prompt.AppendLine($"Detected Stack: {discovery.DetectedStack}");
        prompt.AppendLine($"Domain Purpose: {discovery.DomainPurpose}");
        prompt.AppendLine($"Architectural Style: {discovery.ArchitecturalStyle}");
        prompt.AppendLine();
        prompt.AppendLine("Enforced Whitelisted Authorities for this Repository:");
        foreach (string auth in allowedAuthorities)
        {
            prompt.AppendLine($"- {auth}");
        }

        prompt.AppendLine();
        prompt.AppendLine("Critical Business Invariants:");
        foreach (string inv in discovery.CriticalBusinessInvariants)
        {
            prompt.AppendLine($"- {inv}");
        }

        prompt.AppendLine();
        prompt.AppendLine("Inspected Codebase Files:");
        foreach (InspectedFileSnapshot file in discovery.CandidateFilesForInspection)
        {
            prompt.AppendLine($"--- {file.RelativePath} ---");
            prompt.AppendLine(file.ContentExcerpt);
            prompt.AppendLine();
        }

        Stage2FinderArtifact rawArtifact = await geminiClient.GenerateStructuredAsync<Stage2FinderArtifact>(
            systemInstruction,
            prompt.ToString(),
            useGoogleSearchGrounding: true,
            cancellationToken).ConfigureAwait(false);

        List<EvidenceBlock> verifiedBlocks =
        [
            .. rawArtifact.EvidenceBlocks.Where(IsVerifiableOfficialEvidence)
        ];

        return new Stage2FinderArtifact
        {
            RepositoryFullName = repository.FullName,
            TargetStack = discovery.DetectedStack,
            DomainFocus = discovery.DomainPurpose,
            SearchQueriesExecuted = rawArtifact.SearchQueriesExecuted,
            EvidenceBlocks = verifiedBlocks
        };
    }

    public static bool IsVerifiableOfficialEvidence(EvidenceBlock block)
    {
        if (string.IsNullOrWhiteSpace(block.IssuingAuthority) ||
            string.IsNullOrWhiteSpace(block.DocumentTitle) ||
            string.IsNullOrWhiteSpace(block.OfficialUrl) ||
            string.IsNullOrWhiteSpace(block.GoverningExcerpt) ||
            string.IsNullOrWhiteSpace(block.RelevanceToRepo))
        {
            return false;
        }

        if (!Uri.TryCreate(block.OfficialUrl, UriKind.Absolute, out Uri? uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string host = uri.Host.ToLowerInvariant();
        foreach (string banned in BannedUnofficialDomains)
        {
            if (host.Equals(banned, StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith($".{banned}", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    public static List<string> ResolveWhitelistedAuthorities(
        RepositoryEntry repository,
        Stage1DiscoveryArtifact discovery)
    {
        List<string> authorities = [.. repository.WhitelistedAuthorities];

        if (authorities.Count == 0)
        {
            authorities.AddRange(
            [
                "OWASP Foundation",
                "IETF RFCs",
                "Cloud Native Computing Foundation (CNCF)",
                "OpenAPI Initiative Specifications"
            ]);
        }

        if (discovery.DetectedStack.Contains("DotNet", StringComparison.OrdinalIgnoreCase))
        {
            AddIfMissing(authorities, "Microsoft Learn (.NET / ASP.NET Core / EF Core Official Docs)");
            AddIfMissing(authorities, "C# Language Specification (ECMA-334)");
        }
        else if (discovery.DetectedStack.Contains("Java", StringComparison.OrdinalIgnoreCase))
        {
            AddIfMissing(authorities, "Official Spring Framework / Spring Boot Documentation");
            AddIfMissing(authorities, "Oracle / OpenJDK Specifications");
            AddIfMissing(authorities, "Jakarta EE Specifications");
        }
        else if (discovery.DetectedStack.Contains("Python", StringComparison.OrdinalIgnoreCase))
        {
            AddIfMissing(authorities, "Official Python Documentation & PEPs");
            AddIfMissing(authorities, "FastAPI / Django / Pydantic Official Documentation");
        }
        else if (discovery.DetectedStack.Contains("Node", StringComparison.OrdinalIgnoreCase))
        {
            AddIfMissing(authorities, "Official Node.js & TypeScript Documentation");
            AddIfMissing(authorities, "ECMA-262 Specification");
        }
        else if (discovery.DetectedStack.Contains("DevOps", StringComparison.OrdinalIgnoreCase) ||
                 discovery.DetectedStack.Contains("Infra", StringComparison.OrdinalIgnoreCase))
        {
            AddIfMissing(authorities, "Cloud Native Computing Foundation (CNCF)");
            AddIfMissing(authorities, "Official Kubernetes Documentation");
            AddIfMissing(authorities, "OpenTelemetry Official Specifications");
            AddIfMissing(authorities, "CIS Foundations & Kubernetes Benchmarks");
            AddIfMissing(authorities, "OWASP Container & Kubernetes Security Cheat Sheet");
        }

        string combinedDomain = $"{repository.Domain} {discovery.DomainPurpose}";
        if (combinedDomain.Contains("health", StringComparison.OrdinalIgnoreCase) ||
            combinedDomain.Contains("clinical", StringComparison.OrdinalIgnoreCase) ||
            combinedDomain.Contains("pediatric", StringComparison.OrdinalIgnoreCase) ||
            combinedDomain.Contains("medical", StringComparison.OrdinalIgnoreCase))
        {
            AddIfMissing(authorities, "World Health Organization (WHO)");
            AddIfMissing(authorities, "American Academy of Pediatrics (AAP)");
            AddIfMissing(authorities, "NICE Clinical Guidelines");
            AddIfMissing(authorities, "Centers for Disease Control and Prevention (CDC)");
        }

        if (combinedDomain.Contains("fin", StringComparison.OrdinalIgnoreCase) ||
            combinedDomain.Contains("payment", StringComparison.OrdinalIgnoreCase) ||
            combinedDomain.Contains("bank", StringComparison.OrdinalIgnoreCase) ||
            combinedDomain.Contains("ledger", StringComparison.OrdinalIgnoreCase))
        {
            AddIfMissing(authorities, "PCI Security Standards Council (PCI-DSS)");
            AddIfMissing(authorities, "ISO-20022 Financial Services Messaging Standard");
        }

        return authorities;
    }

    private static void AddIfMissing(List<string> list, string item)
    {
        if (!list.Contains(item, StringComparer.OrdinalIgnoreCase))
        {
            list.Add(item);
        }
    }
}
