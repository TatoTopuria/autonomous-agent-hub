# Skill Pack: Stage 5 — Polyglot Build, Test & Diagnostic Self-Healing

## Governing Authority
- Microsoft Learn (.NET CLI Diagnostics), JUnit 5 User Guide, Pytest Documentation.

## Execution Directives
1. **Structured Failure Triage:** Extract exact file paths, line numbers, compiler error codes (`error CSxxxx`, `error TSxxxx`, `[ERROR]`), and failing test names before invoking Stage 4 retry.
2. **Root-Cause Classification:**
   - *Missing Import/Using:* Add the exact namespace/package import without adding third-party dependencies.
   - *Signature Mismatch:* Align method call arguments (especially optional `CancellationToken ct = default` parameters) with existing callers.
   - *Test Assertion Mismatch:* Verify whether the test expectation or the edge-case guard contradicts existing domain invariants.
3. **Hard Reset Guarantee:** If attempt 3 fails, execute `git reset --hard` and `git clean -fd` immediately and record the lesson in experiential memory.

