# Cache policy zero allowlist (2026-08-08)

Snapshot: `architecture-cache-policy-zero-allowlist-20260808`

## Verification

Unit and architecture tests for cache policy registry, tenant resolver, grid preferences, and zero allowlist boundary.

| Area | Result |
| --- | --- |
| Unit (cache policy / tenancy / settings) | 20/20 pass |
| Architecture allowlist | 4/4 pass, allowlist count = 0 |

`TenantResolver` and `MyGridPreferenceService` now consume `ICachePolicyRegistry.CreateHybridEntryOptions` for `tenancy.tenant-resolution` and `settings.grid-preference`.
