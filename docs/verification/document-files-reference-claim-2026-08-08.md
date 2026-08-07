# Document-Files Reference Claim Verification (2026-08-08)

## Scope

Task 6 from `docs/superpowers/plans/2026-08-08-architecture-gap-follow-up.md`: Document to Files reference claim state machine and zero cross-module local transaction debt.

## Delivered

- Migration `085` (`fn_files_file_reference_claim`) for SQL Server and MySQL
- Files claim service, reconciliation runner, delete guard via open claims
- Document version flow: claim before local transaction, confirm after success, release on known rollback
- `contracts/architecture/module-local-transaction-debt.json` entries cleared
- Global SQL catalog entries for Identity projection and Organization snapshot exports (Task 5 follow-up)

## Verification

- `dotnet build Full.NET.slnx -c Release`
- Architecture gates: transaction catalog empty, global SQL catalog aligned
- Unit: `HostFileReferenceClaimServiceTests`, `HostFileManagementServiceTests`
- Integration slice: `pnpm test:integration:affected -- --snapshot architecture-document-files-claim-20260808 --phase slice`

## Notes

- Delete checks `HasOpenClaimsAsync` outside the Files local transaction to satisfy module transaction boundary scanner.
- Commit-unknown paths keep Pending claims for Files reconciliation; Document probe confirms exact `VersionId + FileId`.