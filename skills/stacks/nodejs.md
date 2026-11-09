# Stack Skill Pack: Node.js 20 LTS & TypeScript Strict Mode

## Official Authorities
- Official Node.js Documentation (`nodejs.org`)
- Official TypeScript Documentation (`typescriptlang.org`)
- ECMA-262 Specification & OWASP API Security Top 10

## Mandatory Engineering Standards
1. **Type Safety:** Respect `strict: true` in `tsconfig.json`. Avoid `any`; use explicit interfaces, discriminated unions, and `unknown` with type narrowing.
2. **Async & Error Handling:** Ensure all `Promise` chains and `async/await` handlers propagate errors cleanly and respect `AbortSignal` timeouts.
3. **Input Validation:** Validate external payloads and environment variables at module boundaries.
4. **Testing:** Add deterministic unit tests compatible with the repository's configured runner (`vitest`, `jest`, or `node --test`).

