# Workflow 首个纵向切片实施计划

> **For agents：** 使用 [`fullnet-module-delivery`](../../../.agents/skills/fullnet-module-delivery/SKILL.md)。**规格审查通过前不得创建模块代码、`.csproj`、迁移或 Composition 注册。**

- 建立日期：2026-08-20
- 状态：**Draft plan — Spec pending review**（不可执行编码）
- 设计规格：[`2026-08-20-workflow-module-design.md`](../specs/2026-08-20-workflow-module-design.md)
- 建议快照（审查通过后）：`workflow-first-vertical-slice-YYYYMMDD`
- 预期迁移号：开工前现场确认两库空闲号段（本计划不占号）

**Goal：** 交付最小可验证闭环：**定义草稿 → 发布不可变版本 → 启动实例 → 单节点人工审批（同意/拒绝）**。

**Architecture：** 单主项目 `Full.NET.Modules.Workflow`；表 `fn_workflow_*`；权限 `workflow.*`；API `/api/v1/workflow/...`；跨模块仅 Contract/Outbox。

**Tech Stack：** DbUp 双库迁移、Dapper、ProblemDetails、Vue `ui/admin`（禁止扩展 Layui）、Integration + 真实栈 E2E。

---

## 前置门禁

1. [`2026-08-20-workflow-module-design.md`](../specs/2026-08-20-workflow-module-design.md) 经项目所有者/授权用户审查通过（状态改为 Approved）。
2. Notifications 与 Jobs 恢复路径可用（parity 队列前置）。
3. `pnpm test:task:start -- workflow-first-vertical-slice-<date>` 创建快照；记录 `git rev-parse HEAD`。
4. 确认迁移号空闲；禁止与其他大型模块抢号。

---

## 切片范围

### 必须交付

| Task | 内容 | 验证 |
| --- | --- | --- |
| A | 双库迁移：`definition`、`definition_version`、`instance`、`step`、`todo`、`execution_log`（抄送表可推迟） | Migration 恢复测试 ×2 |
| B | 草稿 CRUD + `Publish` 生成不可变版本（含内容哈希） | Integration SQL Server/MySQL |
| C | `Start` 实例：绑定版本、创建首个人工步骤与待办 | Integration；业务关联键冲突失败关闭 |
| D | 待办 `approve` / `reject`：推进实例终态；写执行日志与审计 | Integration + 403 |
| E | 权限 Contributor、导航、精确 Endpoint 权限 | Architecture + Vue 按钮门控 |
| F | Vue：定义列表/草稿编辑/发布、我的待办办理 | 客户端单测 + admin-parity |
| G | 真实栈 E2E：发布 → 启动 → 审批；无权限 403/按钮不可见 | `admin-real-stack` 双库 |

### 明确非目标（本切片）

- 多节点图、并行网关、会签、抄送 UI、子流程、自动 HTTP/脚本节点。
- 超时扫描 Jobs 接线（可留接口；本切片可用手动取消代替超时）。
- DataApproval 集成、业务模块真实联动（可用 Integration 内假业务键）。
- 创建独立 `*.Contracts` 程序集（无真实消费者时不拆）。
- 任何 `.csproj` 在 Spec 未批准时预先落地。

---

## 建议实施顺序（审查通过后）

### Task A：迁移与 RED

1. 确认迁移号；编写 SQL Server/MySQL 配对脚本与过滤唯一索引。
2. RED：半完成 DDL 恢复、二次执行、数据保留。
3. Architecture：Workflow 不得引用具体 SQL 驱动程序集。

### Task B：草稿与发布

1. RED：发布后修改版本业务列必须失败；版本号单调；哈希稳定。
2. GREEN：Command/Endpoint；OpenAPI + JSON 源生成 Context。

### Task C–D：实例与单节点审批

1. RED：启动创建 Active 实例 + Pending/Active 待办；同意 → Completed；拒绝 → Rejected。
2. 重复审批同一待办幂等或冲突码明确；跨用户办理 403。
3. 同事务 Outbox 钩子可先用测试探测“已写入意图”（若首切片无外部消费者，可仅本地日志 + 预留 Outbox 调用点，但不得跨模块直写）。

### Task E–G：权限与客户端

1. 权限码与 Spec §6.2 一致；无权限不创建 DOM 入口。
2. Vue 仅 `ui/admin`。
3. E2E 双库；环境缺失写入 `docs/verification/`，不升 Verified。

---

## 完成定义

- Spec Approved 且本计划勾选任务均有新鲜测试输出。
- affected merge 双库非零发现全绿；`pnpm test:naming`、`test:sql-safety`、Architecture、OpenAPI 通过。
- `capability-status.md` / parity：首切片最多 `Build-verified`；Verified 需恢复与幂等推进的真实栈证据。
- 单切片独立提交；禁止与其他 B3 模块混交。

---

## 停止条件

- Spec 仍为 pending review → **停止，不写代码**。
- 发现必须跨模块本地事务才能审批 → 停止并修订 Spec。
- 迁移号冲突 → 停止并重新协调。
- 需要动态脚本节点才能演示 → 拒绝，保持人工单节点范围。
