# Identity organization unit projection (2026-08-08)

Snapshot: architecture-identity-org-projection-20260808

## Verification

Organization publishes `OrganizationUnitChangedIntegrationEvent` in the same transaction as unit writes. Identity maintains `fn_identity_organization_unit_projection` and validates host role custom data scope against the local projection instead of a synchronous Organization port inside a transaction.

| Area | Result |
| --- | --- |
| Unit (version-monotonic writer) | 1/1 pass |
| Architecture transaction catalog | 2/2 pass, 1 high debt remains (Document->Files) |
| Migration 084 recovery (SQL Server + MySQL) | 2/2 pass |
| Integration slice (Identity + Organization) | pass |

## Removed debt

- HostRoleDataScopeService.UpdateDataScopeAsync / IIdentityOrganizationUnitDirectory