# CodeGeneration 工作台 UX 对标设计

**状态：** Implemented（Build-verified，2026-08-20；见 [验证记录](../../verification/codegeneration-workbench-ux-parity-20260820.md)）  
**日期：** 2026-08-20  
**基线：** `main` @ `ce7dddf6`  
**批准依据：** 项目所有者确认按 Admin.NET.Pro v2.1 代码生成产品面做全方位对比并补齐工作台 UX；不推翻 2026-08-16 能力 Spec 的拒绝项。  
**适用范围：** Host Vue 模板页与预览工作台、模板列表筛选、权限 Action 目录；不改生成引擎控件枚举、不改表结构、不改 Layui。  
**上游：** [`2026-08-16-codegeneration-adminnet-parity-design.md`](./2026-08-16-codegeneration-adminnet-parity-design.md)  
**下游计划：** [`2026-08-20-codegeneration-workbench-ux-parity.md`](../plans/2026-08-20-codegeneration-workbench-ux-parity.md)

## 1. 决策摘要

Full.NET 代码生成引擎与主路径（选表 → 配列 → 场景 → 预览 → Apply/下载）已 Build-verified。相对 Admin.NET 的差距集中在工作台交互密度：列表筛选/复制/列展示不足，列元数据与实体能力、关系编辑主要依赖高级 JSON，预览页缺少模板深链与 `integrationTarget` 表单。本设计要求把**已有 Schema 契约可视化**，而不是扩控件种类或三表模型。

## 2. 差距矩阵

### A. 已对齐 / Full.NET 更强

| 能力 | 归属 |
| --- | --- |
| 模板 CRUD、Catalog、列同步、场景、精确权限产物 | 2026-08-16 Spec |
| Tracked Preview / Apply Gate / Rollback / 鉴权 zip | Full.NET 超出 Admin.NET |
| Dapper + 双库迁移草案 | Full.NET 架构基线 |

### B. 本轮必须对齐

| 维度 | 验收 |
| --- | --- |
| 模板列表 | 展示 name、table、scene、module、entity、version；支持 name/table 筛选；复制；编辑；跳转预览；删除 |
| 表单结构 | Tabs：基础 / 能力 / 列 / 关系（非 single）/ JSON 兜底 |
| 列网格 | 暴露 `sortable`、`queryKind`、`includeInImportExport`；展示 `scalarType` |
| 实体能力 | 可视化 `deleteMode`、审计开关、`hasVersion`、`ownershipMode` |
| 关系 | 非 single 场景可编辑 `relationships[]`（同模块语义由引擎 fail-closed） |
| 预览深链 | `/code-generation/previews?templateId=` 自动载入模板 Schema |
| integrationTarget | Apply 前可选可视化目标表单；缺省行为不变 |
| Action 目录 | `codegen.runs.execute|apply|rollback` 登记到预览页 |

### C. 明确拒绝（UI 不提供对等控件）

多库定位器、打印、IsApiService、运行时插菜单、Dict/FK/Upload 等扩展控件、统计/FromValid、DatabaseTools、ReZero、公开 zip URL、新建 Job/Column 表、列 Comment 契约扩展。

## 3. API 增量

- `GET /api/v1/code-generation/templates` 增加可选查询参数 `name`、`tableName`（contains，空则不过滤）。
- 复制不新增权限码：客户端 `GET` 详情后 `POST` 创建，名称追加稳定后缀。
- 不新增表、不抢迁移号。

## 4. 权限

现有权限码不变。预览页 Action 补齐 execute / apply / rollback（download 已有）。模板页可增加 copy 为 create 门控下的 UI 动作，不新增独立权限。

## 5. 验收与状态

- Vue 交互与权限门控可测；模板筛选有 Unit/Integration 证据。
- 路线图「前后端代码生成」保持 **Build-verified**；未跑完整双库 real-stack 前不得标 Verified。
- 禁止修改 `ui/admin-layui/**`。
