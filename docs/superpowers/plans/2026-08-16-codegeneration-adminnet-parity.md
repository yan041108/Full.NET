# CodeGeneration Admin.NET 对标实施计划

> **For agentic workers:** Use `fullnet-module-delivery`. RED first. Do not create a new `.csproj`.

**Goal:** 按已批准 Spec 把 Admin.NET 核心代码生成用户流程接到 Full.NET 内核，交付可视化工作台、精确权限 Vue 页面、Host Apply 自动接线、鉴权下载与剩余场景可执行生成；收口后再补 Identity B1 导入/批量与最后一名超管 E2E。

**Spec：** [`docs/superpowers/specs/2026-08-16-codegeneration-adminnet-parity-design.md`](../specs/2026-08-16-codegeneration-adminnet-parity-design.md)（Approved）

**Architecture:** 模板继续作为生成任务；Catalog 只读当前库；生成器扩展列 UI、精确权限与 Vue SFC；Apply 可选编排既有 Integration 编辑器；Identity 导入作为独立纵向切片，不并行抢迁移号。

**Tech Stack:** .NET 10、Dapper、DbUp、Vue 3、Element Plus、MSTest、client-contracts。

## Global Constraints

- 任务快照：`b1-codegen-parity-20260816`；基线 `31bec6a2`。
- inner：`pnpm test:inner -- --snapshot b1-codegen-parity-20260816`（CodeGeneration MySQL 聚焦）。切片关闭跑 `pnpm test:slice`。禁止 `test:integration:full` / `test:e2e:real`。
- 不修改 `ui/admin-layui/**`。不启用 DatabaseTools / ExecuteSQL / ReZero。
- 新权限进入 Contributor 即可，不抢迁移号，除非必须改表。
- 测试数量只改 `eng/testing/test-matrix.json`。

---

### Task 1: Spec 已冻结（本文件与设计 Spec）

- [x] 能力映射、拒绝项、列 UI、权限码、目录、Apply 闭环、场景与下载契约。

### Task 2: Host 表目录 + 列 UI + Vue 工作台

- [x] RED：目录 401/403、Host 成功列表排除视图、缺表 404、列同步保留人工 UI。
- [x] 实现 Catalog Endpoint/SQL/服务；扩展 `FullNetColumn.Ui` 与 Preview 列契约。
- [x] Vue 模板页：分页、选表、列配置表、场景表单；JSON 降为高级面板。
- [x] inner MySQL GREEN。

### Task 3: Vue SFC + 精确操作权限 + Contributor/路由

- [x] RED：显式 Schema 生成 `vue_view`、Endpoint 使用 create/update/disable。
- [x] 生成 SFC 与 Contributor 片段；Layui 路由目标改为可选。
- [x] 单元与 OpenAPI GREEN。

### Task 4: Host Apply 编排显式目标链

- [x] RED：无目标时保持工作区 Apply；有目标时编译失败零写入。
- [x] Apply 请求可选 `integrationTarget`；复用 BuildingBlocks Integration。
- [x] inner GREEN。

### Task 5: 鉴权下载 + Tree/关系场景

- [x] RED：无 download 权限 403；Tree 环检测拒绝；跨模块关系拒绝。
- [x] zip 流式下载；解锁 Tree 与同模块 MasterDetail 可执行生成。

### Task 6: Identity B1

- [x] RED：`identity.users.import` 与批量禁用；最后一名超管 UI 拒绝。
- [x] 实现导入（逐行报告、禁止导入超级管理员）、批量停用/启用、Playwright 规格（不在 inner 跑真实栈）。

### Task 7: Closeout

- [x] 更新对标矩阵与一份 verification；`git diff --check`；规则/Skill 一行结论。
