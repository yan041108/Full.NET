# 执行清单全量程序跟踪（2026-09-23）

**纪律**：严格单编号串行；权威清单 [2026-09-05](2026-09-05-adminnet-module-gap-and-execution-checklist.md)。  
**计划**：Cursor 计划「执行清单全量开发排期」；不修改计划文件本身。

## Gate 0

| 包 | 状态 | 证据/备注 |
|----|------|-----------|
| G0-1 CI | 已推送待 CI | `be03a5b8`：`AiAgentApprovalRecord` AOT materializer；观测 `api-native-aot-linux` / `ci` |
| G0-2 A 区 E2E | 部分 | real-stack 登录等待 `/api/v1/navigation`（`780b37bb`）；Recovery 通知投影仍登记 Inconclusive |
| G0-3 RBAC merge | 待 CI | `admin-action-w4-w5-program-20260803` merge 以 `integration-shard` 为准 |

**Gate0 出口**：G0-1 绿 + A 区 P0 证据无未登记阻塞 → 可开 B-19 验证槽（19 已实现则直接 closeout）。

## Phase A（01–12 验证关闭）

| 编号 | 状态 | 锚点 |
|------|------|------|
| 01–06, 08–12 | Build-verified（源码） | [wave3 gap-audit](2026-09-22-wave3-workflow-dataapproval-gap-audit.md) |
| 07 | 已关闭 | B1b closeout |
| 证据补强 | 随 Gate0 G0-2 | `data-approval-requests`、`workflow-*` real-stack specs |

## Phase B（19–47，28 槽）

| 编号 | 状态 | 说明 |
|------|------|------|
| 19 | **closeout** | [2026-09-23-execution-checklist-19-closeout](2026-09-23-execution-checklist-19-closeout.md)；`host-roles` API 复制 E2E |
| 20 | **closeout** | [2026-09-23-execution-checklist-20-closeout](2026-09-23-execution-checklist-20-closeout.md) |
| 21–47 | **待串行** | B-3…B-28 逐编号 closeout（见 [B 区审计](2026-09-23-execution-checklist-b-zone-audit.md)） |

## Phase D / Gate C / Phase C

未启动；依赖 Phase B 全部合并 + D-5（87）。

## 本机验证（滚动）

| 命令 | 结果 |
|------|------|
| `pnpm test:aot:analyzers` | 通过（Ai materializer 变更后） |
| `dotnet test` …`AiBudgetRowReader` | 3/3 通过 |
