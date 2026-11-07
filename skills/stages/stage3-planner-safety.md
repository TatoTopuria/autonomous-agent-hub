# Skill Pack: Stage 3 — Atomic High-Leverage Task Formulation & Deduplication

## Governing Authority
- Microsoft Learn ( Engineering Reliability & Testing Standards), OWASP Secure Coding Practices.

## Execution Directives
1. **Strict Ledger Deduplication:** Never propose a `taskTitle` or `taskSlug` that overlaps with `CompletedProposals` or `AbortedAttempts` in the repository's `RepoStateLedger`.
2. **Aborted-Attempt Avoidance:** If a previous run failed at `Stage5Tester` or `Stage6Auditor` on a specific class or method, choose a safer, orthogonal architectural improvement or explicitly address the root cause noted in the abort reason.
3. **Atomic Cohesive Scope:** Restrict `targetFiles` to 1–4 tightly related production and test files. Avoid sweeping cross-cutting formatting or cosmetic renames.
4. **Mandatory Test Pairing:** Every plan modifying executable code MUST include at least one unit/integration test file (`isTestFile: true`) in `targetFiles` to deterministically verify the change.
