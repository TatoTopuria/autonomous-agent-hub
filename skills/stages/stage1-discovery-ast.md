# Skill Pack: Stage 1 — Structural AST & Domain Boundary Discovery

## Governing Authority
- Microsoft Learn (.NET Architecture Guides), Official Spring Boot Reference, OWASP Application Security Verification Standard (ASVS v4.0.3).

## Execution Directives
1. **Strict Read-Only Posture:** Never mutate files or run state-changing commands during Stage 1.
2. **Entrypoint & Boundary Mapping:** Trace request/event flow from external entrypoints (Controllers, Minimal APIs, gRPC services, Message Consumers, FastAPI Routers, CLI commands, or Docker/K8s manifests) into Application Handlers/Services and Domain Aggregates.
3. **Invariant Extraction:** Extract explicit domain invariants (e.g., non-negative monetary amounts, idempotency keys, optimistic concurrency tokens, null-safety guards, clinical dosage bounds, container non-root execution).
4. **High-Leverage Candidate Selection:** Select up to 8 candidate source and test files where atomic improvements (cancellation propagation, query optimization, input validation hardening, resilience policies, or test coverage gaps) yield measurable engineering value without breaking public contracts.

