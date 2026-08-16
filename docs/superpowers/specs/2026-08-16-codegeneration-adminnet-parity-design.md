# CodeGeneration Admin.NET 能力对标设计

**状态：** Implemented（Build-verified，2026-08-16；见 [验证记录](../../verification/codegeneration-adminnet-parity-2026-08-16.md)）  
**日期：** 2026-08-16  
**基线：** `main` @ `31bec6a2`  
**批准依据：** 项目所有者确认执行 B1，且 CodeGeneration 按能力和用户流程全量对标 Admin.NET.Pro `v2.1` 核心代码生成（不复制源码）。  
**适用范围：** Host 代码生成工作台、生成引擎、显式模块接入、Vue 管理端；不包含 DatabaseTools、FormBuilder、ReZero。  
**被替代关系：** 不替代既有 Apply/Rollback/检查点 Spec；本文件冻结产品面与生成契约的增量。下游实施计划见 [`2026-08-16-codegeneration-adminnet-parity.md`](../plans/2026-08-16-codegeneration-adminnet-parity.md)。

## 1. 决策摘要

Full.NET 已具备确定性 CRUD 引擎、模板持久化、受跟踪 Preview/Apply/Rollback 与 CLI 模块接入。Admin.NET.Pro 核心「代码生成」对用户呈现为：选表、配列、配置场景、预览、生成、写盘、可选菜单。本设计把同一用户流程接到 Full.NET 现有内核上，生成物必须是 Dapper、显式 Endpoint、双库迁移草案与 Vue 3 页面，禁止吸收 Furion、SqlSugar、公开 zip URL、运行时 DDL 或动态程序集。

生成任务继续复用 `fn_codegeneration_template` 中的规范 Schema JSON，不新建 Job 表、不新增 `.csproj`。

## 2. 能力映射

| Admin.NET.Pro 核心能力 | Full.NET 归属 | 契约 |
| --- | --- | --- |
| 生成任务 CRUD | 现有 Template API | 名称/描述 + `CodeGenerationPreviewRequest` |
| 库定位器选表 | Host 只读目录，当前进程数据库 | `GET /api/v1/code-generation/catalog/tables`，权限 `codegen.catalog.read` |
| 载入默认列 | 复用 `DatabaseColumnMetadataMapper` | `GET .../tables/{tableName}/columns` |
| 同步列 | 只读 diff，不静默覆盖人工 UI 配置 | `POST .../catalog/column-sync` |
| 场景 | 已有 `FullNetCrudScene` | Single 可执行；Tree/MasterDetail/ManyToMany 按第 8 节解锁 |
| 列控件/查询/唯一 | `FullNetColumn.Ui` 可选元数据 | 不得改写物理列名 |
| 预览 | 现有 Preview/Runs | 增加精确操作权限与 Vue SFC 产物 |
| 写盘 | 现有 Apply Gate + 检查点 | 可选显式 `integrationTarget` 编排模块/Composition/Vue 路由 |
| 下载 zip | 受保护流式下载 | `codegen.runs.download`；禁止 `wwwroot` 公共 URL |
| 生成菜单 | 目标模块 `AuthorizationContributor` 文本接入 | 禁止运行时插入导航表 |
| 库表 DDL / ExecuteSQL / ER / 备份 | DatabaseTools（Mapped M5+） | 本设计拒绝 |
| ReZero / 动态编译 / 脚本任务 | 禁止吸收 | — |

## 3. 列展示元数据

[`FullNetColumn`](../../../src/BuildingBlocks/Full.NET.Data.CodeGeneration/Schema/FullNetColumn.cs) 增加可选 `Ui`。缺省时由标量类型与列名推导，不改变已持久化模板的物理含义。

稳定机器码：

- 控件 `controlKind`：`text` / `textarea` / `number` / `switch` / `datetime` / `uuid`
- 查询 `queryKind`：`equals` / `contains` / `range` / `none`
- 开关：`showInList`、`includeInCreate`、`includeInUpdate`、`required`、`sortable`、`queryable`、`unique`、`includeInImportExport`

HTTP 列对象在现有字段上以可空成员追加上述属性；`JsonIgnore(WhenWritingNull)`。未知成员仍 `Disallow`。UI 元数据不得覆盖 `databaseName` / `clrPropertyName` / `jsonPropertyName` / `scalarType`。

默认推导：`Id`/`TenantId`/审计/Version 不进入创建或更新表单；`String`→`text`+`contains`；`Boolean`→`switch`；`DateTimeUtc`→`datetime`+`range`；整数与 Decimal→`number`+`equals`；`Uuid`→`uuid`。`required` 默认等于 `!isNullable`。

## 4. 生成权限码

显式能力 Schema 生成：

```text
{moduleKey}.{permissionResourceName}.read
{moduleKey}.{permissionResourceName}.create
{moduleKey}.{permissionResourceName}.update
{moduleKey}.{permissionResourceName}.disable
```

列表与详情使用 `read`；创建使用 `create`；更新使用 `update`；停用/删除使用 `disable`（硬删、软删或不可变拒绝由既有 `deleteMode` 决定）。禁止 Admin.NET `{camel}/page` 形态，也禁止新生成物把写操作绑在粗粒度 `.write` 上。

预览响应保留 `writePermission` 作为兼容只读字段（值等于 `update`），并新增 `createPermission` / `updatePermission` / `disablePermission`。遗留 `hasVersion` Schema 仍可发出 `.read/.write`，不得作为新工作台默认。

生成的 `AuthorizationContributor` 片段必须为每个操作声明独立 Action，Vue 无权限时不创建对应按钮。

## 5. Host 表目录

- 只扫描当前 API 进程已配置的 SQL Server 或 MySQL；禁止请求体或查询携带连接串。
- SQL 使用 `SqlDataScope.HostOnly`，语句与 CLI 内核一致：默认 Schema 基础表，排除视图，ordinal 排序。
- `tableName` 必须先存在于目录再读列；参数化查询，拒绝路径与通配。
- 不执行 DDL、不返回行数据、不推断业务名。表名符合 `{owner}_{module}_{entity}` 时工作台可预填键；否则用户显式填写，Preview 仍受 Naming Profile 约束。

## 6. Vue 产物与菜单接入

生成器在页面模型之外增加 Vue SFC：

- `clients/vue/{apiResourceName}View.vue`（列表 + 编辑对话框，Element Plus）
- 机器码 `vue_view`；`GeneratedArtifactKind.VueView = 7`

SFC 必须消费既有 `*-page.generated.ts`，提供分页、空态、ProblemDetails，并按精确权限隐藏操作。不生成 Layui 页面；`includeLayuiClientArtifacts` 默认 false。

显式模块目标：

- `clientRoute.layuiControllerPath` / `layuiControllerExport` 改为可选；缺省时只改 Vue 路由文件。
- 新增可选 `authorizationContributorPath`：仅接受仓库相对路径，按标准 `Permissions`/`Navigation`/`Actions` 集合尾部幂等插入生成片段；非标准形态 fail-closed。

## 7. Host Apply 闭环

`POST /api/v1/code-generation/runs/apply` 请求增加可选 `integrationTarget`。缺省时行为与现网一致：只写入 Apply `WorkspaceRoot`。

存在目标时，在检查点创建之后、数据库事务之外按顺序执行：

1. `apply-module-integration`
2. `apply-module-entry-integration`
3. `apply-composition-integration`
4. Vue `apply-client-route-integration`
5. 可选 Contributor 插入

约束：目标 JSON 必须完整；缺失项目/入口/Catalog/路由 fail-closed；先隔离编译后提交；失败零写入业务文件；Git 同步与检查点回滚语义不变；`CodeGeneration:Apply:Enabled` 默认关闭。禁止对官方 `Full.NET.Modules.*` 做隐式推断接入。

## 8. 场景可执行生成

- **Single：** 现有 CRUD 加上列 UI 效果（查询条件、必填、唯一校验）。
- **Tree：** 可空 `ParentId`；同租户父节点必须存在；禁止自引用；祖先链环检测（深度上限 32）；悬挂父节点拒绝写入。满足后才允许可执行产物。
- **MasterDetail / ManyToMany：** 关系两端必须同模块、同数据作用域；主从写入使用本模块本地事务；跨模块关系继续禁止。复合键与级联删除必须在 Schema 显式声明，缺省拒绝。

## 9. 鉴权下载

`GET /api/v1/code-generation/runs/{id}/artifacts.zip` 需要 `codegen.runs.download`。仅允许 `preview` 或 `apply` 且 `succeeded` 的运行；内容为当次产物的确定性 zip（条目路径正斜杠、LF、按路径排序）。不落盘 `wwwroot`，不返回绝对路径。

## 10. 安全与拒绝项

- 不复制 Admin.NET 源码、模板或公开文件 URL。
- 不在浏览器或 API 进程执行生成代码。
- 不把缓存失效或 Audit 写入 Outbox。
- 工作台与目录均为 Host 权限；租户令牌必须 403。
- 总体能力在双库 Integration 与 Vue 权限验收后保持 `Build-verified`；未跑完整真实栈矩阵前不得标 `Verified`。
