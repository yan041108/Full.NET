# Full.NET Identity Session Foundation Implementation Plan

> **For Codex:** REQUIRED SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task, `superpowers:test-driven-development` for every behavior change, and `fullnet-module-delivery` for the module vertical slice.

**Goal:** Deliver a secure host-administrator login/session foundation with Dapper, SQL Server/MySQL parity, standard ProblemDetails, and equivalent Vue/Layui login flows.

**Architecture:** Add a Core `Full.NET.Modules.Identity` module containing password verification, JWT issuance, refresh-session rotation, CSRF/origin protection, audit persistence, bootstrap service, and four HTTP endpoints. Persist users, sessions, and auth audit through explicit scoped Dapper SQL. Host API owns middleware ordering; both browser clients keep Access Tokens in memory and use the same refresh-cookie contract without sharing UI runtime code.

**Tech Stack:** .NET 10, ASP.NET Core JwtBearer/Identity/RateLimiting, Dapper, DbUp, MSTest, Testcontainers SQL Server/MySQL, System.Text.Json source generation, Vue 3/Pinia/Vitest, native ES Modules/Layui/Vitest, Playwright.

**Approved design:** [`docs/superpowers/specs/2026-07-17-identity-session-foundation-design.md`](../specs/2026-07-17-identity-session-foundation-design.md)

---

## Execution rules

- Work on `codex/identity-session-foundation` in `.worktrees/identity-session-foundation`.
- Before each production change, add the smallest failing test and record the expected failure.
- All handwritten C# and JavaScript/TypeScript comments are Chinese; identifiers remain English.
- Do not commit a password, refresh token, CSRF token, RSA private key, or generated development secret.
- Every persistence behavior must pass on SQL Server and MySQL before it is complete.
- Commit after each task only when its targeted checks pass.

### Task 1: Register the Identity module boundary

**Files:**

- Create: `src/Modules/Full.NET.Modules.Identity/Full.NET.Modules.Identity.csproj`
- Create: `src/Modules/Full.NET.Modules.Identity/IdentityModule.cs`
- Create: `src/Modules/Full.NET.Modules.Identity/Properties/AssemblyInfo.cs`
- Modify: `Directory.Packages.props`
- Modify: `Full.NET.slnx`
- Modify: `src/Hosts/Full.NET.Host.Api/Full.NET.Host.Api.csproj`
- Modify: `src/Hosts/Full.NET.Host.Api/Program.cs`
- Modify: `src/Hosts/Full.NET.Host.Migrator/Full.NET.Host.Migrator.csproj`
- Modify: `src/Hosts/Full.NET.Host.Migrator/Program.cs`
- Modify: `tests/Full.NET.ArchitectureTests/Full.NET.ArchitectureTests.csproj`
- Modify: `tests/Full.NET.ArchitectureTests/DependencyRulesTests.cs`
- Modify: `tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj`

**Step 1: Write the failing architecture test**

Add a test that loads `Full.NET.Modules.Identity` and asserts it does not reference `Full.NET.Modules.Tenancy` or any Host assembly. Add the project reference before the project exists and run:

```powershell
dotnet test tests/Full.NET.ArchitectureTests/Full.NET.ArchitectureTests.csproj --no-restore
```

Expected: FAIL because the Identity project/type is missing.

**Step 2: Add the minimal module project**

Use the Tenancy project as the dependency ceiling. Add `Microsoft.AspNetCore.App`, Abstractions, Data.Abstractions, Hosting, Modularity and FluentValidation references, plus `Microsoft.AspNetCore.Authentication.JwtBearer`. Add central version `10.0.10`. `IdentityModule` initially registers JSON options and maps no endpoints.

Register `IdentityModule` in API and Migrator, add `UseAuthentication()`, `UseAuthorization()` and `UseRateLimiter()` in the correct order, and add the project to `Full.NET.slnx`.

**Step 3: Verify and commit**

```powershell
dotnet restore Full.NET.slnx
dotnet test tests/Full.NET.ArchitectureTests/Full.NET.ArchitectureTests.csproj --no-restore
dotnet build Full.NET.slnx --no-restore
git add Directory.Packages.props Full.NET.slnx src tests/Full.NET.ArchitectureTests tests/Full.NET.UnitTests
git commit -m "feat: add identity module boundary"
```

### Task 2: Add provider-equivalent Identity migrations

**Files:**

- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer/002_Identity.sql`
- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/MySql/002_Identity.sql`
- Modify: `tests/Full.NET.IntegrationTests/Migrations/SqlServerMigrationTests.cs`
- Modify: `tests/Full.NET.IntegrationTests/Migrations/MySqlMigrationTests.cs`

**Step 1: Write failing schema assertions**

For each provider assert the existence of:

- `fn_identity_user` and unique `(ScopeKey, NormalizedUsername)`;
- `fn_identity_refresh_session` and unique `TokenHash` plus family/user indexes;
- `fn_identity_auth_audit` and occurred/user indexes.

Also run migration twice and assert the second run executes zero scripts.

```powershell
dotnet test tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~MigrationTests"
```

Expected: FAIL because migration `002_Identity.sql` and tables are missing.

**Step 2: Implement both migrations**

Create provider-equivalent columns defined by the approved design. Use `datetimeoffset(7)`/`uniqueidentifier` for SQL Server and UTC `datetime(6)`/`char(36)` for MySQL. Store token hashes and username fingerprints as fixed lowercase hexadecimal ASCII. Use restrictive foreign keys; do not cascade-delete audit history.

**Step 3: Verify and commit**

```powershell
dotnet test tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~MigrationTests"
git add src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations tests/Full.NET.IntegrationTests/Migrations
git commit -m "feat: add identity database schema"
```

### Task 3: Implement identity options, password policy and bootstrap

**Files:**

- Create: `src/Modules/Full.NET.Modules.Identity/Domain/IdentityUser.cs`
- Create: `src/Modules/Full.NET.Modules.Identity/Configuration/IdentityOptions.cs`
- Create: `src/Modules/Full.NET.Modules.Identity/Configuration/IdentityOptionsValidator.cs`
- Create: `src/Modules/Full.NET.Modules.Identity/Security/IdentityPasswordPolicy.cs`
- Create: `src/Modules/Full.NET.Modules.Identity/Persistence/IdentitySql.cs`
- Create: `src/Modules/Full.NET.Modules.Identity/Persistence/IdentityUserRecord.cs`
- Create: `src/Modules/Full.NET.Modules.Identity/Contracts/IIdentityBootstrapService.cs`
- Create: `src/Modules/Full.NET.Modules.Identity/Features/Bootstrap/IdentityBootstrapService.cs`
- Create: `tests/Full.NET.UnitTests/Identity/IdentityPasswordPolicyTests.cs`
- Create: `tests/Full.NET.UnitTests/Identity/IdentityOptionsValidatorTests.cs`
- Create: `tests/Full.NET.UnitTests/Identity/IdentityBootstrapServiceTests.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/IdentityModule.cs`
- Modify: `src/Hosts/Full.NET.Host.Migrator/Program.cs`
- Modify: `src/Hosts/Full.NET.Host.Migrator/appsettings.json`

**Step 1: Write failing unit tests**

Cover password length/character classes, normalized host username, option defaults, production key/cookie validation, idempotent bootstrap and refusal to overwrite an existing user. Use NSubstitute executors and a fixed `IClock`/`IIdGenerator`.

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj --no-restore --filter "FullyQualifiedName~Identity"
```

Expected: FAIL because domain, options, policy and service do not exist.

**Step 2: Implement the minimum behavior**

Use `IPasswordHasher<IdentityUser>` and store only its returned hash. `IIdentityBootstrapService.BootstrapHostAdminAsync` must run inside `ICommandTransaction`, insert exactly once, and return a typed outcome for created/already-exists. Migrator reads `Identity:Bootstrap:Username` and `Identity:Bootstrap:Password`; neither value is logged. If only one value is present, fail with a safe configuration message. If both are absent, log an actionable warning and continue without an account.

**Step 3: Verify and commit**

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj --no-restore --filter "FullyQualifiedName~Identity"
git add src/Modules/Full.NET.Modules.Identity src/Hosts/Full.NET.Host.Migrator tests/Full.NET.UnitTests/Identity
git commit -m "feat: bootstrap secure host identity"
```

### Task 4: Implement JWT, refresh-token and CSRF security primitives

**Files:**

- Create: `src/Modules/Full.NET.Modules.Identity/Security/IAccessTokenIssuer.cs`
- Create: `src/Modules/Full.NET.Modules.Identity/Security/RsaSigningKeyRing.cs`
- Create: `src/Modules/Full.NET.Modules.Identity/Security/JwtAccessTokenIssuer.cs`
- Create: `src/Modules/Full.NET.Modules.Identity/Security/IRandomTokenGenerator.cs`
- Create: `src/Modules/Full.NET.Modules.Identity/Security/CryptographicTokenGenerator.cs`
- Create: `src/Modules/Full.NET.Modules.Identity/Security/TokenHash.cs`
- Create: `src/Modules/Full.NET.Modules.Identity/Security/CsrfTokenValidator.cs`
- Create: `src/Modules/Full.NET.Modules.Identity/Security/AllowedOriginValidator.cs`
- Create: `src/Modules/Full.NET.Modules.Identity/Security/IdentityClaimTypes.cs`
- Create: `tests/Full.NET.UnitTests/Identity/JwtAccessTokenIssuerTests.cs`
- Create: `tests/Full.NET.UnitTests/Identity/TokenHashTests.cs`
- Create: `tests/Full.NET.UnitTests/Identity/CsrfTokenValidatorTests.cs`
- Create: `tests/Full.NET.UnitTests/Identity/AllowedOriginValidatorTests.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/IdentityModule.cs`

**Step 1: Write failing primitive tests**

Assert 32-byte refresh entropy, deterministic lowercase SHA-256 hashes, constant-time CSRF comparison behavior, exact-origin matching, JWT `kid` and all required claims, 10-minute default expiry, and rejection of missing production key configuration.

Expected failing command:

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj --no-restore --filter "FullyQualifiedName~JwtAccessTokenIssuerTests|FullyQualifiedName~TokenHashTests|FullyQualifiedName~CsrfTokenValidatorTests|FullyQualifiedName~AllowedOriginValidatorTests"
```

**Step 2: Implement and register primitives**

Generate an ephemeral 3072-bit RSA key only in Development/Testing when no configured key exists, and log only its KeyId. Configure JwtBearer to validate signature, issuer, audience, lifetime and required signing key; set `MapInboundClaims=false`. Production must fail validation without an active RSA private key and verification key ring.

**Step 3: Verify and commit**

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj --no-restore --filter "FullyQualifiedName~Identity"
git add src/Modules/Full.NET.Modules.Identity tests/Full.NET.UnitTests/Identity
git commit -m "feat: add identity token security primitives"
```

### Task 5: Deliver login and current-user API vertically

**Files:**

- Create: `src/Modules/Full.NET.Modules.Identity/Contracts/TokenResponse.cs`
- Create: `src/Modules/Full.NET.Modules.Identity/Contracts/CurrentUserResponse.cs`
- Create: `src/Modules/Full.NET.Modules.Identity/Domain/RefreshSession.cs`
- Create: `src/Modules/Full.NET.Modules.Identity/Domain/AuthAuditEvent.cs`
- Create: `src/Modules/Full.NET.Modules.Identity/Features/Login/Command.cs`
- Create: `src/Modules/Full.NET.Modules.Identity/Features/Login/LoginCommandValidator.cs`
- Create: `src/Modules/Full.NET.Modules.Identity/Features/Login/Handler.cs`
- Create: `src/Modules/Full.NET.Modules.Identity/Features/Login/Endpoint.cs`
- Create: `src/Modules/Full.NET.Modules.Identity/Features/GetCurrentUser/Endpoint.cs`
- Create: `src/Modules/Full.NET.Modules.Identity/Http/IdentityCookieWriter.cs`
- Create: `src/Modules/Full.NET.Modules.Identity/Http/ClientRequestContext.cs`
- Create: `src/Modules/Full.NET.Modules.Identity/Serialization/IdentityJsonSerializerContext.cs`
- Create: `tests/Full.NET.UnitTests/Identity/LoginCommandValidatorTests.cs`
- Create: `tests/Full.NET.UnitTests/Identity/LoginHandlerTests.cs`
- Create: `tests/Full.NET.UnitTests/Identity/IdentityJsonSerializerContextTests.cs`
- Create: `tests/Full.NET.IntegrationTests/Api/IdentityApiAssertions.cs`
- Create: `tests/Full.NET.IntegrationTests/Api/IdentityApiSqlServerTests.cs`
- Create: `tests/Full.NET.IntegrationTests/Api/IdentityApiMySqlTests.cs`
- Modify: `tests/Full.NET.IntegrationTests/Api/FullNetApiFactory.cs`
- Modify: `tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj`
- Modify: `src/Modules/Full.NET.Modules.Identity/IdentityModule.cs`

**Step 1: Write failing handler and API tests**

Unit tests must first fail for unknown/wrong/disabled/locked accounts sharing `identity.invalid_credentials`, fifth failure locking for 15 minutes, successful reset, transactional audit/session creation, and no secret in the result error.

Provider API tests bootstrap a random strong test password, then assert login sets both cookies, returns only Access Token fields, authenticated `/api/v1/me` exposes safe claims, invalid login returns ProblemDetails, and audit rows exist.

**Step 2: Implement minimum login flow**

Use explicit `IdentitySql` statements with `SqlDataScope.HostOnly`, `ICommandTransaction`, `IPasswordHasher`, `IClock`, `IIdGenerator`, `IAccessTokenIssuer` and `IRandomTokenGenerator`. Add the rate-limit policy, exact Origin validation, secure cookie writer, source-generated JSON metadata and endpoint metadata. Never log the command.

**Step 3: Run both providers and commit**

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj --no-restore --filter "FullyQualifiedName~Identity"
dotnet test tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~IdentityApi"
git add src/Modules/Full.NET.Modules.Identity tests/Full.NET.UnitTests/Identity tests/Full.NET.IntegrationTests
git commit -m "feat: add identity login api"
```

### Task 6: Deliver refresh rotation, reuse detection and logout

**Files:**

- Create: `src/Modules/Full.NET.Modules.Identity/Features/RefreshSession/Command.cs`
- Create: `src/Modules/Full.NET.Modules.Identity/Features/RefreshSession/Handler.cs`
- Create: `src/Modules/Full.NET.Modules.Identity/Features/RefreshSession/Endpoint.cs`
- Create: `src/Modules/Full.NET.Modules.Identity/Features/Logout/Command.cs`
- Create: `src/Modules/Full.NET.Modules.Identity/Features/Logout/Handler.cs`
- Create: `src/Modules/Full.NET.Modules.Identity/Features/Logout/Endpoint.cs`
- Create: `tests/Full.NET.UnitTests/Identity/RefreshSessionHandlerTests.cs`
- Create: `tests/Full.NET.UnitTests/Identity/LogoutHandlerTests.cs`
- Modify: `tests/Full.NET.IntegrationTests/Api/IdentityApiAssertions.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/IdentityModule.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/Persistence/IdentitySql.cs`

**Step 1: Write failing rotation tests**

Cover valid rotation, expired/revoked token rejection, CSRF failure before persistence, logout idempotency, exactly one winner for concurrent refresh, old-token reuse revoking the family, and a later new-token refresh failing after family revocation.

**Step 2: Implement conditional transactions**

Consume with `WHERE ConsumedAtUtc IS NULL AND RevokedAtUtc IS NULL AND Version=@Version`. If the conditional update affects zero rows, re-read inside the transaction; a previously consumed token triggers family revocation and `identity.refresh_token_reuse_detected`. Successful refresh creates the replacement before returning cookies. Logout revokes the presented active session if found and always clears both cookies.

**Step 3: Run both providers and commit**

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj --no-restore --filter "FullyQualifiedName~RefreshSessionHandlerTests|FullyQualifiedName~LogoutHandlerTests"
dotnet test tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~IdentityApi"
git add src/Modules/Full.NET.Modules.Identity tests
git commit -m "feat: rotate and revoke identity sessions"
```

### Task 7: Add the Vue login/session flow

**Files:**

- Create: `packages/client-contracts/src/identity.ts`
- Create: `packages/client-contracts/tests/identity.test.ts`
- Modify: `packages/client-contracts/src/index.ts`
- Create: `ui/admin/src/auth/session.ts`
- Create: `ui/admin/src/auth/session.test.ts`
- Create: `ui/admin/src/views/LoginView.vue`
- Create: `ui/admin/src/views/LoginView.test.ts`
- Modify: `ui/admin/src/api/http.ts`
- Modify: `ui/admin/src/api/http.test.ts`
- Modify: `ui/admin/src/App.vue`
- Modify: `ui/admin/src/App.test.ts`
- Modify: `ui/admin/src/main.ts`
- Modify: `ui/admin/src/styles/app.css`

**Step 1: Write failing contract/session tests**

Define `TokenResponse`, `CurrentUserResponse` and runtime guards in shared contracts. Test that Vue keeps the token only in module memory, adds Bearer, sends CSRF from the cookie, deduplicates concurrent 401 refreshes, retries once, never refreshes the refresh request, clears state on failure, and never writes LocalStorage/SessionStorage.

**Step 2: Implement the Pinia session and login UI**

The store exposes `restore`, `login`, `logout`, `currentUser` and state `initializing/authenticated/anonymous`. `App.vue` renders a neutral boot state, `LoginView`, or the admin shell. Replace hard-coded operator data with `/me` data and add a real logout button. Preserve current clean-room design tokens and responsive behavior.

**Step 3: Verify and commit**

```powershell
pnpm --filter @fullnet/client-contracts test
pnpm --filter @fullnet/admin test
pnpm --filter @fullnet/admin typecheck
pnpm --filter @fullnet/admin build
git add packages/client-contracts ui/admin
git commit -m "feat: add vue admin identity session"
```

### Task 8: Add the equivalent Layui login/session flow

**Files:**

- Create: `ui/admin-layui/js/core/session.js`
- Create: `ui/admin-layui/tests/session.test.js`
- Modify: `ui/admin-layui/js/core/http.js`
- Modify: `ui/admin-layui/tests/http.test.js`
- Modify: `ui/admin-layui/js/app.js`
- Modify: `ui/admin-layui/js/main.js`
- Modify: `ui/admin-layui/index.html`
- Modify: `ui/admin-layui/css/app.css`
- Modify: `ui/admin-layui/tests/app.test.js`

**Step 1: Write failing native-session tests**

Reuse the same scenario list as Vue: memory-only token, Bearer, CSRF, one shared refresh promise, one retry, failed refresh cleanup, login, `/me`, logout and no SPA runtime dependency.

**Step 2: Implement with native ES Modules**

Create a closure-owned session controller and DOM login form. Toggle login, boot and shell regions using `hidden`/ARIA state. Use Layui only for progressive form/layer enhancement; core auth must work when `globalThis.layui` is absent. Do not add Vue, React or copy layuiAdmin assets/source.

**Step 3: Verify and commit**

```powershell
pnpm --filter @fullnet/admin-layui test
pnpm --filter @fullnet/admin-layui build
node tests/client-workspace.test.mjs
git add ui/admin-layui tests/client-workspace.test.mjs
git commit -m "feat: add layui admin identity session"
```

### Task 9: Prove dual-admin parity and update operational docs

**Files:**

- Modify: `tests/e2e/admin-parity/tests/shell-parity.spec.mjs`
- Modify: `docs/development/getting-started.md`
- Modify: `docs/roadmap/client-delivery-roadmap.md`
- Modify: `docs/roadmap/adminnet-feature-parity.md`
- Modify: `README.md`
- Modify: `src/Hosts/Full.NET.Host.Api/appsettings.json`
- Modify: `src/Hosts/Full.NET.Host.Api/appsettings.Development.json`
- Modify: `src/Hosts/Full.NET.Host.Migrator/appsettings.json`

**Step 1: Add failing E2E parity scenarios**

For both Playwright projects test startup refresh success, startup refresh failure showing login, successful login, invalid ProblemDetails display, authenticated current user, and logout returning to login. Assert no token appears in LocalStorage/SessionStorage.

**Step 2: Complete configuration and docs**

Document secret-based bootstrap, RSA production requirements, development ephemeral key behavior, allowed origins, Cookie/HTTPS requirement, curl/manual smoke flow and next RBAC slice. Update both client columns independently; do not mark tenant switching or complete permission navigation verified.

**Step 3: Verify and commit**

```powershell
pnpm test:e2e
pnpm test:workspace
git add tests/e2e docs README.md src/Hosts
git commit -m "docs: verify identity session delivery"
```

### Task 10: Full verification, review and evolution audit

**Files:**

- Modify only if thresholds are met: `rules/*.md`
- Modify only if reuse evidence is met: `.agents/skills/fullnet-module-delivery/**`

**Step 1: Run fresh complete checks**

```powershell
dotnet restore Full.NET.slnx
dotnet build Full.NET.slnx --no-restore
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj --no-build
dotnet test tests/Full.NET.CompatibilityTests/Full.NET.CompatibilityTests.csproj --no-build
dotnet test tests/Full.NET.ArchitectureTests/Full.NET.ArchitectureTests.csproj --no-build
dotnet test tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj --no-build
pnpm test:workspace
pnpm test:clients
pnpm build:clients
pnpm test:e2e
python tests/skills/validate_project_skills.py
git diff --check
git status --short --branch
```

Record exact counts and any Docker/platform exclusions. A skipped provider is not a pass.

**Step 2: Perform security and scope review**

Search diffs for password/token/private-key values, console logging of requests, LocalStorage/SessionStorage token writes, anonymous `/me`, missing SQL scopes, provider-specific unguarded SQL, copied Admin.NET/layuiAdmin assets, English handwritten comments, and undocumented public-contract changes.

```powershell
git diff main...HEAD --check
git diff --stat main...HEAD
rg -n "localStorage|sessionStorage|Password|RefreshToken|PRIVATE KEY|Console\.Write|AllowAnonymous" src ui packages tests docs
```

Inspect every match; do not rely on the search result alone.

**Step 3: Run rule and Skill evolution retrospectives**

Read `rules/rule-evolution.md` and `rules/skill-evolution.md`. Update a rule only when the documented recurrence/evidence threshold is met. Update the project Skill only through its contract test and validation workflow; otherwise record “no promotion” in the delivery summary.

**Step 4: Request review, fix findings, and commit**

Use `superpowers:requesting-code-review`; because this repository currently forbids unrequested subagents, perform the checklist locally unless the user explicitly authorizes subagents. Apply `superpowers:receiving-code-review` to actionable findings, rerun affected checks, then:

```powershell
git add --all
git commit -m "test: verify identity session foundation"
```

Skip the commit if no files changed.

### Task 11: Merge locally and clean up

**Step 1: Verify branch relationship and main cleanliness**

```powershell
git status --short --branch
git log --oneline --decorate -10
```

**Step 2: Merge using the repository-approved local workflow**

Switch to the main worktree, fast-forward or merge `codex/identity-session-foundation`, rerun the smoke suite on `main`, then remove the worktree and delete the merged local branch. Never delete the worktree before verifying that `main` contains every implementation commit.

**Step 3: Final evidence**

```powershell
git status --short --branch
git branch --merged main
git worktree list
git log --oneline --decorate -12
```

The final report must include the delivered API/client behavior, exact verification results, remaining RBAC/tenant-switch scope, rule/Skill evolution result, final main commit and cleanup state.
