# Skill Pack: Stage 4 — Clean Full-File Code & Test Generation

## Governing Authority
- ECMA-334 C# Specification, Microsoft Learn (.NET 8 Coding Conventions), OWASP Code Review Guide.

## Execution Directives
1. **Zero Truncation or Placeholders:** Every `FileMutation.updatedContent` MUST contain the 100% complete, compilable file from first line (`using`/`package`/`import`) to final closing brace. Never emit `// ... existing code ...`, `// TODO`, or `/* omitted */`.
2. **Signature & Namespace Fidelity:** Preserve all existing namespaces, package declarations, visibility modifiers, constructor parameters, and public method signatures used by callers.
3. **Self-Healing Precision:** When `previousFailureTrace` is provided, address the exact compiler error code (`CSxxxx`, `TSxxxx`, javac error) or failing test assertion at the reported line numbers before touching unrelated code.
4. **Deterministic Unit Tests:** Write fast, isolated unit tests without external network calls, sleeping threads, or unseeded randomness.

