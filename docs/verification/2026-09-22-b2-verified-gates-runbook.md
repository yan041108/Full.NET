# Wave 2 — B2 Verified 门禁运行手册

**日期**：2026-09-22
**规则**：无 fresh 双库 + 文档证据不得改 parity 为 `Verified`。

| 优先级 | 模块 | 命令/文档 |
|--------|------|-----------|
| V1 | RBAC | `vue-action-authorization-w4-w5-closeout-2026-08-03.md`；program merge E2E |
| V2 | SerialNumbers | `serial-numbers-verified-20260820.md`；双库 real-stack fresh |
| V3 | Document | `2026-09-22-document-verified-gates.md`；`openapi:client:snapshot --update` |
| V4 | Tenancy / Org / Files | parity §4.2 B2；各模块 real-stack + WCAG 子集 |
| V5 | Identity 用户+超管 | Lane A 完成后统一升档；capability-status 38–39、58 |

**横切**：`pnpm test:governance`、`pnpm test:openapi`、受影响 `dotnet test`、合 main 前 integration-matrix 绿。
