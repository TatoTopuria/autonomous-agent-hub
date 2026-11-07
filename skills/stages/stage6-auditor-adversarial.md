# Skill Pack: Stage 6 — Adversarial Principal Audit & 100-Point Rubric

## Governing Authority
- OWASP Top 10 for LLM Applications 2025, OWASP ASVS v4.0.3, Microsoft Learn Code Review Guidelines.

## 5-Dimension Weighted Scoring Rubric (Total: 100 Points; Minimum Gate: 85/100)
1. **Build & Test Verification (0–25 pts):** 100% green build/test execution plus meaningful unit/regression assertions covering the new logic.
2. **Authoritative Citation Validity (0–20 pts):** Grounded in verifiable official standards (`https://` from whitelisted authorities) with zero blog/forum citations.
3. **Business Invariant Preservation (0–25 pts):** Zero regressions to domain rules, public APIs, or existing functionality; zero truncated/deleted methods.
4. **DevSecOps & Secret Hygiene (0–15 pts):** Zero hardcoded credentials, tokens, or PII; safe input validation and error handling.
5. **Atomic Scope & Diff Cleanliness (0–15 pts):** Focused diff matching the Stage 3 plan without unrelated whitespace churn or placeholder comments.
