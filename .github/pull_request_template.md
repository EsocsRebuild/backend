## Description & Context
<!-- Provide a clear summary of what this PR does and why it was created. -->

## Type of Change
- [ ] `feat`: New feature or user capability
- [ ] `fix`: Bug fix
- [ ] `refactor`: Code reorganization with no behavior changes
- [ ] `perf`: Performance improvement
- [ ] `database`: Schema change / EF Core migration
- [ ] `security`: Security enhancement / dependency patch
- [ ] `ci`: Pipeline or Docker configuration update

## Affected Modules
- [ ] `BuildingBlocks` (`SharedKernel`, `Application`, `Infrastructure`, `Web`)
- [ ] `Identity`
- [ ] `Tenancy`
- [ ] `People`
- [ ] `Groups`
- [ ] `Events`
- [ ] `Giving`
- [ ] `Content`
- [ ] `Communications`
- [ ] `DevOps / Kubernetes / Docker`

## Architecture & Quality Checklist
- [ ] **Architecture Boundaries:** Clean hexagonal boundaries maintained; no cross-module database foreign keys or direct module-to-module EF context dependencies.
- [ ] **Multi-Tenant Isolation:** All entities inherit from `TenantAggregateRoot` or `TenantEntity` and respect schema-level isolation.
- [ ] **Migrations Parity:** If entities changed, migrations were generated and `dotnet ef migrations has-pending-model-changes` reports 0 pending drift across all 8 modules.
- [ ] **Compiler Cleanliness:** Builds with `0 Warnings, 0 Errors` under `TreatWarningsAsErrors=true`.
- [ ] **Architecture Tests:** Passed 100% of NetArchTest boundary tests (`Platform.ArchitectureTests`).
- [ ] **Unit Tests:** Passed 100% of fast domain unit tests (`Platform.UnitTests`).
- [ ] **Integration Tests:** Database and real-time Outbox integration tests pass (`Platform.IntegrationTests`).
- [ ] **Security:** No API keys, credentials, or sensitive tokens committed.

## Breaking Changes
- [ ] None
- [ ] Yes (explain migration path or API deprecation details below):

