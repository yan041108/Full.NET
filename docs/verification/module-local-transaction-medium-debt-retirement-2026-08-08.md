# Module local transaction medium debt retirement (2026-08-08)

Snapshot: rchitecture-local-tx-medium-debt-20260808

## Verification

Moved IHostUserDirectory lookups outside local transactions for Notifications inbox send and Organization user-unit/user-position create paths. Removed three medium debt entries; two high debts remain.

| Area | Result |
| --- | --- |
| Unit (call-order + rollback) | 10/10 pass |
| Architecture transaction catalog | 2/2 pass, 2 high debts remain |
| Integration slice (Notifications, Organization) | 12/12 pass |

## Retired entries

- HostInboxMessageService.SendCoreAsync / IHostUserDirectory
- TenantUserUnitManagementService.CreateCoreAsync / IHostUserDirectory
- TenantUserPositionManagementService.CreateCoreAsync / IHostUserDirectory
