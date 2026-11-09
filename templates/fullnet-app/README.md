# Full.NET Application Template

This template creates an independently buildable Full.NET application with a versioned framework source bundle under `framework/fullnet/`.

New applications expose their own root commands: `pnpm dev:api`, `pnpm dev:worker`, `pnpm dev:admin`, `pnpm build:api`, `pnpm build:worker`, `pnpm build:migrator`, `pnpm build:admin`, and `pnpm typecheck:admin`. Run `pnpm db:migrate` to migrate without seeding, or explicitly choose `pnpm db:seed:baseline`. `pnpm generate:crud --schema schema.json --workspace .` previews CRUD; append `--apply` only when writing is intended. `pnpm generate:client` and `pnpm check:client` invoke the shipped OpenAPI tools and accept their explicit input/manifest/output options. Configure dependencies and secrets before starting hosts; these commands do not provision infrastructure. Dependency versions, workspace safety overrides, and patch files remain frozen with the package; repository-only tests and release commands are excluded.

From the Full.NET source checkout, assemble the template package into an empty output directory.

```bash
node scripts/templates/build-app-template.mjs --output ./fullnet-app-package
```

For a release candidate, use `--source-commit <full HEAD SHA>` to select the committed tree explicitly. The packager requires its own tools to match the commit and takes the source and template files from Git. Record the printed `distribution-manifest.json` SHA-256 with the candidate's commit. The package contains the distribution and framework manifests, the MIT license and third-party notices.

## Parameters

| Symbol | Description | Default |
| --- | --- | --- |
| `name` | Application name | `App` |
| `owner-key` | Application owner key for naming profile (`^[a-z][a-z0-9]{1,11}$`, excluding `fn` and `sys`) | `demo` |
| `database` | `sqlserver` or `mysql` | `sqlserver` |
| `preset` | `minimal`, `platform`, `saas`, or `enterprise` runtime module preset | `minimal` |
| `http-port` | Local HTTP port | `5180` |
| `worker-http-port` | Worker health HTTP port, distinct from `http-port`; validated creator chooses the adjacent port automatically | `5181` |

## Create an app

Use the package's validated creator so invalid owner keys, modified or unlisted framework files, and existing output paths are rejected before instantiation. It builds in a temporary sibling directory and publishes the app only after verification succeeds:

```bash
node fullnet-app-package/.fullnet-tools/create-app.mjs --output Demo --name Demo --owner-key demo --database mysql --preset platform --http-port 5180
```

To consume a fixed candidate, use the creator from a trusted Full.NET checkout and the expected manifest SHA-256 obtained from the release record. The creator verifies outer template files and the inner framework inventory before calling `dotnet new`.

```bash
node scripts/templates/create-app.mjs --package ./fullnet-app-package --distribution-sha256 "$MANIFEST_SHA256" --output Demo --name Demo --owner-key acme --database sqlserver --preset minimal --http-port 5180
```

The creator uses an isolated `dotnet new` template hive and projects the selected modules into the generated Composition source and project references. It selects a Worker health port adjacent to the API port, using 65534 when the API uses 65535. Direct `dotnet new fullnet-app` installation requires distinct `--http-port` and `--worker-http-port` values. Use the validated creator for integrity checks and Composition projection.

Before publishing the created directory, the structure verifier requires the sole API host's matching application Composition project and `ApplicationModuleCatalog.cs`. Required application, host, and Composition paths must be files; directories cannot satisfy these checks. This verifies layout only; compilation and runtime acceptance remain separate gates.

If the existing `src` path cannot be read as a directory, verification returns a failed result with a source-directory diagnostic. The CLI prints the diagnostics and exits with code 1; verification does not repair or overwrite the path.

The verifier also parses the root, API, Worker, and Migrator host `appsettings.json` files and requires their `FullNet:Modules:Preset` and `Database:Provider` values to match the frozen preset and database provider in `fullnet-app.json`. This checks the generated files; environment-specific overrides still require runtime diagnostics.

## Layout

- `framework/fullnet/` — managed framework source bundle and `framework-manifest.json`
- `src/FullNetAppNameToken.Host.Api/` — application-owned API host
- `src/FullNetAppNameToken.Host.Worker/` — application-owned Worker host using the bundled framework processing pipeline
- `src/FullNetAppNameToken.Host.Migrator/` — application-owned one-shot migration and explicit seed host
- `src/FullNetAppNameToken.Composition/` — application-owned static business module catalog; references the managed framework Composition
- `ui/admin/` — application-owned Vue administration UI
- `packages/` and `pnpm-lock.yaml` — local workspace dependencies and their locked third-party versions
- `fullnet-app.json` — frozen application owner key, database provider, and preset
- `src/FullNetAppNameToken.Host.Api/appsettings.json` — runtime configuration including `FullNet:Modules`

After generation, build the API and Worker with `dotnet build src/<name>.Host.Api/<name>.Host.Api.csproj -c Release` and `dotnet build src/<name>.Host.Worker/<name>.Host.Worker.csproj -c Release`. Build the Vue UI with `pnpm install --filter '@fullnet/admin...' --frozen-lockfile` followed by `pnpm --filter @fullnet/admin build` from the generated app root. Secrets are not generated by the template; configure connection strings and signing keys in local user secrets or environment-specific settings.

Run `pnpm run diagnose:development` from the generated app root to check its host layout, frozen preset and database provider, selected module references, and configuration placeholders. The application-owned Node entry first probes the SDK selected by the app workspace and reports `DIAG_SDK_MISSING` or `DIAG_SDK_INCOMPATIBLE` with exit code 1 before CLR startup, without echoing raw SDK errors or installing anything. With a compatible SDK it delegates to the existing .NET CLI and preserves its exit code. It compares the root, API, and any declared matching Migrator base configuration with the frozen application profile; missing or invalid files return redacted errors. Older applications without a Migrator remain supported. Use `pnpm run diagnose:production` for production configuration checks. Existing application scripts are not automatically replaced. The command reads files and environment metadata without initializing the database and does not validate every ASP.NET Core configuration override.

The generated Composition project references the selected official modules. The source bundle also contains the separate Enterprise Request sample, which requires explicit application composition. Transitive project references, migrations, login and dual-database acceptance remain separate gates.

## Minimal / SQL Server candidate configuration

The source candidate projects Identity, Tenancy, Settings and Organization into the application. `fullnet-app.json` records the selected provider and preset. The generated `framework-manifest.json` freezes the matching migration and seed inventory. Keep these files with the application and its publish output.

| Scope | Configuration keys and secret references |
| --- | --- |
| API, Worker and Migrator | Provide `ConnectionStrings__app` through the runtime secret system, with a role-appropriate SQL Server login, TLS and server certificate validation. Keep `Database__ConnectionName` and the selected provider aligned with the application declaration. |
| Identity | Supply `Identity__ActiveKeyId` and `Identity__SigningKeys__<id>__PublicKeyPem` / `Identity__SigningKeys__<id>__PrivateKeyPem` from the managed signing material. Set `Identity__AllowedOrigins__0` to the exact HTTPS admin origin and `Tenancy__HostDomains__0` to the exact Host name. |
| API and Worker data protection | Set the same `DataProtection__ApplicationName`, a persistent absolute `DataProtection__KeyRingPath`, and `DataProtection__CertificatePath`. Inject `DataProtection__CertificatePassword` from the secret store when the certificate is password protected. |
| Cache and logging | Set `Cache__RedisConnectionString` for shared Redis deployments. For a single-instance production deployment using Local logging, set `FullNet__Logging__DeliveryMode=Local`, `FullNet__Logging__LocalFile__Enabled=true` and a writable persistent `FullNet__Logging__LocalFile__Directory`. |
| Initial baseline seed | Provide `Identity__Bootstrap__Username` and `Identity__Bootstrap__Password` to the explicit seed command when provisioning the first administrator. |

Use the actual target settings with `pnpm diagnose:production`. For an existing database, apply the upgrade gates before `pnpm db:migrate`; for initial provisioning, run `pnpm db:seed:baseline` as a separate step when required. Start Worker before API, confirm the readiness probes, and check the published admin frontend through its configured HTTPS origin. Candidate evidence records separately which of these runtime checks were executed.

Declare business modules in the application-owned `ApplicationModuleCatalog.CreateModules()` and reference their projects from the application Composition project. API, Worker, and Migrator call this catalog with their respective profiles; it combines the frozen official preset with the explicit application list and validates dependencies before registration. Migrator registers only migration and seed capabilities. Worker compiles the bundled framework Worker processing pipeline and uses `ApplicationWorkerModuleCatalog` to register the application modules. This keeps Outbox and messaging behavior aligned with the bundled framework version; the generated Worker is JIT only until its own Native AOT publish and runtime path are verified. For CLI integration, target this application-owned project and catalog, not files under `framework/fullnet/`. Application migrations and full generated-business acceptance still require their own verification.

Run `dotnet run --project src/<name>.Host.Migrator -- --seed baseline` from the application root. Omit `--seed` to migrate only. Development overlay requires an explicitly selected Development environment; Production permits Baseline only. This host reuses `Full.NET.Hosting.Migrator` for lifecycle, cancellation, exit codes, and migration-before-seed ordering. It currently executes only the frozen framework migration inventory; generated business SQL drafts are not automatically registered. Existing applications must explicitly adopt the new application-owned host; framework source upgrades do not create or replace it.

Application optional contract dependencies must name a known official contract source or a module in the explicit application list, and must not also be required dependencies. An uninstalled official source remains valid; optional contracts do not install modules or enter the required dependency graph. Invalid declarations are rejected before any module service registration for all three host profiles.

A module instance's `Name` is its exact registry key. Path separators, NUL characters and leading or trailing whitespace are rejected before registration; the registry never trims or renames a module key.

The package manifest initially records paired SQL Server/MySQL scripts with `selectionStatus: unscoped`; creation freezes the selected preset inventory and its seed contributors. The application Migrator executes that selected framework inventory. Business scripts still require explicit ownership, numbering, recovery review, and dual-database verification before registration.

New application packages include `.fullnet-tools/openapi/generate-fullnet-client.mjs` and its readiness validator, with the default OpenAPI snapshot and public-operation manifest under `contracts/openapi/`. Run `node .fullnet-tools/openapi/generate-fullnet-client.mjs --check` against the application's frozen client baseline, or supply explicit `--input`, `--manifest` and `--output` paths for business contracts. All four files originate from the same hashed source bundle. Existing applications must explicitly adopt these application-owned copies; framework source upgrades do not overwrite them. This tool distribution does not certify generated business Vue integration or page behavior.
