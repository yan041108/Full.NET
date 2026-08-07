# Document-Files Reference Claim Verification (2026-08-08)

## Scope

Task 6 from `docs/superpowers/plans/2026-08-08-architecture-gap-follow-up.md`: Document to Files reference claim state machine and zero cross-module local transaction debt.

## Delivered

- Migration `085` (`fn_files_file_reference_claim`) for SQL Server and MySQL
- Files claim service, reconciliation runner, Files 本地事务内的 open-claim 删除保护
- Document version flow: claim before local transaction, confirm after success, release on known rollback
- `contracts/architecture/module-local-transaction-debt.json` entries cleared
- Global SQL catalog entries for Identity projection and Organization snapshot exports (Task 5 follow-up)

## Verification

- `dotnet build Full.NET.slnx -c Release`
- Architecture gates: transaction catalog empty, global SQL catalog aligned
- Unit: `HostFileReferenceClaimServiceTests`, `HostFileManagementServiceTests`
- Integration slice: `pnpm test:integration:affected -- --snapshot architecture-document-files-claim-20260808 --phase slice`

## Notes

- 2026-08-08 Cursor 后续审查纠正：删除保护不得放在 Files 事务外。当前实现已在 Files 本地事务内检查，并以条件 Claim/条件删除 SQL 关闭检查—写入竞态；`released` 幂等键复用失败关闭。
- **Task 1（post-review）**：Claim/Delete 共享 `fn_files_file` 行锁（SQL Server `UPDLOCK,HOLDLOCK` / MySQL `FOR UPDATE`）；双库 20×2 次竞争矩阵通过（`DocumentFilesReferenceClaim_race_is_atomic_*`）。
