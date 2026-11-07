# Zero-Cost ($0/Month) Setup Guide: Google AI Studio Free Tier & GitHub Actions

This guide explains how **AutonomousAgentHub** achieves continuous, verifiable autonomous evolution at **$0/month** using **Google AI Studio Free Tier (`GEMINI_API_KEY`)**, built-in **Google Search Grounding**, and **GitHub Actions**.

---

## 1. Obtaining a Free `GEMINI_API_KEY` (Google AI Studio)

1. Sign in to [Google AI Studio](https://aistudio.google.com/).
2. Select **Get API key** -> **Create API key** in a free-tier project (do **not** attach a paid billing account if you wish to guarantee strict `$0.00` spend).
3. Copy the generated API key.
4. In your GitHub repository (`TatoTopuria/autonomous-agent-hub`), navigate to:
   **Settings -> Secrets and variables -> Actions -> New repository secret**
5. Add:
   - **Name:** `GEMINI_API_KEY`
   - **Secret:** `<your-google-ai-studio-key>`
6. Ensure `GH_ORCHESTRATOR_PAT` is also configured with fine-grained permissions (`Contents: Read and write`, `Pull requests: Read and write`, `Metadata: Read-only`).

---

## 2. Free-Tier Rate-Limit & Quota Governance

Google AI Studio's Free Tier enforces Requests Per Minute (RPM), Tokens Per Minute (TPM), and Requests Per Day (RPD) quotas. `GeminiFreeTierClient` enforces three deterministic layers of protection:

1. **Inter-Stage Pacing (`InterStageDelaySeconds = 6`):**
   - A thread-safe `SemaphoreSlim` gate enforces a minimum 6-second pause between consecutive Gemini API calls, keeping burst rate well below 10 RPM.
2. **HTTP 429 / 503 Exponential Backoff + `Retry-After` Header Compliance:**
   - On `429 Too Many Requests` or `503 Service Unavailable`, `GeminiFreeTierClient` inspects the standard HTTP `Retry-After` header (IETF RFC 9110 Section 10.2.3) and falls back to `15s -> 35s -> 70s` exponential backoff with random jitter (`250ms–2250ms`).
3. **Automatic Model Fallback:**
   - If a primary model (such as `gemini-2.5-pro`) encounters HTTP `429`, the client automatically switches to `gemini-2.5-flash` for the remainder of the retry loop.

---

## 3. Built-In Google Search Grounding at $0 Cost

In Stage 2 (`Stage2FinderAgent`), `GeminiFreeTierClient` attaches:
```json
{
  "tools": [
    {
      "google_search": {}
    }
  ]
}
```
This enables real-time retrieval of official specifications (Microsoft Learn, ECMA-334, OWASP, IETF RFCs, WHO/AAP/NICE/CDC, PCI-DSS/ISO-20022) without paid third-party search APIs (no SerpApi, Tavily, or Bing API keys required).

---

## 4. Delta-First Context Budgeting (`docs/autonomous-evolution/state-ledger.json`)

To minimize input token usage on every evolution cycle:
- `scripts/autonomous-evolution-runner.sh` reads `lastInspectedCommitSha` from `docs/autonomous-evolution/state-ledger.json`.
- Only commits and file diffs modified since `lastInspectedCommitSha` (`git diff <lastInspectedCommitSha>..HEAD`) are prioritized for inspection.
- Completed Tier 1 evolutions and approved Tier 2 proposals are recorded in `docs/autonomous-evolution/state-ledger.json` so the agent never wastes tokens re-proposing completed work.

