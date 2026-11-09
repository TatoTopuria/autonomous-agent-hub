# Learned Heuristics & Anti-Pattern Playbook (Companion Ledger)

> Synchronized with `state/experiential-memory.json` by the Autonomous Agent Hub RSI Engine.

## Active Heuristics (`LH-xxx`)
- **[LH-001 | All]** Every production code modification MUST be paired with at least one deterministic unit or regression test file in `targetFiles` and `fileMutations`.
- **[LH-002 | DotNet]** Propagate `CancellationToken` through all async methods, enforce `ConfigureAwait(false)` in library/infrastructure code, and apply `AsNoTracking()` on read-only EF Core queries.
- **[LH-003 | JavaGradle]** Use constructor injection, `@Transactional(readOnly = true)` for query methods, Jakarta Bean Validation annotations on request DTOs, and AssertJ in JUnit 5 tests.
- **[LH-004 | DevOpsInfra]** Enforce non-root container execution (`securityContext.runAsNonRoot: true`), explicit CPU/memory requests and limits, valid Grafana/OpenTelemetry JSON schemas, and `bash -n` syntax verification.

## Forbidden Anti-Patterns (`AP-xxx`)
- **[AP-001 | All]** Never emit truncation placeholders (`// ... existing code ...`, `/* omitted */`, `// TODO`) in `FileMutation.updatedContent`. Always output complete, compilable files.
- **[AP-002 | All]** Never cite unofficial blogs (Medium, Reddit, StackOverflow, dev.to, Baeldung, DZone) in Stage 2 `EvidenceBlocks`.

