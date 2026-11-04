# Enterprise Request 样例

主从（header + line）租户单据样板，配合 Full.NET CRUD 生成器演示 F09 业务接入标准。

Schema 已冻结完整明细定义；头和明细均显式保存组织归属。当前生成后端使用组合事务保存主从，级联删除遵守明细软删、审计及版本策略。示例迁移用于新应用目标结构；此前手工创建的旧明细表须由应用迁移补齐 OrganizationUnitId，生成器不会自动改写业务数据库。金额和版本的 JSON 使用十进制字符串。

## 内容

- `schema.json` — `master.detail` CRUD Schema（申请头 `enterprise_request` + 明细 `enterprise_request_line`）
- `integration-target.json` — 模块/Composition/Vue 路由接入目标
- `src/` — 应用自有模块，包含生成 CRUD、审批、导入、附件与文档交付
- `tests/schema.test.mjs` — Schema 结构契约测试

## 装配边界

官方 Full / Enterprise 预设与三个官方宿主均不包含此示例，也不创建 `demo_*` 表。示例是应用自有模块，应用组合根显式引用示例项目后使用：

```csharp
services.AddFullNetApplicationModules(configuration, FullNetHostProfile.Api,
    [new EnterpriseRequestModule()]);
```

Worker 使用相同静态模块清单和 `FullNetHostProfile.Worker`。应用 Migrator 单独加载示例程序集 `Migrations.SqlServer` 或 `Migrations.MySql` 下的嵌入脚本；官方迁移程序集不包含示例 SQL。应用模块目录来源为 Application。集成测试通过 `includeEnterpriseRequest: true` 显式装配模块及示例迁移。

`integration-target.json` 是开发者主动执行生成器时的接入目标，执行该接入会修改目标组合根；它不表示官方预设默认启用示例。

## 快速开始

```bash
# 预览生成
dotnet run --project src/Tools/Full.NET.CodeGeneration.Cli -- \
  --schema samples/enterprise-request/schema.json --workspace .

# 规划接入
dotnet run --project src/Tools/Full.NET.CodeGeneration.Cli -- plan-module-integration \
  --schema samples/enterprise-request/schema.json \
  --repository . \
  --target samples/enterprise-request/integration-target.json
```

## E2E

管理端路由 `/enterprise-requests` 的真实栈 CRUD 场景位于 `tests/e2e/admin-real-stack/tests/enterprise-request.spec.mjs`；测试文件存在不表示本轮已经执行。提交审批使用独立的 `enterprise_request.enterprise_requests.submit` 权限，与编辑权限分别授权。

## 草稿与审批边界

头单与明细 Schema 均启用 `entityCapabilities.hasMutationPolicy`。应用自有的 `Features/DraftMutations` 实现并注册生成的强类型策略；重新生成覆盖 `.g.cs` 时业务规则仍保留。普通创建仅允许草稿，提交后禁止主单更新/删除、明细新增/更新/删除及聚合更新，明细不能改挂父单。明细写入与提交操作通过同一主单行锁串行化；状态由提交及 Workflow 结果回写推进。策略在本模块命令事务内执行，不以 UI 隐藏替代服务端约束。

页面“创建主从单据”一次保存主单和最多 128 行明细；“主从明细”按权限提供查看/草稿编辑。聚合读取和更新同时要求明细读取权限，写入使用独立主单创建/更新权限；未开放独立明细 HTTP 路由。数量最多四位小数，单价/金额最多两位，客户端保留十进制字符串与 Int64 字符串版本；不在浏览器用浮点数计算权威业务金额。冲突后保留本地输入，关闭重开获取最新快照。

基础主单“创建/编辑”弹窗关闭、切换企业或会话时清空输入；旧保存和关闭回调不能操作后来打开的弹窗。关闭正在保存的弹窗只取消网络等待，不能证明服务端回滚。此时以及保存返回未知结果时，当前页面保留原申请编号（编辑时含记录 ID），并阻止新的主单保存；先刷新列表或通过原业务入口核对，再明确确认“已核对原保存结果”。确认只解锁，不重发请求，也不代替服务器查询或保证原操作未发生。标记按操作者、会话与企业隔离；页面内提示不能替代业务记录核对。

`contracts/openapi/enterprise-requests.openapi.json` 是应用业务快照，`ui/admin/src/api/enterprise-request-client` 是其生成客户端。更新快照后在仓库根执行：

```bash
node scripts/openapi/generate-fullnet-client.mjs --input samples/enterprise-request/contracts/openapi/enterprise-requests.openapi.json --manifest samples/enterprise-request/contracts/openapi/client-generation-manifest.json --output ui/admin/src/api/enterprise-request-client --http-module @fullnet/client-contracts
node --test samples/enterprise-request/tests/schema.test.mjs
```

本轮主从 Mock 浏览器 4/4（Edge 桌面/窄屏与 axe）、业务状态冻结双库 2/2 及主单锁/提交交错双库 2/2 通过。SQL Server + Kestrel + Vue + 独立 Worker 的真实页面 3/3 通过，覆盖主从编辑/提交冻结、待办业务详情往返、审批/驳回回写 Approved/Rejected 与站内通知，以及同一导入单据的附件、报表和打印。另增两级审批退回旅程 1/1：二级退回一级、相同请求重投固定回执、原轮次重新办理、Approved 回写与通知。退回期间业务仍 Submitted 且只读，不允许从普通 CRUD 修改审批中单据，见[验证记录](../../docs/verification/2026-11-03-enterprise-request-return.md)。跨进程中断及处理中撤权按总计划继续补齐。

待办、实例、抄送中的业务详情通过静态路由白名单进入 `/enterprise-requests?requestId=<UUID>`，需要主单及明细读取权限；对象读取仍由当前租户和数据范围授权。关闭详情清除深链，切换会话取消旧请求。

## XLSX 导入

导入任务页 `/import-export/tasks` 可下载企业申请模板，选择 XLSX 上传并查看原始行号及错误原因，确认后再执行。需要 `import_export.import_tasks.read/create`、`import_export.static_schemas.read` 和本业务创建权限；排队执行另需 `import_export.import_tasks.execute`。Worker 必须采用同一应用模块清单，且开启 `FullNet:ImportExport:ExecutionEnabled`；只装配 API 不能代替独立 Worker。

详情打开时每两秒跟踪排队/执行结果，终态停止；网络失败或五分钟后暂停，使用“刷新详情”重新读取。隐藏页面暂停，关闭或切换会话取消请求。读取超时不会重放执行操作。

模板固定为 `requestNumber,title,totalAmount,applicantUserId,organizationUnitId`，每文件最多 1 MiB、1000 行；金额最多两位小数，ID 使用非空 UUID。只接受真实 XLSX，不接受改名的 CSV；公式、外部关系及超限 ZIP/XML 会被拒绝。预校验不写业务数据，执行时重新验证机构归属。

应用迁移 003 在 SQL Server/MySQL 创建自有行幂等账本；同租户、任务和原始行号的重复执行返回原申请 ID，绑定的源文件、操作人或数据变化则拒绝。账本与申请行在同一事务写入。只导入申请头，状态固定为草稿；明细仍经主从业务入口维护，不能把该模板理解为完整主从导入协议。

本轮 SQL Server/MySQL 的真实 XLSX 预校验、执行及重复回放场景 2/2 通过；真实 Vue 异步导入及后续附件/报表/打印流程在 SQL Server 已通过，MySQL 浏览器流程未执行。

## 附件与文档交付

应用迁移 002 增加自有交付日记；应用 Worker 显式装配此模块后默认每 5 秒恢复上传/报表及清理任务，`EnterpriseRequest:Delivery:Enabled=false` 可停止恢复线程。API 只保存意图和执行业务入口，不自行启动交付 Worker。Reporting/Printing 必须在应用模块集合中启用，分别通过 Workbook/Form Renderer Contract 消费已授权投影；它们不查询样例数据库表。

`/enterprise-requests` 行操作“附件与文档”提供附件、工作簿和打印。附件读/管理、报表创建/读/取消、打印及文档金额使用独立 `enterprise_request.attachments.*`、`reports.*`、`documents.*` 权限；报表/打印同时要求主单与明细读取权限。文档金额策略不改变原 CRUD 行为。任务固定原会话和源版本，下载仍需当前对象权限，不能把 Ready 状态当作已授权的永久链接。

文档弹窗由页面持有，上传或删除附件刷新列表后可继续生成报表和打印。会话、租户或权限变化清除预览并取消在途读取/下载，不接纳撤权前的迟到结果；Worker 从当前权威数据范围重新读取单据，不能借后台身份扩大部门可见范围。

当前页排队/执行中的报表每五秒自动刷新状态，保留原主单版本；终态停止，隐藏页面暂停，单次读取三十秒或总计五分钟后暂停，使用“刷新状态”恢复。该机制只读取状态，不重新创建任务。

页面展示服务端给定的报表有效期与时区，下载仍以服务端当次检查为准，不使用浏览器时钟决定是否过期。取消、过期或删除后的 Cleanup 阶段已经禁止结果访问，但文件仍在清理；页面保留业务终态并有界跟踪清理。取消前确认点击时版本，结果不明或版本冲突后需手动刷新状态，再执行新的写操作。本轮组件和受控 HTTP 浏览器验证状态见[任务控制增量记录](../../docs/verification/2026-11-03-reporting-and-preview-task-controls.md)，真实链路验收仍按该记录区分。

已就绪报表按当前下载会话复核本人、对象、明细、报表及金额读取权限；重新登录不会单独剥夺合法历史下载，读取旧文件不要求仍有新建权限。后台排队执行继续绑定原会话和创建资格，新登录不能恢复旧队列。低权限生成的文件不会因后来提权补入金额；文件打开期间撤权也会阻止最终交付。真实角色变更双库证据见[验证记录](../../docs/verification/2026-11-03-enterprise-request-live-delivery-authorization.md)。

## 普通成员接入与权限

先由所有者邀请用户并完成接受，再分配业务角色、部门隶属及针对目标租户的部门数据范围。主从创建/读取/更新分别使用 `enterprise_request.enterprise_requests.create/read/update`，读取现有明细另需 `enterprise_request.enterprise_request_lines.read`；提交使用独立 `submit` 权限。通过 Host 域切换租户的客户端还需既有 `tenancy.tenants.read/switch` 权限；切换后使用返回的新令牌，默认同一客户端新登录会撤销旧会话。

创建时通过 `X-FullNet-Organization-Unit-Id` 传入有权写入的部门；模板导入使用 `organizationUnitId` 列，执行时重新校验。TenantId 始终来自可信会话。文档金额权限为 `enterprise_request.documents.amounts.read`，未授予时打印/报表不包含合计、单价和行金额，原 CRUD 的金额仍按主单读取契约返回。

普通活动成员专项已在 SQL Server/MySQL 通过，包含部门可见范围、无权部门写入、版本冲突、缺明细权限、打印金额脱敏、正式开通的第二租户隔离及有效/无权部门 XLSX 导入。生成器版本 `2026.11.03.1` 修复受限列表的计数过滤锚点；受影响应用使用真实生成器重新生成主单和明细功能文件。

审批运行中撤权会注销旧会话；重新登录仍需当前审批权限。恢复权限后原待办可以继续，重复请求不重复推进。双库真实撤权及独立 Worker 在流程启动/业务回写后、消息确认前被终止并重启恢复均已通过，后者等待生产租约自然过期，不直接调用恢复批处理。详见[撤权记录](../../docs/verification/2026-11-03-enterprise-request-approval-revocation.md)与[进程恢复记录](../../docs/verification/2026-11-03-enterprise-request-worker-process-recovery.md)；不将该范围外推为 Native 或容量验收。

每附件最大 10 MiB、每单最多 20 个附件；工作簿最多 500 明细、同时 10 个未过期任务，24 小时有效；打印最多 20 行。超限明确拒绝。停用租户待清理记录保留至重新启用，部署方不能将其误记为已经物理删除。验证范围和已知限制见[交付规格](../../docs/superpowers/specs/2026-10-27-enterprise-request-delivery.md)。
