# 全项目审查修复处理状态

2026-09-08 更新：后续四项复审发现的处置与验证见 [恢复问题修复记录](2026-09-08-review-recovery-fixes.md)。以下保留上一阶段的历史处理记录。

日期：2026-09-07。状态：**继续修复中，整个修复清单尚未完成**。

## 范围与停止边界

用户先授权执行全项目审查修复，中途要求 AI 生成所有权问题收尾后停止；随后要求继续修复。本轮已完成 R05、R17、R12、R18 本地治理切片与本地架构/OpenAPI 门禁、R14 本地 AOT 分析/选择器重跑，以及 R15 本地 Vue 传输/类型与生产构建。双库运行、Linux 原生发布、真实渠道/Worker 崩溃注入、真实浏览器 E2E 仍未完成，不标记 `Verified`。

修复基线仍为已提交的 `6fafd5ea00f1981ba223126936c06672eecb1dd6`，分支 `main`。本轮任务快照为 `full-project-review-fixes-20260907-r05`、`full-project-review-fixes-20260907-r17`、`full-project-review-fixes-20260907-r12`、`full-project-review-fixes-20260907-r18`、`full-project-review-fixes-20260907-r18-remain`、`full-project-review-fixes-20260907-r18-openapi`、`full-project-review-fixes-20260907-r14`、`full-project-review-fixes-20260907-r15`、`full-project-review-fixes-20260907-r18-gates`、`full-project-review-fixes-20260907-r18-openapi-remain`、`full-project-review-fixes-20260907-r18-gov-naming`、`full-project-review-fixes-20260907-unit`、`full-project-review-fixes-20260907-r13-uuid`、`full-project-review-fixes-20260907-migrations`、`full-project-review-fixes-20260907-focused-modules`、`full-project-review-fixes-20260907-r03-r08`。未推送、未发布，也未将迁移应用到业务数据库。保留了任务开始前已有的审查文档和用户工作区中既有的 `Full.NET.slnx` 变更。

输入：[原始架构与代码审查](2026-09-07-full-project-architecture-code-review.md)。执行清单：[活动修复计划](../superpowers/plans/2026-09-07-full-project-review-fixes.md)。原始审查报告保留为历史证据，本记录说明修复后的实际进展，不能反向修改原始评估事实。

## 当前 AI 问题的处理结果

- 数据库以 `GenerationId`、到期时间及取消标志控制生成所有权，获取槽位与初始消息写入处于同一短事务。
- 本地注册、取消、消息完成和释放均核对生成代次，旧请求的取消或 finally 不会影响新请求。
- 独立数据库作用域立即续租并每 5 秒检查，单轮等待最多 10 秒；本地独立租约期限在数据库不返回时仍取消外部推理。调用模型前、完成消息前都再次确认租约。
- HTTP 启动失败会释放已提交的槽位；消息写入失败回滚启动事务。过期状态不会让客户端永久等待，新生成会收敛遗留 streaming 消息。
- 新增 205 双库迁移，保留新版本有效租约；升级时停止旧 API 实例。SQL Server 对引用新增列的更新使用动态执行，避免同批编译错误。
- 配套补齐模型租户读取、204 配额预留/幂等结算、有限响应读取和 OpenAI `usage:null` 兼容。

独立只读复核发现的“迟到取消误伤后继”和“续租等待超过租约期限”均已修正并补测试。复核未发现这两项范围内的剩余明确阻断；静态复核不替代数据库运行验证。

## R05 外部副作用的处理结果

- 支付下单、退款、金蝶 Save/Submit、OCR 识别都先用短事务提交本地意图和幂等键，再在事务外调用提供程序，最后用另一短事务回写结果。
- 超时、取消和传输失败写入 `provider_unknown`，不把可能已经生效的外部操作标记为失败。
- 渠道已成功但本地回写失败时保留已提交意图，返回未知错误，供后续对账；对账查询渠道时不再占用本地事务。
- 退款通过订单 `succeeded → refunding` 领取互斥；pending 金蝶同步在 45 秒租约内拒绝并发重试。微信退款使用已持久化的 `OutRefundNo` 作为渠道幂等键。
- 新增 206 双库迁移，扩展支付/退款/金蝶/OCR 状态 CHECK，允许 `provider_unknown`。升级时停止旧 API 实例后再应用。

本地单元已覆盖“事务外调用、超时未知、回写失败保留意图、并发退款/重试所有权”。未对真实支付网关、金蝶或 OCR 发请求，也未运行双库 Integration。

## R17 任务恢复与文件对账的处理结果

- 导入任务按租约领取 `queued` 或租约过期的 `executing`；进度与失败更新绑定当前 `LeaseId`。Host 取消或意外异常保留 `executing`，不误标失败。
- 错误回执先 `ListReadyAsync` 复用已有非源文件，避免二次上传；终态重复完成按 0 行更新短路。
- 报表导出先持久化 `queued`，同请求尝试执行；崩溃后绑定已上传 ready 文件，取消时保留 `processing`。Worker 用创建时权限快照重建主体。
- 租户资源文件 pending：blob 存在则提升 ready，缺失则清除，探测失败跳过；ready 仅在所有者确认无引用后释放。
- 新增 207 双库迁移，追加导入/报表租约列、报表 `queued` 状态与领取索引。升级时停止旧 API/Worker 后再应用。

本地单元已覆盖租约重领、取消不失败、上传后恢复与孤儿文件对账。未运行双库 Integration，也未做真实 Worker 进程崩溃注入。

## R12 Host 空租户并发幂等的处理结果

- 查找仍用 `TenantId IS NULL` 精确匹配 Host 行；插入撞到唯一约束后回读，摘要相同则回放、不同则冲突，不再次发布。
- MySQL 增加 `ScopeTenantKey` 生成列（`COALESCE(TenantId, 0x00…)`），唯一索引改为 `(ScopeTenantKey, IdempotencyKey)`，闭合可空 TenantId 在 InnoDB 中互异的窗口。
- SQL Server 增加持久化 `ScopeTenantKey` 计算列，并以 `IdempotencyKey IS NOT NULL` 过滤唯一索引替换旧的 `(TenantId, IdempotencyKey)` 索引。
- 新增 208 双库迁移。升级时停止旧 API 后再应用。未运行双库 Integration。

## R18 模块边界与治理的处理结果

- Identity 以可选合同依赖声明 Files，Tenancy 声明 Files+Printing，Organization 声明 ImportExport；硬 `Dependencies` 不改，以免 Minimal 预设装上 Files/ImportExport。
- `pnpm generate:module-dependency-graph` 已按硬依赖重写 `docs/operations/module-dependency-graph.mmd`。
- 报表模块去掉 `Microsoft.Data.SqlClient`/`MySqlConnector`。外部库打开、`SELECT 1` 探活和结果读取改由 `Full.NET.Data.Abstractions` 的会话接口完成，实现留在 `Full.NET.Data.Dapper`。外部 MySQL 不套用主库 Binary16 Guid 策略。
- OCR 在事务外读取 Files 描述；支付下单与 AI 配额在事务外查询 Identity 租户目录；文档历史版本删除提交后再调用 Files `ReleaseAsync`，与新增版本的 Claim 边界一致。`module-local-transaction-debt.json` 保持空目录。
- 对象注释目录按迁移重新生成 151 张表的中文说明，并写回双库 COMMENT/`MS_Description`；未把注释应用到业务数据库。
- Vue 生产 API 99 个模块全部进入 `vue-client-coverage-v1.json`；`request<T>` 改为 `unknown` 加运行时守卫或生成客户端；补齐 Host 虚拟目录及备份/国密/MQTT/品牌/钉钉/微信等离线 OpenAPI 夹具。
- 未登记的动态 SQL、条件 DDL 与历史内联主键/压缩索引名写入 `naming-debt.json` 精确条目，不改写已发布对象名。Jobs `UnexpectedCommandExecutor` 夹具此前已绿。
- 官方业务程序集目录与 `OfficialModuleNames` 一致；新增门禁要求每个官方模块发布 `IAuthorizationCatalogContributor`。ImportExport Worker 补登记 Dapper 物化器；Files 物化器清单补齐租户资源与目录行。
- 已发布的四段权限码与两段错误码写入 naming-debt，不改公共契约。头像/Logo 异步 Endpoint 方法补 `Async` 后缀。70 条 Global SQL 按真实类别入目录。
- ADR-0002 已按当前装配同步：28 个官方模块、Platform/Content 预设、`Organization`/`Settings`/`Files` Contracts 的真实消费者，以及 `Tenancy.Http` 已合并回主项目；Architecture 继续禁止恢复该拆分。
- 客户端生成器把 Excel 工作簿下载收成 Blob，跳过 SSE，支持 `allOf` 与批量文件 append；users/host-files 试点 API 改为生成 Operation 薄适配。兼容门禁允许匿名回调补空 `security`、me 夹具追加改密路径、覆盖清单追加绑定、规范化键顺序，以及清理未被路径引用的闲置 Schema。
- 客户端生成快照与 manifest 已双向对齐为 525 个 Operation：补齐 39 个缺失路径（角色复制/成员、自助档案、租户品牌、职位工作簿、公告已读等），并把快照中已有的 41 个额外 Operation 登记进 manifest。已重新生成 client-contracts。
- 未降低架构门禁。本轮已重跑完整架构测试 213 通过；治理 52、命名 31、OpenAPI 166 通过；离线 `pnpm openapi:client:snapshot --check --offline` 通过。全量 Unit 2424 通过、1 项 Windows 上 Linux FIFO 跳过。双库运行与 Linux 原生发布仍待 CI。

## R14 Native AOT 分析重跑的处理结果

- `pnpm test:aot:analyzers` 在外部库会话接口与物化器登记后重跑，0 警告 / 0 错误。
- `pnpm test:dotnet:architecture --selection api-native-aot` 73 通过（选择器最低发现数 36）。
- 状态为 `Aot-analysis-clean`，不是 `Aot-published`。未执行 Linux 原生发布或进程 E2E。

## R15 Vue 传输与类型的处理结果

- 工作流表单附件上传把取消信号放到 `uploadHostFile` 第三参，下载走共享 `requestBlob`。
- 手写 AI 会话与通知意图 API 发送 JSON、声明 `application/json` 并透传 AbortSignal。
- 办理人预览列表改为接受契约只读 `WorkflowRecipientCandidateResponse[]`，生产 typecheck 与 Vite 构建通过。
- 未运行真实浏览器 Playwright。本轮已补跑治理、命名与完整 OpenAPI 门禁。

## 全量 Unit 与 R13 摘要重记账的处理结果

- 对象注释写回已发布 MySQL 脚本后，165–199 中 15 份 `char(36)` 脚本的全文 SHA-256 与预处理器白名单不一致，执行期不再改写为 `BINARY(16)`。历史 DDL 未改；仅按归一化换行后的当前全文更新 15 份摘要，173 仍匹配。
- 影响集规划（累计快照 `full-project-review-fixes-20260907`，inner）约 581 个变更文件、估时约 152 分钟，超过 10 分钟预算。当时 202–208 尚未登记。未启动 Docker，也未把 203/206/207/208 应用到业务库。
- `pnpm test:dotnet:unit` Release：2425 总计，2424 通过，0 失败，1 跳过（`Linux_file_replaced_by_fifo_after_validation_is_rejected_without_blocking`，Windows 上 Inconclusive，预期）。未调整 `eng/testing/test-matrix.json` 的 `unit.minimum`（仍为 2304；本切片未新增用例）。

## 迁移恢复登记与 R13 外键/中间态测试的处理结果

- 影响集矩阵补登记已有 138–153（含 151/153，跳过无恢复夹具的 150/152）以及审查修复新增的 202–208，避免这些脚本继续降级到完整 migrations 分片。
- 新增 202/204/205/206/207/208 双库恢复测试：删表/删列/回退 CHECK 或旧唯一索引后重跑必须收敛；202 断言不出现跨模块外键。
- R13 在 203 上补文档访问日志/预览任务外键恢复，以及同一行 `BINARY(16)` 与 `VARBINARY(36)` 混合中间态收敛。
- `migrations.minimum` 406→420，`full.minimum` 739→753。工具链断言改为“恢复夹具必须登记”，保留 121–126 的 Application/Index/Title 选择器。
- 本切片影响集 inner 估时约 45 分钟（共享 Support 夹具仍会带上 migrations 分片）；未启动 Docker，未把 202–208 应用到业务库。

## 聚焦模块登记与 R02/R10 双库测试的处理结果

- 影响集 `focusedModules` 补登记 Calendar、Regions、ImportExport、Ai、Reporting。Calendar/Regions 走已有 `*Api` 过滤器；ImportExport 同时覆盖 API 与 `IntegrationTests.ImportExport.` 持久化夹具；Ai/Reporting 无 API 对时只覆盖对应持久化命名空间。Payments/Mqtt/Ocr 等仍无匹配测试，继续降级 Smoke。
- 补齐 `ImportExportApiMySqlTests`，与 SQL Server 预览契约配对。
- R10：`AiQuotaReservationPersistence*` 使用生产 `AiQuotaReservationSql`，覆盖最后额度并发预留、跨月计数重置、未知用量结算不退款。
- R02：ImportExport/Reporting 领取测试使用生产 Claim SQL，覆盖 A/B 租户隔离、双连接争抢、到期租约重领。
- `api-mysql.minimum` 74→75，`infrastructure.minimum` 128→146，`full.minimum` 753→772。未启动 Docker，未运行这些双库用例。

## R03/R08 双库持久化测试的处理结果

- Files/Notifications 聚焦过滤器同时覆盖 `*Api` 与对应 `IntegrationTests` 持久化命名空间。
- R03：`TenantResourceFilePersistence*` 使用生产 SQL，覆盖租户/模块/资源三元所有权、ListReady 范围、跨租户不得提升或清除 pending。
- R08：投递领取争抢、到期后旧世代不能续租/完成、验证码失败计数上限与一次性消费；隔离库上关闭投递/挑战外键，只验证锁与 fencing SQL。
- `infrastructure.minimum` 146→162，`full.minimum` 772→788。未启动 Docker，未运行这些双库用例。

## 18 组发现的实际进度

“本地回归通过”只覆盖已执行测试；“待验证”不得解释为已经通过完整门禁。

| 编号 | 处理情况 | 当前状态 / 剩余工作 |
| --- | --- | --- |
| R01 | 通知服务与 Endpoint 开关装配一致，开关矩阵测试通过 | 已修复并有本地回归；后续模块目录调整后的完整架构门禁仍需重跑 |
| R02 | ImportExport/Reporting 任务 SQL 增加可信租户绑定与谓词；Worker 按活动租户领取 | 17 条真实 ScopeGuard 回归通过；已补双库租户隔离/并发领取/到期重领测试（未运行） |
| R03 | 新增租户资源文件合同、所有权元数据与 202 双库迁移；替换导入/报表 Host 文件调用，保留受资源所有者确认的旧引用读取 | 文件测试已含 R17 对账；pending/ready 孤儿本地回归已补；已补双库所有权谓词/ListReady/pending 提升清除测试（未运行） |
| R04 | 支付/退款业务编号使用完整 Guid，避免截取 UUID v7 时间前缀造成碰撞 | 已修复，有固定时钟回归 |
| R05 | 支付、退款、K3Cloud、OCR 外部调用与本地事务分离，可靠外部意图与重试所有权 | 已完成本地代码与单元回归；206 双库运行、真实渠道对账和崩溃注入仍待补齐，不得标记 Verified |
| R06 | 微信回调绑定商户、租户、订单、渠道、币种；保护退款等终态；验签原文、失败 HTTP 500；凭据唯一冲突回滚后重试 | 已修复；支付命名空间现为 37 项通过 |
| R07 | 打印布局使用 DOMPurify 白名单净化，保留合法样式 | 攻击回归 2 项通过；现有 uni-app/Vite 依赖高危仍存在 |
| R08 | 限制秘密引用；验证码失败计数提交、发送脱离事务、串行挑战消费；投递租约与超时预算；按持久化模板版本重放 | 通知 147 项通过；已补双库投递争抢/到期 fencing 与验证码计数消费测试（未运行） |
| R09 | AI 租户模型/配额消费 SQL 与 Host 管理 SQL 分离，可信租户绑定；配额写入前在事务外确认租户活动 | 实际服务与 ScopeGuard 回归通过；AI 单元现含配额事务边界 2 项 |
| R10 | 原子占用请求与 Token，204 预留凭据、原月份幂等结算；取消/未知用量保留预算；Guard 改 Scoped | 本地回归通过；已补双库并发预留、跨月重置与未知结算不退款测试（未运行），未关闭整组 |
| R11 | 生成代次、持久化租约、启动事务、跨实例取消、本地期限、过期显示与旧代清理隔离 | 上一阶段已收尾；本地验证通过，双库持久化竞争测试已编写但未执行 |
| R12 | MQTT 保存完整正文 SHA-256 并比较幂等语义；Host 空租户 `ScopeTenantKey` 哨兵唯一索引；并发 UniqueConstraint 回放 | **本轮已完成本地代码与单元回归**（MQTT 15 项）；208 双库运行仍待补齐，不得标记 Verified |
| R13 | 16 份已发布 MySQL 脚本按全文摘要精确兼容；203 前向迁移修复 68 个 UUID 列，不修改历史文件 | **本轮已按当前全文重记账 15 份摘要**，并补文档外键与混合中间态双库测试（未运行）。203 及 202/204–208 已登记聚焦恢复集。双库升级仍待 CI，不得标记 Verified |
| R14 | 修复 AOT 参数、物化、源生成 JSON 与提供程序边界中的已发现静态错误 | **本轮已重跑本地分析**：`pnpm test:aot:analyzers` 0 警告/0 错误；`api-native-aot` 选择器 73 通过（最低发现数 36）。状态为 `Aot-analysis-clean`，不是 `Aot-published`；Linux 原生发布与进程 E2E 仍待 CI |
| R15 | 修复请求 JSON、文件上传/下载、客户端合同、类型与 Vue 测试夹具 | **本轮已完成本地代码与回归**：附件传输、JSON 传输、client-contracts 167、设计器预览只读数组、`vue-tsc` 与 Vite 生产构建。全量 vitest 722 通过另有 2 项在高负载下超时、隔离复跑 35 通过。真实浏览器 E2E 与完整前端/目录门禁仍待 CI |
| R16 | 报表收集预算、行列上限、流式 worksheet、ZIP 写入限制；AI 帧/协议/正文限制、共享缓冲与有限历史 | 聚焦回归通过；不是内存基准或容量认证，真实大数据/外部提供程序验证未执行 |
| R17 | 导入/报表执行租约、崩溃恢复、幂等完成与租户文件 pending/ready 对账 | **本轮已完成本地代码与单元回归**；207 双库运行、真实 Worker 崩溃注入仍待补齐，不得标记 Verified |
| R18 | 补全官方模块目录与 Platform 文件依赖；修正部分静态扫描、目录查找、Regions 参数/支付异常边界与 Jobs 夹具 | **本轮已完成本地切片与本地门禁**：依赖、报表会话、跨模块事务、目录/OpenAPI/命名、授权/物化器、Endpoint 授权、OpenAPI Operation 身份、ADR-0002 拓扑同步、生成快照与 manifest 525 项对齐；架构 213、治理 52、命名 31、OpenAPI 166 通过，离线快照 check 通过。双库运行仍待 CI，不得标记 Verified |

## 本轮新鲜验证

| 命令 | 实际结果 |
| --- | --- |
| `dotnet run --project tests/Full.NET.UnitTests -- --filter FullyQualifiedName~PaymentExternalSideEffectTests` | 5 通过，0 失败 |
| `dotnet run --project tests/Full.NET.UnitTests --no-restore -- --filter FullyQualifiedName~Full.NET.UnitTests.Payments` | 35 通过，0 失败，0 跳过 |
| `dotnet run --project tests/Full.NET.UnitTests --no-restore -- --filter FullyQualifiedName~Full.NET.UnitTests.K3Cloud` | 9 通过，0 失败，0 跳过 |
| `dotnet run --project tests/Full.NET.UnitTests --no-restore -- --filter FullyQualifiedName~Full.NET.UnitTests.Ocr` | 9 通过，0 失败，0 跳过 |
| `dotnet run --project tests/Full.NET.UnitTests -- --filter FullyQualifiedName~ImportExportTaskRecoveryTests` | 5 通过，0 失败 |
| `dotnet run --project tests/Full.NET.UnitTests --no-restore -- --filter FullyQualifiedName~ReportingExportTaskRecoveryTests` | 4 通过，0 失败 |
| `dotnet run --project tests/Full.NET.UnitTests --no-restore -- --filter FullyQualifiedName~PendingTenantResourceFileReconciliationTests` | 3 通过，0 失败 |
| `dotnet run --project tests/Full.NET.UnitTests --no-restore -- --filter FullyQualifiedName~Full.NET.UnitTests.ImportExport` | 10 通过，0 失败 |
| `dotnet run --project tests/Full.NET.UnitTests --no-restore -- --filter FullyQualifiedName~Full.NET.UnitTests.Reporting` | 25 通过，0 失败 |
| `dotnet run --project tests/Full.NET.UnitTests --no-restore -- --filter FullyQualifiedName~Full.NET.UnitTests.Files` | 87 通过，0 失败 |
| `dotnet run --project tests/Full.NET.UnitTests --no-restore -- --filter FullyQualifiedName~Full.NET.UnitTests.Mqtt` | 15 通过，0 失败（含 Host 并发幂等 6 项） |
| `dotnet run --project tests/Full.NET.ArchitectureTests -- --filter FullyQualifiedName~ReportingModuleArchitectureTests\|BusinessModules_DoNotDependOnDapperOrAdoNetProviders\|ModuleLocalTransactionBoundaryTests` | 7 通过，0 失败 |
| `dotnet run --project tests/Full.NET.UnitTests -- --filter FullyQualifiedName~OcrIdCardTaskSideEffectTests\|PaymentExternalSideEffectTests\|AiTenantQuotaTransactionTests` | 14 通过，0 失败 |
| `dotnet run --project tests/Full.NET.UnitTests --no-restore -- --filter FullyQualifiedName~Full.NET.UnitTests.Ocr\|Payments\|Ai\|Reporting\|Document\|FullNetModuleCatalogTests` | 135 通过，0 失败 |
| `git diff --check` | 通过（仅 CRLF 提示） |
| `node --test tests/naming/sql-comments.test.mjs tests/naming/sql-naming.test.mjs tests/openapi/openapi-fixture-coverage-contract.test.mjs tests/openapi/vue-client-contract-coverage.test.mjs` 及新增夹具测试 | 25 通过，0 失败 |
| `dotnet run --project tests/Full.NET.ArchitectureTests -- --filter FullyQualifiedName~NamingConventionTests\|GlobalSqlStatementCatalogTests\|Production_business_module_assembly_catalog\|Official_business_modules_publish_authorization\|FilesModule_RegistersAllNativeAotRowMaterializers\|WorkerNativeAot_BackgroundModulesRegisterDapperMaterializers` | 10 通过，0 失败 |
| `dotnet run --project tests/Full.NET.ArchitectureTests -- --filter FullyQualifiedName~OpenApiOperationIdentityRulesTests\|EndpointAuthorizationTests\|Composition_uses_tenancy_core_project` | 30 通过，0 失败 |
| `pnpm test:aot:analyzers` | 0 警告 / 0 错误（含 `IExternalDatabaseConnectionFactory` 与后续物化器登记后的 Host.Api 可达闭包） |
| `pnpm test:dotnet:architecture --selection api-native-aot` | 73 通过，0 失败（选择器最低发现数 36） |
| `pnpm --filter @fullnet/admin exec vitest run src/api/json-request-transport.test.ts src/workflow/workflow-form-attachments.test.ts src/api/host-files.test.ts src/api/notification-intents.test.ts` | 11 通过，0 失败 |
| `pnpm --filter @fullnet/client-contracts test` | 167 通过，0 失败 |
| `pnpm --filter @fullnet/admin exec vitest run src/workflow/WorkflowVue3Designer.test.ts` | 9 通过，0 失败 |
| `pnpm --filter @fullnet/admin typecheck` | 通过 |
| `pnpm --filter @fullnet/admin exec vite build` | 通过 |
| `pnpm --filter @fullnet/admin test` | 214 文件：722 通过，2 失败（RolesView/UsersView 5s 超时）；隔离复跑上述两文件 35 通过 |
| `git rev-parse HEAD` / `git branch --show-current` | `6fafd5ea`，`main`；R05/R17/R12/R18/R14/R15 变更保留于未提交工作区 |
| `dotnet run --project tests/Full.NET.ArchitectureTests` | 213 通过，0 失败 |
| `pnpm test:openapi` | 165 通过，0 失败（含相对 HEAD 无破坏变化） |
| `pnpm --filter @fullnet/client-contracts test` | 167 通过，0 失败 |
| `pnpm --filter @fullnet/admin typecheck` | 通过 |
| `pnpm --filter @fullnet/admin exec vitest run src/api/users.test.ts src/api/host-files.test.ts` | 14 通过，0 失败 |
| `git diff --check` | 通过（仅 CRLF 提示） |
| `pnpm test:governance` | 52 通过，0 失败 |
| `pnpm test:naming` | 31 通过，0 失败 |
| `pnpm openapi:client:snapshot --check --offline` | 通过；快照与 manifest 均为 525 个 Operation |
| `pnpm test:openapi` | 166 通过，0 失败（含相对 HEAD 无破坏变化；新增规范化键顺序/闲置 Schema 用例） |
| `node scripts/openapi/generate-fullnet-client.mjs --check` | 生成产物零漂移 |
| `pnpm --filter @fullnet/client-contracts test` | 167 通过，0 失败 |
| `pnpm --filter @fullnet/admin typecheck` | 通过 |
| `git diff --check` | 通过（仅 CRLF 提示） |
| `pnpm test:integration:affected:plan -- --snapshot full-project-review-fixes-20260907 --phase inner` | 581 个变更文件；估时约 152 分钟；未启动双库 |
| `dotnet run --project tests/Full.NET.UnitTests --configuration Release -- --filter FullyQualifiedName~MySqlPublishedUuidCompatibilityTests` | 4 通过，0 失败 |
| `pnpm test:dotnet:unit` | 2425 总计，2424 通过，0 失败，1 跳过（Linux FIFO，Windows Inconclusive） |
| `git diff --check` | 通过（仅 CRLF 提示） |
| `dotnet build tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release` | 0 警告 / 0 错误 |
| `pnpm test:integration:tooling` | 44 通过，0 失败 |
| `pnpm test:integration:affected:plan -- --snapshot full-project-review-fixes-20260907-migrations --phase inner` | 10 个变更文件；聚焦 202–208；Support 夹具仍带 migrations 分片；估时约 45 分钟；未启动双库 |
| `git diff --check` | 通过（仅 CRLF 提示） |
| `dotnet build tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release` | 0 警告 / 0 错误（含 R02/R10 持久化夹具与 ImportExport MySQL API） |
| `pnpm test:integration:tooling` | 46 通过，0 失败 |
| `pnpm test:integration:affected:plan -- --snapshot full-project-review-fixes-20260907-focused-modules --phase inner` | 13 个变更文件；目标 Ai/ImportExport/Reporting + 工具链；inner 只跑工具链约 1 分钟；模块过滤器留给 slice/CI；未启动双库 |
| `git diff --check` | 通过（仅 CRLF 提示） |
| `dotnet build tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release` | 0 警告 / 0 错误（含 R03 文件所有权与 R08 投递/挑战夹具） |
| `pnpm test:integration:tooling` | 46 通过，0 失败 |
| `pnpm test:integration:affected:plan -- --snapshot full-project-review-fixes-20260907-r03-r08 --phase inner` | 13 个变更文件；目标 Files/Notifications + 工具链；inner 只跑工具链约 1 分钟；未启动双库 |
| `git diff --check` | 通过（仅 CRLF 提示） |

上一阶段 AI/AOT/Integration 编译结果仍有效于当时基线；本轮已重跑完整架构测试、治理、命名、OpenAPI 离线套件与 snapshot check、client-contracts、Vue 生产 typecheck、全量 Unit，以及 Integration 工具链。202–208 与存量 138–153 恢复夹具已登记。Calendar/Regions/ImportExport/Ai/Reporting 已进入聚焦模块。Files/Notifications 过滤器已覆盖持久化夹具。按仓库规则，本次未启动 Docker、SQL Server/MySQL 容器、完整浏览器或 Linux Native AOT 发布，也未擅自提交/推送触发 CI。

最近一次全量 Unit 为 **2424 通过、0 失败、1 Linux 专用跳过**。本轮架构 213、治理 52、命名 31、OpenAPI 166、Integration 工具链 46 通过。不得标记 `Verified`。

## 继续时的边界

下一优先项为 GitHub Actions 上的双库 Integration（含已登记的 202–208/203，以及本切片编写的配额/领取/文件所有权/投递租约持久化测试）、Linux `pnpm test:aot:publish:linux` 与真实 Playwright。不要把 202–208 应用到业务库。原始问题涉及的租户、事务和恢复不变量已有规则覆盖，本次修正实现，不新增近义规则或 Skill。
