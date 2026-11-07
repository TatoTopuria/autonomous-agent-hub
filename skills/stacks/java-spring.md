# Stack Skill Pack: Java 21 & Spring Boot 3 (Maven / Gradle)

## Official Authorities
- Official Spring Framework & Spring Boot Documentation (`docs.spring.io`)
- Oracle / OpenJDK Java 21 Specifications (`docs.oracle.com`, `openjdk.org`)
- Jakarta EE Specifications (`jakarta.ee`)

## Mandatory Engineering Standards
1. **Dependency Injection & Immutability:** Use constructor injection (`private final` fields) or Java `record` DTOs. Avoid field `@Autowired`.
2. **Persistence & Transactions:** Annotate read-only service/query methods with `@Transactional(readOnly = true)`. Prevent N+1 query cascades and enforce optimistic locking (`@Version`) on financial/inventory aggregates.
3. **Validation & Financial Precision:** Use `java.math.BigDecimal` with explicit `RoundingMode` for monetary calculations (PCI-DSS / ISO-20022). Enforce Jakarta Bean Validation (`@NotNull`, `@Positive`, `@Valid`) at controller/service boundaries.
4. **Testing:** Write deterministic JUnit 5 (`@Test`, `@ParameterizedTest`) and AssertJ assertions.
