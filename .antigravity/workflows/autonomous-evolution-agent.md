# Autonomous Evolution Agent Workflow (`AutonomousAgentHub`)

## Overview
This workflow defines the unattended, zero-cost (`$0/month`) self-evolution protocol for **AutonomousAgentHub** (`TatoTopuria/autonomous-agent-hub`). It governs both interactive executions in the **Antigravity IDE** and automated weekly/manual runs on **GitHub Actions** using the **Google AI Studio Free Tier (`GEMINI_API_KEY`)** with **Google Search Grounding**.

---

## 1. Repository Ground Truth & Verification Commands

- **Solution File:** `AutonomousAgentHub.sln`
- **Target Framework:** `.NET 8.0` (`net8.0`), `C# 12.0`, `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`
- **Default Branch:** `main`
- **Evolution State Ledger:** `docs/autonomous-evolution/state-ledger.json`

### Mandatory Verification Pipeline
```bash
# 1. Restore NuGet packages
dotnet restore AutonomousAgentHub.sln

# 2. Compile in Release mode with Warnings-as-Errors enforcement
dotnet build AutonomousAgentHub.sln --configuration Release --no-restore

# 3. Execute full xUnit regression & unit test suite
dotnet test AutonomousAgentHub.sln --configuration Release --no-build --verbosity normal

# 4. Verify CLI Orchestrator in Read-Only Dry-Run mode
dotnet run --project src/AutonomousAgentHub.Core/AutonomousAgentHub.Core.csproj --configuration Release --no-build -- --dry-run
```

---

## 2. Whitelisted International Authorities & Evidence Governance

Every Tier 1 implementation and Tier 2 architectural proposal MUST be backed by a verifiable `EvidenceBlock` from one of the following authorities:

1. **Microsoft Learn (.NET 8 / C# 12 / BCL Documentation)** — `https://learn.microsoft.com/en-us/dotnet/`
2. **ECMA-334 C# Language Specification** — `https://ecma-international.org/publications-and-standards/standards/ecma-334/`
3. **Google AI for Developers (Official Gemini API Documentation)** — `https://ai.google.dev/gemini-api/docs`
4. **GitHub Official Documentation & REST API (`2022-11-28`)** — `https://docs.github.com/`
5. **OWASP Foundation (OWASP Top 10 / Top 10 for LLM Applications / ASVS)** — `https://owasp.org/`
6. **IETF RFCs (RFC 9110 HTTP Semantics, RFC 8259 JSON, RFC 3986 URI)** — `https://datatracker.ietf.org/` / `https://www.rfc-editor.org/`

### Zero-Hallucination Disqualification Rule
Citations from Medium, Reddit, StackOverflow, dev.to, Baeldung, DZone, personal blogs, or unverified tutorials are strictly prohibited. Any proposal lacking a verifiable official citation is immediately disqualified.

---

## 3. Tier 1 vs. Tier 2 Boundary Calibration

### Tier 1 (Safe for Autonomous Implementation & Direct Push to `main`)
- Unit and integration test expansions in `tests/AutonomousAgentHub.Tests/`.
- Internal resilience, `IDisposable` resource cleanup, HTTP `Retry-After` header parsing, and atomic file persistence in `src/AutonomousAgentHub.Core/Infrastructure/`.
- Expanded secret scanning patterns in `Stage6AuditorAgent` and prompt injection markers in `GeminiFreeTierClient`.
- Non-breaking compile-time JSON source generation (`JsonSerializerContext`) and structured JSONL telemetry.

### Tier 2 (Requires Single-Gate Human Approval / Consolidated PR)
- Changes to target repository allowlists or global thresholds (`minimumAuditScore`, `maxSelfHealingAttempts`) in `config/repositories.json`.
- Breaking modifications to JSON property names in `src/AutonomousAgentHub.Core/Contracts/` or `state/rotation-state.json`.
- Altering the 7-stage execution sequence in `SevenStageOrchestrator.cs`.
- Adding external third-party NuGet dependencies to `src/AutonomousAgentHub.Core/AutonomousAgentHub.Core.csproj`.
- Modifying production cron schedules or permissions in `.github/workflows/daily-autonomous-orchestrator.yml`.

