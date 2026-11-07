# Stack Skill Pack: Python 3.11+ (FastAPI / Pydantic v2 / Pytest)

## Official Authorities
- Official Python Documentation (`docs.python.org`) & PEPs (`peps.python.org`)
- FastAPI & Pydantic v2 Official Documentation

## Mandatory Engineering Standards
1. **Strict Type Annotations:** Annotate all function signatures, parameters, and return types using Python 3.11+ union syntax (`str | None`, `list[str]`, `dict[str, Any]`).
2. **Data Validation:** Use Pydantic v2 models (`ConfigDict`, `@field_validator`, `Field(...)`) for strict boundary validation and clinical/analytics domain invariants.
3. **Async Concurrency:** Never run blocking I/O or CPU-heavy tasks directly inside `async def` routes without offloading.
4. **Testing:** Write deterministic `pytest` unit tests using fixtures and `pytest.raises` for boundary validation checks.
