# CodeGeneration 工作台 UX 对标实施计划

> **For agentic workers:** Use `fullnet-module-delivery`. RED first. Do not create a new `.csproj`. Do not edit Layui.

**Goal:** 把 Admin.NET 代码生成工作台交互密度对齐到 Full.NET：模板列表筛选/复制/列展示、Tabs 表单暴露已有 Schema 能力、预览深链与 integrationTarget 表单、runs Action 目录补齐。

**Spec：** [`docs/superpowers/specs/2026-08-20-codegeneration-workbench-ux-parity-design.md`](../specs/2026-08-20-codegeneration-workbench-ux-parity-design.md)

**Architecture:** 无新表；列表筛选走现有 `fn_codegeneration_template`；复制复用 Create；Vue 只暴露 `FullNetColumnUi` / `entityCapabilities` / `relationships` 已有字段。

**Tech Stack:** .NET 10、Dapper、Vue 3、Element Plus、MSTest、client-contracts、admin-i18n。

## Global Constraints

- 任务快照：`codegen-ux-parity-20260820`；基线 `ce7dddf6`。
- inner：`pnpm test:inner -- --snapshot codegen-ux-parity-20260820`；slice 同快照。禁止用完整 `test:e2e:real` / `test:integration:full` 代替 inner。
- 不修改 `ui/admin-layui/**`；不扩展 controlKind；不升 Verified。
- 测试数量只改 `eng/testing/test-matrix.json`（若本波新增用例计数变化）。

---

### Task 1: Spec / Plan

- [x] Dated Spec 固化差距矩阵与拒绝项。
- [x] 本实施计划。

### Task 2: Templates 列表筛选（RED→GREEN）

- [x] RED：Unit 断言 Page SQL 含 `@NameContains` / `@TableNameContains`；QueryService 传入规范化参数。
- [x] 实现 SQL/Endpoint/QueryService；Vue API 传 query。
- [x] 复制：Vue `create` + 名称后缀，无新 API。

### Task 3: TemplatesView 工作台

- [x] 列表列、筛选、复制、预览深链、删除门控。
- [x] Tabs：基础 / 能力 / 列（补 sortable/queryKind/importExport/scalarType）/ 关系 / JSON。
- [x] i18n zh-CN + en-US。

### Task 4: PreviewsView

- [x] `templateId` query 自动载入。
- [x] `integrationTarget` 开关与表单；Apply 时附带。
- [x] Action：execute / apply / rollback；`PermissionGate`。

### Task 5: Closeout

- [x] inner + slice；扩展 real-stack CodeGen 规格（复制/列开关/深链）。
- [x] verification + roadmap 脚注；`git diff --check`；规则/Skill 一行结论。
