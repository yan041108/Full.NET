# CodeGeneration Admin.NET 能力对标设计

**状态：** Implemented（Build-verified，2026-08-16；见 [验证记录](../../verification/codegeneration-adminnet-parity-2026-08-16.md)）  
**日期：** 2026-08-16  
**基线：** `main` @ `31bec6a2`  
**批准依据：** 项目所有者确认执行 B1，且 CodeGeneration 按能力和用户流程全量对标 Admin.NET.Pro `v2.1` 核心代码生成（不复制源码）。  
**适用范围：** Host 代码生成工作台、生成引擎、显式模块接入、Vue 管理端；不包含 DatabaseTools、FormBuilder、ReZero。  
**被替代关系：** 不替代既有 Apply/Rollback/检查点 Spec；本文件冻结产品面与生成契约的增量。下游实施计划见 [`2026-08-16-codegeneration-adminnet-parity.md`](../plans/2026-08-16-codegeneration-adminnet-parity.md)。

> **2026-10-24 状态限定：** 上述 Implemented / Build-verified 为历史切片记录，不表示本规格全部生成行为已兑现。[合并审查](../../verification/2026-10-24-codegeneration-remediation-review.md)确认列查询/排序/导入导出、关系页面仍有缺口，多唯一列、多对多、失败事务与生成 Feature AOT 还存在正确性问题；Host 整链应用/回滚及 Git 发布也待整改。建议按该记录四批收敛。本次授权仅更新文档，没有执行整改或运行验收；具体长期契约变更在后续实施前同步本规格及相关 Apply/Git 规格，保留历史验证事实。

> **同日方案复审：** [批次依赖与执行不变量](../../verification/2026-10-24-codegeneration-remediation-review.md#41-批次依赖与最小输入契约)补充输入冻结、服务端校验前移、工作区全局 LIFO、Apply 幂等与旧执行者隔离、持续 Git 分支基线和独立下载保留。运行恢复及完整 Apply Plan 均按 P1 纳入第二批闭环。这些是待实施整改目标，不把原有分阶段文件接入描述为跨磁盘/数据库/Git 的单一事务。

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

2026-10-26 按整改授权直接演进为两步协议。先通过 `POST /api/v1/code-generation/runs/apply-plan` 提交 `previewRunId` 与可选 `integrationTarget`，取得完整文件动作、前后摘要、工作区 GUID/revision、生成器版本、计划摘要与 15 分钟有效期。默认仅规划产物；有显式目标时同时规划模块后端/注册桥、入口、Composition、Vue 页面及两个客户端文件、路由、授权与根 Manifest，并在真实写盘前合并编译候选。

用户审查文件列表后，`POST /api/v1/code-generation/runs/apply` 只接受 `planId`、`planSha256`、`idempotencyKey`。锁内重新校验工作区顺序、所有前态与 Git 基线，只执行冻结正文，不重新读取可变模板。相同键和计划重放原始运行；同键不同计划冲突。页面按当前用户保存待提交计划与幂等键，响应丢失或重新挂载时重试同一次执行。

计划审查、运行登记和文件应用分离：未确认的计划不创建运行或活动操作。确认后先耐久登记操作，再创建摘要与检查点；数据库插入/终态失败均保留日志，恢复时重建或收敛同一运行，不把可能已经提交的文件标成普通 failed。全局回滚顺序与阶段由工作区日志记录，清单最后提交，人工漂移停止恢复。四个逐阶段 CLI 写命令已退役，整链接入统一使用 `apply-host-integration`；只读 plan/validate 保留。

目标 JSON 必须完整，所有目标路径先做包含关系、链接和别名检查；Apply 默认关闭，禁止隐式接入官方 `Full.NET.Modules.*`。相关能力仍按[整改执行计划](../plans/2026-10-24-codegeneration-remediation.md)的真实验证范围报告，完整恢复入口、历史归档与其余批次不能由此自动计为完成。

## 8. 场景可执行生成

- **Single：** 现有 CRUD 加上列 UI 效果（查询条件、必填、唯一校验）。
- **Tree：** 可空 `ParentId`；同租户父节点必须存在；禁止自引用；祖先链环检测（深度上限 32）；悬挂父节点拒绝写入。满足后才允许可执行产物。
- **MasterDetail / ManyToMany：** 关系两端必须同模块、同数据作用域；主从写入使用本模块本地事务；跨模块关系继续禁止。复合键与级联删除必须在 Schema 显式声明，缺省拒绝。

Tree 的环检查与结构修改须在同一本地事务取得同租户互斥；草案生成 `{entityTable}_tree_guard`（租户唯一行，Host/Global 为该实体单行），过长表名明确拒绝，不截断。SQL Server 使用唯一键上的 `UPDLOCK/HOLDLOCK`，MySQL 使用唯一键原子写；锁随事务结束释放，不使用需要额外释放的会话锁。存在活动子节点时拒绝删除父节点。该锁仅覆盖生成服务写路径；禁止后台任务在活动写入期间删除锁行。SQL Server 锁语义依据[官方表提示文档](https://learn.microsoft.com/sql/t-sql/queries/hints-transact-sql-table?view=sql-server-ver16)。

## 9. 鉴权下载

`GET /api/v1/code-generation/runs/{id}/artifacts.zip` 需要 `codegen.runs.download`。仅允许 `preview` 或 `apply` 且 `succeeded` 的运行；内容为当次产物的确定性 zip（条目路径正斜杠、LF、按路径排序）。不落盘 `wwwroot`，不返回绝对路径。

下载不调用当前模板或生成器。受跟踪预览先持久化当次包及规范 Schema、生成器版本、清单与 zip 摘要，成功后才插入 succeeded 运行；直接 Schema 同样支持。Apply 在文件提交前从冻结操作的文件正文与未变化摘要生成完整工作区包，包含最终清单；恢复沿用同一包和元数据。规划预览读取归档输入，模板编辑或删除不使已归档预览失去输入；生成器版本变化要求重新预览后才能新建 Apply Plan，但旧包仍可下载。

归档配置为 `CodeGeneration:Artifacts`：`RootDirectory` 必须是已存在的共享绝对目录，与 Apply 工作区互不包含且不能位于公开静态目录。默认空目录表示存储未配置，受跟踪预览返回 `codegen.run.archive_unavailable`，不写 succeeded。默认保留 30 天、总量 512 MiB、4096 包，单包上限 8 MiB；字节存储采用受控元数据边界编码，总量限制按实际存储字节计。归档不依赖 Apply 开关，不随检查点清理或成功回滚删除。到期保留不可变数据库元数据并清理包，明确返回 `archive_expired`；丢失、损坏、容量不足和租约占用分别返回稳定错误，均不重算替代包。写入时清理到期包，未知文件保留并计入容量；共享目录锁必须由底层文件系统提供一致语义。

## 10. 安全与拒绝项

数据库部署需先执行双库 `290_CodeGenerationRunArchive` 和 `292_CodeGenerationRunSource`。来源约束允许冻结内联 Schema 或完整模板标识/正版本对；回滚必须绑定来源 Apply 且不携带模板，保留原状态、结果及唯一回滚约束。292 升级遇到残缺模板对必须失败并保留原约束和业务行，不自行修补未知历史输入。HTTP 参数绑定拒绝保持正确 400/415，并由业务 API 的空响应兜底返回 ProblemDetails，不回显原始请求；OAuth/OIDC 协议路径保持原契约。

- 不复制 Admin.NET 源码、模板或公开文件 URL。
- 不在浏览器或 API 进程执行生成代码。
- 不把缓存失效或 Audit 写入 Outbox。
- 工作台与目录均为 Host 权限；租户令牌必须 403。
- 总体能力在双库 Integration 与 Vue 权限验收后保持 `Build-verified`；未跑完整真实栈矩阵前不得标 `Verified`。

## 11. 2026-10-24 整改执行契约

批准依据为用户在方案复审后明确要求“执行方案”，活动计划见[整改执行计划](../plans/2026-10-24-codegeneration-remediation.md)。本节直接演进对应生成契约，历史切片状态不作为本轮运行验证；Apply/Git 的新状态契约随第二批同步其原规格。

- 生成写服务使用 Result 感知事务，失败必须回滚；数据库唯一冲突在事务退出后转换为稳定业务冲突。必填字符串和 decimal 精度/小数位由服务端验证，页面不能代替校验。
- 关系请求新增 `principalSchema` / `dependentSchema`：仅非当前端提供一层无关系的单实体快照，必须同 owner、module 与作用域；Host、CLI 共用内核校验。主端引用列为非空主键或显式唯一列，从端不可变时拒绝级联。快照及列元数据进入规范 Schema 摘要，不从可变目录临时补齐。
- 仍遵守 Naming Profile：当前模型的表名必须等于 owner/module/entity 的规范结果，不新增任意物理表名别名。原报告中“接受不一致表名后查错表”的前提在现行 Schema 构造器下不成立；快照主要用于冻结真实列、生命周期和约束，防止接入推断。
- 每个唯一列独立生成预检查；关系复合键形成数据库唯一约束，多对多还约束关联对。NULL 不参与唯一判定，软删值继续占用，租户键包含 TenantId。唯一文本默认区分大小写，以 SQL Server `Latin1_General_100_BIN2` / MySQL `utf8mb4_bin` 明确比较，保留相同的尾空格比较语义；索引大小超出共同安全预算时拒绝生成，不能截断。
- 双库草案独立探测和补建唯一索引；既有同名索引形状不符或唯一文本排序规则漂移时失败，不能静默把存在视作正确。草案仍不自动进入 DbUp。
- 级联根据从端能力选择硬删或软删；软删同步删除审计与版本，不物理删除。生成 SQL 参数使用静态字典，Record 提供显式双库物化器，模块注册在 AOT 请求前安装物化路径。
- 报告携带显式 `generatorVersion`，内置模板行为变化必须演进版本并改变产物摘要。第一批验证包含真实生成且编译的写服务、失败事务、关系约束和物化，不以字符串断言替代行为。

上述是实现目标与输入约定；具体完成范围以活动计划中的新鲜测试证据为准，不据此宣称第二至四批已完成。

### 11.1 单表查询与线格式

`queryable && queryKind != none` 的列才进入过滤契约；公开参数为 `filter{ClrPropertyName}`，范围追加 `From/To`。`contains` 仅适用于字符串，`range` 仅适用于数值/UTC 时间；参数命名碰撞在规范化时拒绝。严格参数白名单拒绝未知、大小写别名与重复键，不能通过绑定器先舍入再验证。排序 `sortBy` 仅接受声明 sortable 的 JSON 属性名，`descending` 可省略，SQL 用固定 CASE 及 Id 尾键，计数与列表使用相同过滤。列名、方向、表名不由输入拼接。

Int64/decimal 请求和响应使用无损十进制字符串，日期要求明确时区。生成的 JSON 属性显式使用源生成可达的转换器，非空字段声明 JsonRequired；超范围或无法精确表示的值拒绝读取。可写 UTC 时间及日期查询限制年份不小于 1000、有效精度不超过微秒，并规范为 UTC。系统审计时钟字段保留其既有服务端契约。

Vue 以字符串编辑 Int64/decimal，Int32 使用有界整数控件；可空布尔区分 true/false/null，UUID 和日期清空保留 null。日期控件只有用户修改才回写 ISO UTC，显示不能截断并覆盖未编辑的原始微秒。表单及错误/操作文字接入当前后台国际化；只把请求字段送入写请求。组织归属创建通过必需的 `X-FullNet-Organization-Unit-Id` UUID 请求头，服务端写授权与关系组织约束分别执行。Host 和 CLI 都冻结完整字段 UI 元数据；不支持的元数据组合不能静默替换为其他行为。

关系叶子快照还须与当前实体采用相同 OwnershipMode；组织归属主端、祖先和级联谓词同时限制组织与租户。ManyToMany 的两个从端外键必须不同，复合唯一键恰好为该关联对。

### 11.2 关系工作台与受控选项

Tree 页面逐页读取完整有界树（最多 1000 条），总数变化、重复 ID 或超限时拒绝用不完整结果替换当前树。父节点选择排除自身和后代；损坏结构可见地平铺提示。写入端并发安全仍由 §8 的事务互斥承担。

MasterDetail 新增 GET/PUT `/{id}/aggregate` 和 POST `/aggregate` 组合入口，共用主端 Read/Create/Update 精确权限。创建最多 128 行，编辑最多 256 个子操作；主端版本先校验，外来明细、重复操作、明细版本冲突或任一写入失败均回滚主端和此前明细。子外键只由已授权主端赋值，页面一次提交 Added/Updated/Removed 及相应版本。当前要求主端 Id 引用、可修改/可删除且带版本的明细；不支持组合明确拒绝，不生成独立明细路由。

ManyToMany 的 `relationship-options/source|target` 在关联 Read 权限下提供有界最小查询，只返回 Value/Label，不开放主端完整 DTO。复用冻结主端的租户、组织数据范围和软删过滤；搜索和 selected 都是静态参数，页面每次 20 条、最多缓存 200 条，旧搜索响应不能覆盖新搜索。软删后的重新添加恢复原关联 Id、清空删除审计并推进版本，重新验证两个主端；活动关联重复仍返回 unique_conflict。

组织归属整链计划同时增加缺失的 Organization.Contracts 项目引用，并纳入候选编译、摘要与回滚；从项目已有 Identity.Contracts 引用确定同一框架位置，不引用 Organization 实现。组织能力所需运行时 Port 由应用组合根提供。

标准生成错误通过 Hosting 嵌入式中英文资源及当前请求文化本地化，实体机器码保持稳定；字段验证携带 ValidationViolation。类型与 ControlKind 不一致、系统字段进入写表单、未知查询组合均拒绝。`includeInImportExport` 默认 false、编辑器禁用，存量 true 仅保留输入并报告 importExportGenerated=false；不表示已生成 CSV 或执行导入导出。字典和导入导出仍使用既有模块契约，不新增动态模板执行器。
