# Stack Skill Pack: .NET 8 / .NET 9 & C# 12+

## Official Authorities
- Microsoft Learn (`learn.microsoft.com/en-us/dotnet/`)
- ECMA-334 C# Language Specification

## Mandatory Engineering Standards
1. **Language & Nullability:** Target C# 12+ idioms (file-scoped namespaces, primary constructors where appropriate, collection expressions `[..]`). Strictly honor `<Nullable>enable</Nullable>` and `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`.
2. **Async & Cancellation:** Pass `CancellationToken cancellationToken = default` through all asynchronous call chains. Never block async code with `.Result` or `.Wait()`. Use `.ConfigureAwait(false)` in non-UI library/infrastructure methods.
3. **Entity Framework Core:** Apply `.AsNoTracking()` on all read-only queries. Always use parameterized LINQ or `FromSqlInterpolated`.
4. **Testing:** Use xUnit (`[Fact]`, `[Theory]`, `[InlineData]`) with deterministic in-memory fakes or temporary directories (`IDisposable`).
