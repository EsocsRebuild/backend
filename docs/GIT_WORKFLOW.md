# Professional Git Workflow & Engineering Standards

This document specifies the branching model, commit conventions, code review protocol, and CI/CD release workflow for the backend platform.

---

## 1. Branching Strategy: Scaled Trunk-Based Development

We use **Trunk-Based Development** with short-lived feature branches:

```
main (protected, deployable)
 ├── feat/campaign-scheduling   ──> PR ──> CI (Tiers 1 & 2) ──> Squash & Merge ──> main
 ├── fix/otp-rate-limiting      ──> PR ──> CI (Tiers 1 & 2) ──> Squash & Merge ──> main
 └── release/v1.2.0             ──> Tag v1.2.0 ──> CD Docker Publish to GHCR
```

### Branch Naming Conventions
- `feat/<short-description>`: New features or capabilities (e.g. `feat/email-resend-provider`).
- `fix/<short-description>`: Bug fixes (e.g. `fix/giving-receipt-timezone`).
- `perf/<short-description>`: Performance optimizations (e.g. `perf/hybrid-cache-invalidation`).
- `refactor/<short-description>`: Code reorganization with no behavior changes.
- `chore/<short-description>`: Tooling, dependency updates, or pipeline improvements.
- `hotfix/<short-description>`: Urgent production fixes branching off the release tag.

### Rules
1. **Never commit directly to `main`**: All changes enter through a pull request.
2. **Branch Lifetime**: Branches should live no longer than 1-2 days to avoid merge divergence.
3. **Squash and Merge**: Feature branches are squashed into a single atomic commit upon merging to `main`.

---

## 2. Commit Message Conventions (Conventional Commits)

Commit messages follow the [Conventional Commits v1.0.0](https://www.conventionalcommits.org/) standard:

```
<type>(<scope>): <short description in imperative present tense>

[optional body explaining motivation and architectural rationale]

[optional footer(s), e.g. Closes #123]
```

### Allowed Types
- `feat`: New feature or user capability.
- `fix`: Bug fix.
- `refactor`: Code reorganization with no change to business behavior.
- `perf`: Code change that improves performance.
- `test`: Adding or updating tests without changing production code.
- `docs`: Documentation changes only.
- `ci`: CI/CD workflow, Docker, or Kubernetes configuration.
- `chore`: Maintenance tasks, package version bumps.

### Scopes
Use module names or cross-cutting boundaries:
`identity`, `tenancy`, `people`, `groups`, `events`, `giving`, `content`, `comms`, `email`, `infra`, `api`, `auth`.

### Examples
```
feat(email): integrate Resend and SendGrid multi-provider dispatch
fix(giving): adjust UTC timestamp conversion for donor receipts
test(comms): verify template renderer with liturgical layout
ci(docker): optimize multi-stage build layer caching
```

---

## 3. Pull Request Protocol & Quality Gates

Every pull request must pass three automated tiers in GitHub Actions before merge:

| Tier | Duration | Scope | Triggers |
| :--- | :--- | :--- | :--- |
| **Tier 1: PR Sanity** | `< 90s` | Formatting (`dotnet format`), Compiler check (`TreatWarningsAsErrors`), Architecture Boundary tests, In-memory Unit tests | Every PR push |
| **Tier 2: Integration Gate** | `2-4m` | Live Postgres 17 & Redis 7 services, EF Core migration model parity across all 8 modules, Integration tests | Ready for review / Main push |
| **Tier 3: Security & Audit** | `1-2m` | NuGet vulnerability scanning (`dotnet list package --vulnerable`), Trivy container scanning | Nightly & Release |

---

## 4. Release Cadence & Semantic Versioning

Releases follow [Semantic Versioning 2.0.0](https://semver.org/): `MAJOR.MINOR.PATCH`

- **Patch (`1.0.x`)**: Bug fixes and minor maintenance changes.
- **Minor (`1.x.0`)**: Backwards-compatible new features and module capabilities.
- **Major (`x.0.0`)**: Breaking API contracts or significant architectural migrations.

### Automated Container Release
When a tag matching `v*.*.*` (e.g. `v1.2.0`) is pushed:
1. GitHub Actions triggers `.github/workflows/cd.yml`.
2. Multi-architecture containers (`linux/amd64` and `linux/arm64`) are compiled.
3. Images are signed and pushed to GitHub Container Registry (`ghcr.io`).
4. A GitHub Release is automatically published with categorized change notes.

