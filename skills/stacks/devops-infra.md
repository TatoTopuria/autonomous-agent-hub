# Stack Skill Pack: Cloud-Native DevOps, Kubernetes, Docker & Observability

## Official Authorities
- Cloud Native Computing Foundation (`cncf.io`) & Official Kubernetes Docs (`kubernetes.io`)
- OpenTelemetry Official Specifications (`opentelemetry.io`)
- CIS Benchmarks & OWASP Container/Kubernetes Security Cheat Sheet

## Mandatory Engineering Standards
1. **Container & Pod Security:** Run containers as non-root (`securityContext.runAsNonRoot: true`, `allowPrivilegeEscalation: false`). Define explicit `resources.requests` and `resources.limits` for CPU and memory.
2. **Docker Compose & IaC Hygiene:** Ensure `docker compose config` validates cleanly across all profiles (`core`, `messaging`, `full`). Never hardcode plaintext secrets; use environment variable interpolation with safe defaults.
3. **Observability & Dashboards:** Validate all Grafana JSON dashboards (`json.load`) and Prometheus/OpenTelemetry YAML configurations for syntactic and schema correctness.
4. **Shell Scripts:** Enforce `set -euo pipefail` and verify syntax with `bash -n`.

