# Realtime SignalR 基础验证记录（2026-07-26）

- 范围：`IRealtimePublisher`、通知 Hub、JWT 鉴权、用户/租户分组、MessagePack、专用 Redis ready、双节点故障恢复与 Vue/Layui 管理端实时客户端
- 计划：[实施计划](../superpowers/plans/2026-07-26-realtime-signalr-foundation-vertical-slice.md)
- 故障恢复：[验证记录](realtime-redis-backplane-recovery-2026-07-26.md)
- 状态：**Build-verified**

## 自动化证据

| 层 | 结果 |
|---|---|
| Unit | `RealtimeGroupsTests` **2/2** → **349 → 351** |
| Architecture | `BusinessModules_DoNotDependOnSignalRHubContext` 等 **40/40** → **38 → 40** |
| Integration 双库 | `Realtime_hub_and_probe` SQL Server/MySQL **2/2** → **156 → 158** |
| Redis 故障恢复 | SQL Server/MySQL 双 API 节点 **2/2**；`HealthEndpointTests` **8/8** |
| 当前 canonical 门槛 | **392/7/49/189** |
| 浏览器共享契约 | `@fullnet/client-contracts` **75/75** |
| Vue / Layui | **200/200** / **95/95** |
| Mock parity | **99/99** 通过，按项目矩阵跳过 **5** |

## 行为摘要

- Hub：`/hubs/notifications`；`[Authorize]`；连接后加入 `user:{id}` 与可选 `tenant:{id}` 组
- 发布：`IRealtimePublisher.PublishToUserAsync` / `PublishToGroupAsync`
- 浏览器客户端可通过 `?access_token=` 传递 JWT（与 Identity 会话校验链兼容）
- `Realtime:Enabled=false` 时注入 `NullRealtimePublisher`
- Redis Backplane：配置 `Realtime:RedisBackplaneConnectionString` 或复用 `ConnectionStrings:redis`
- 配置 Backplane 后注册 `realtime-backplane` ready 探针；中断不影响 live/startup
- 运行连接保留后台重连并使用 `fullnet:{environment}:signalr:` Channel Prefix
- 固定 Redis 端点 stop/start 后，无需重启两个 API 宿主或 SignalR 客户端即可恢复跨节点投递
- 管理端：认证后连接 `/hubs/notifications`；Access Token 仅由内存会话闭包按需提供；首次 `start()` 失败后按 **0/2s/10s/30s** 退避重建连接；切换 Host/租户上下文时取消旧重试并连接新上下文，匿名、退出和卸载时断开
- 通知消费：只接受已登记稳定机器码；Vue/Layui 同步真实未读徽标，并在当前站内信或公告页收到对应事件时刷新 HTTP 数据
- 降级：初始连接失败会在后台自恢复，重试或断开失败均不破坏登录、退出、租户切换与通知页面 HTTP 主流程

## 非目标

- 生产多副本编排、告警路由实装与演练、Redis Cluster/Sentinel、非浏览器客户端

## Redis ready 可观测性增补（2026-07-30）

- Realtime BuildingBlock 自注册 `fullnet.realtime` Meter，不再要求 API 或 Worker
  `Program.cs` 复制 Meter 名称。
- ready 探针记录无标签 `readiness.state`，以及仅含封闭 `outcome` 标签的
  `readiness.checks` 和 `readiness.duration`；允许值固定为 `healthy`、`timeout`、
  `failure`，不记录端点、连接字符串、租户、用户或异常文本。
- Redis 短连接与 `PING` 已隔离为内部探针；确定性 Unit 覆盖健康、内部两秒超时、
  普通失败、调用方取消传播和指标监听器故障旁路，Realtime 聚焦 Unit 与 BuildingBlock
  Release build 通过。
- 运维文档已给出逐实例连续失败、窗口错误比例和尾延迟接近两秒预算的初始告警门槛，
  并区分 `timeout` 与 `failure` 排障路径。
- 本切片使用独立任务快照运行 affected inner 门禁；测试矩阵/治理校验与 Realtime
  SQL Server/MySQL 聚焦 Integration 通过，Testcontainers teardown 后确认 Docker
  为空。生产 Collector、告警规则部署、升级路由和多副本演练仍未完成，因此状态保持
  `Build-verified`。

## SignalR 发布取消与可观测性增补（2026-07-30）

- `IRealtimePublisher` 的取消令牌现在传入 SignalR `SendCoreAsync`，不再被强类型客户端
  代理适配层丢弃；客户端方法名仍为 `ReceiveMessageAsync`，载荷仍为单一
  `RealtimeMessage`。
- 发布任务按 `target=user|group` 和 `outcome=success|failure|canceled` 记录尝试次数
  与耗时；不记录用户、组名、租户、消息机器码、异常类型或异常文本。
- 调用方取消与普通失败分开计数并继续传播原始异常；指标监听器故障不会改变发送结果。
- `fullnet.realtime` Meter 已提升为所有启用模式自注册，因此无 Redis 的单节点发布也可
  导出指标；ready 探针仍只在配置 Backplane 时注册。
- 运维基线明确发布成功只代表服务端发送任务完成，并分别给出失败率、取消率和 P99
  相对基线告警；可靠业务状态和重试仍由数据库与事务 Outbox 提供。
- 独立任务快照的 slice 计划因其它窗口后写入文件同时命中 CodeGeneration；本窗口按测试矩阵
  的同一 `FullyQualifiedName~Realtime` 过滤器执行聚焦真实栈，SQL Server/MySQL、Redis
  Backplane、ready 降级与 Worker 修复路径全部通过，未越界执行 CodeGeneration 目标。
- 真实栈结束后本窗口自身 Testcontainers 会话已退出；同时启动的 CodeGeneration affected
  会话完成后再次确认 `docker ps` 为空，并已按窗口队列正式释放 Docker。

## Hub 广播作用域失败关闭增补（2026-07-30）

- Hub 不再把缺失或无法解析的租户 Claim 推断为 Host；只有有效 `sub` 且
  `fullnet_scope=host`、无租户 Claim 的主体才加入 Host 广播组。
- 租户广播组要求规范租户 scope 与 `fullnet_tenant_id` 精确对应。Claim 缺失、畸形或
  相互矛盾时不会加入用户、Host 或租户组，避免其它认证方案的 subject 碰撞被放大为授权。
- 正常 JWT 会话与 Host API Key 都已显式携带有效 scope，因此现有 Host/租户连接行为保持
  不变；该防线用于避免宿主新增认证方案或异常主体时把不完整上下文放大为广播授权。
- 回归测试先稳定复现旧实现对三类不自洽主体的错误分组，再锁定正常 Host、正常租户与
  失败关闭路径；追加的最小权限复核还锁定缺失 Full.NET scope 时不加入任何组。
- 独立任务快照的 inner 计划因其它窗口后写入矩阵同时命中 `integration-matrix` 与
  Realtime；本窗口未覆盖矩阵，按矩阵同一 Realtime 过滤器执行 SQL Server/MySQL、
  Redis Backplane、ready 与 Worker 修复聚焦真实栈并通过。Testcontainers teardown 后
  `docker ps` 为空。

## Hub 分组授权遥测增补（2026-07-30）

- `fullnet.realtime.hub.authorization.decisions` 使用唯一固定标签 `outcome`，枚举仅包含
  `authorized_host`、`authorized_tenant`、`rejected_invalid_subject` 与
  `rejected_scope_claim_mismatch`，不记录用户、租户、连接或 Claim 原值。
- 指标表达 Claim 授权决策，不把后续 `AddToGroupAsync` 或浏览器连接状态误报为成功；
  指标监听器失败被旁路，不改变失败关闭分组或正常 Host/Tenant 分组。
- RED 隔离构建 **0 warning / 0 error**；既有 Hub 测试 **7/7** 通过，新增四个结果断言均
  因缺少 measurement 精确失败。最小实现后 Hub 聚焦 **11/11** 通过。
- 共享 Jobs benchmark 当前切片恢复后，使用正常 ProjectReferences 新鲜构建 Unit
  项目 **0 warning / 0 error**，完整 Realtime Unit 聚焦 **31/31** 通过。
- 本切片使用任务快照 `realtime-hub-authorization-telemetry-20260730`，不修改测试矩阵，
  也不覆盖 Files、Jobs、Notifications 或 CodeGeneration 窗口文件。等待 Docker 队列期间
  快照继续吸收了 CodeGeneration 与 Integration 工具链变更，inner 最终执行工具契约
  **39/39**、CodeGeneration + Realtime UID 去重真实栈 **26/26**；Integration Release
  build **0 warning / 0 error**。TRX 按目标复核为 Realtime **7/7**、CodeGeneration
  **19/19**。Testcontainers/Ryuk 退出后 `docker ps` 为空，
  `.stack-state.json` 与 `.stack-state.lock` 均不存在。
- slice 计划随后又吸收 Files 窗口变更，组合目标扩展为 CodeGeneration、Files、
  Integration 工具链与 Realtime。本窗口未在 Docker 已交回 Jobs 后重复跨范围执行；
  各窗口继续按所有权和容器队列保留各自聚焦证据。

## Hub 分组操作遥测增补（2026-07-30）

- Claim 授权通过后，用户组与 Host/租户广播组的实际 `AddToGroupAsync` 分别按
  `target=user|broadcast`、`outcome=success|failure|canceled` 记录次数与耗时；不记录
  用户、租户、连接、实际组名或异常文本。
- 分组仍保持用户组后广播组的既有顺序，并继续使用 `Context.ConnectionAborted`。普通
  异常与连接取消均原样传播；指标监听器故障被旁路，不改变连接或分组语义。
- RED 使用正常 ProjectReferences 构建 Unit 项目 **0 warning / 0 error**；Hub 原有
  **11/11** 通过，新增成功、失败和取消三条均因缺少 measurement 精确失败。最小实现后
  Hub 聚焦 **14/14**、完整 Realtime Unit **34/34**，Realtime SignalR BuildingBlock
  Release build **0 warning / 0 error**。
- 本切片使用任务快照 `realtime-hub-group-assignment-telemetry-20260730`，不修改
  `eng/testing/test-matrix.json`，也不覆盖 Jobs、Files、Notifications 或 CodeGeneration
  文件。共享 CodeGeneration 窗口在本实现落盘后串行执行组合 slice：tooling **39/39**、
  smoke **8/8**、Integration Release build **0 warning / 0 error**、组合真实栈
  **28/28**；TRX 复核为 Realtime **7/7**、Files **2/2**、CodeGeneration **19/19**。
  Testcontainers teardown 后运行与残留容器均为零，随后按队列将 Docker 交给 Jobs；
  本窗口未重复启动相同真实栈。生产 Collector、告警规则部署与多副本演练仍属于未验证项。

## Hub 授权拒绝连接终止增补（2026-07-30）

- 已通过 JWT 认证但缺少有效 `sub`、Full.NET scope 缺失或租户 scope/Claim 不自洽的主体，
  现在会在记录既有低基数拒绝结果后立即调用 `Context.Abort()`；这些连接仍不得加入用户、
  Host 或租户组，也不再以无分组连接持续占用 Hub 资源。
- 合法 Host 与租户主体继续加入原有用户组和广播组，且不会触发 `Abort()`；分组顺序、
  `Context.ConnectionAborted` 取消、发布器、Redis Backplane 与客户端协议均未改变。
- TDD 复用现有四条拒绝场景而不新增测试方法：Unit Release build
  **0 warning / 0 error**；RED 时 Hub **10/14**，四条均精确失败于未收到 `Abort()`；
  最小实现后 Hub **14/14**、完整 Realtime Unit **35/35**。
- 本切片使用任务快照 `realtime-hub-rejected-scope-abort-20260730`，不修改测试矩阵，
  不触碰 Jobs、Files、Notifications 或 CodeGeneration 文件。最终工作区 affected
  命中 Files + Realtime 并通过 **9/9**，Integration Release build
  **0 warning / 0 error**；Ryuk 自然退出后 running **0**、相关残留 **0**。

## Hub 安全 Claim 单值约束增补（2026-08-01）

- `sub`、`fullnet_scope` 与租户作用域下的 `fullnet_tenant_id` 现在都必须精确出现一次；
  Host 作用域继续要求 tenant Claim 零出现。重复的相同值或不同值均失败关闭，不再依赖
  `FindFirst` 顺序静默授权。
- 重复 subject 复用 `rejected_invalid_subject`，重复 scope/tenant 复用
  `rejected_scope_claim_mismatch`；低基数标签枚举、合法 Host/Tenant 分组与客户端协议不变。
- TDD 新增 Unit 方法 **1**、数据行 **3**：RED 时 Hub **17/20**，三类重复 Claim 都错误
  执行分组；最小实现后 Hub **20/20**、Realtime **52/52**，Unit Release build
  **0 warning / 0 error**。
- `realtime-hub-single-claim-20260801` slice 仅命中 Realtime；Integration Release build
  **0 warning / 0 error**、SQL Server/MySQL 聚焦 **7/7**。Integration 方法数不变，
  未修改共享测试矩阵。

## Hub 安全标识非空约束增补（2026-08-01）

- `sub` 与租户作用域下的 `fullnet_tenant_id` 除可解析为 UUID 外，还必须不是全零 UUID；
  空 subject 复用 `rejected_invalid_subject`，空 tenant 复用
  `rejected_scope_claim_mismatch`，并在任何分组前终止连接。
- 既有低基数遥测枚举、正常 Host/Tenant 分组、公共 API、客户端协议与数据库契约不变。
- TDD 新增 Unit 方法 **1**、数据行 **2**：RED 时 Hub **20/22**，两类空标识均错误
  执行分组；最小实现后 Hub **22/22**、Realtime **54/54**，Unit Release build
  **0 warning / 0 error**。
- `realtime-hub-nonempty-identifiers-20260801` slice 仅命中 Realtime；Integration Release
  build **0 warning / 0 error**、SQL Server/MySQL 聚焦 **7/7**。Integration 方法数不变，
  未修改共享测试矩阵。

## JWT 查询令牌精确路径增补（2026-07-30）

- `access_token` 查询令牌现在只在配置的 Hub 根路径及其精确 `/negotiate` 路径读取；
  SignalR WebSocket、SSE、Long Polling 与协商请求保持可用，同前缀下的普通 HTTP
  后代路径不再被 `StartsWithSegments` 扩大为 Bearer 认证入口。
- Identity 已配置的 `JwtBearerEvents`/`EventsType` 继续先执行；既有 Token 或认证结果
  继续优先，路径收口不绕过会话即时校验，也不改变自定义规范 HubPath。
- TDD 新增 Unit 方法 **1**：Release build **0 warning / 0 error**；RED **0/1**
  精确失败于普通后代路径错误取得查询令牌；最小实现后注册聚焦 **10/10**、完整
  Realtime Unit **36/36**。
- 本切片使用任务快照 `realtime-query-token-route-boundary-20260730`，不修改测试矩阵，
  不触碰 Jobs、Files、Notifications 或 CodeGeneration 文件。最终快照 slice 命中
  CodeGeneration + Files + Realtime，Release Integration build
  **0 warning / 0 error**、双 Provider 聚焦 **29/29**；Ryuk 自然退出后 Docker
  running **0**、SQL Server/MySQL/Ryuk/Testcontainers residual **0**。

## JWT 查询令牌单值约束增补（2026-07-31）

- Hub 根路径与精确 `/negotiate` 路径现在只接受一个非空 `access_token` 查询值；重复的
  相同值或不同值都在 JWT 解析前失败关闭，避免 `StringValues` 隐式拼接产生歧义 Token。
- Identity 既有事件链、Header Bearer、已有 Token/认证结果优先级、规范 HubPath 与正常
  SignalR 单值查询令牌均保持不变。
- TDD 新增 Unit 方法 **1**、数据行 **2**：RED 时注册聚焦 **16/18**，两条重复值分别被
  合并为逗号分隔字符串；最小实现后注册 **18/18**、Realtime **49/49**，Unit Release
  build **0 warning / 0 error**。
- `realtime-query-token-single-value-20260731` slice 仅命中 Realtime；Integration Release
  build **0 warning / 0 error**、SQL Server/MySQL 聚焦 **7/7**。Integration 方法数不变，
  未修改共享测试矩阵。

## Hub 活跃连接指标增补（2026-07-30）

- `fullnet.realtime.hub.connections.active` 使用无标签 `UpDownCounter`：只有授权通过、
  用户组与广播组均完成且 `OnConnectedAsync` 正常结束的连接记录 `+1`；对应
  `OnDisconnectedAsync` 在 finally 中记录 `-1`。
- 连接标记保存在 `HubCallerContext.Items`，可跨 SignalR 的瞬态 Hub 实例识别同一连接；
  Claim 拒绝、分组失败或从未计数的断开不会产生错误减量，也不导出连接 ID、用户或租户。
- TDD 新增 Unit 方法 **1**：Release build **0 warning / 0 error**；RED **0/1**
  精确失败于 active measurement 缺失；最小实现后 Hub **15/15**、Realtime
  **37/37**。fresh snapshot slice 仅命中 Realtime，Integration Release build
  **0 warning / 0 error**、双 Provider 聚焦 **7/7**；Ryuk 自然退出后 Docker
  running **0**、SQL Server/MySQL/Redis/Ryuk/Testcontainers residual **0**。

## Hub 连接存活时长指标增补（2026-07-31）

- `fullnet.realtime.hub.connection.duration` 使用 `ms` Histogram；计时从授权连接完成
  分组并正常结束 `OnConnectedAsync` 后开始，到对应 `OnDisconnectedAsync` 的
  finally 为止，结果只允许 `completed|failure`。
- 单调时间戳保存在 `HubCallerContext.Items` 的既有连接标记中，因此 SignalR 后续创建
  瞬态 Hub 实例时仍能读取同一连接起点；拒绝、分组失败或未计数连接不产生时长。
  指标仅含 `outcome`，不导出连接、用户、租户、异常类型或异常文本。
- TDD 新增 Unit 方法 **1**、数据行 **2**：RED **0/2** 均精确失败于 duration
  measurement 缺失；最小实现后精确测试 **2/2**、Hub **17/17**、Realtime
  **39/39**，Unit Release build **0 warning / 0 error**。
- v2 快照 slice 计划命中共享 `integration-matrix`、`integration-tooling` 与 Realtime；
  工具链 **39/39**、Integration Release build **0 warning / 0 error**。canonical
  执行在 Realtime 前因 fresh Integration full discovery **245** 与当时矩阵 **243**
  不一致而 fail-closed；该差额属于并行 CodeGeneration 045 后的最终统一矩阵收口，
  本切片不修改中间计数。使用选择器同一 Realtime 过滤器直跑双 Provider **7/7**；
  Testcontainers teardown 后 Docker running **0**，SQL Server/MySQL/Redis/Ryuk/
  Testcontainers residual **0**。

## Redis Channel Prefix 环境隔离增补（2026-07-31）

- `environmentName` 直接进入 `fullnet:{environment}:signalr:` 通道前缀，因此现在只接受
  ASCII 字母、数字和中间连字符，并要求首尾为字母或数字；空白、冒号、Unicode 与首尾连字符
  会在注册 Redis Backplane 前失败关闭，避免静默规范化造成跨环境通道碰撞。
- 既有 `Production`、`Testing` 与 `IntegrationTests` 继续映射为小写环境段；
  Redis 重连、连接字符串解析、Hub/JWT、消息协议和发布语义均未改变。
- TDD 新增 Unit 方法 **1**、数据行 **6**：首轮 RED 注册聚焦 **10/15**，五个非法字符形状
  均因未抛出 `ArgumentException` 而失败；首尾边界复核再以 **5/6** 精确证明尾随连字符
  仍会漏过。最小实现后注册聚焦 **16/16**、Realtime **45/45**，Unit Release build
  **0 warning / 0 error**。
- `realtime-alert-contract-20260731` slice 仅命中 Realtime；Integration Release build
  **0 warning / 0 error**、SQL Server/MySQL 聚焦 **7/7**。Testcontainers 与 Ryuk
  自然退出后 Docker running/residual **0**、共享 runner **0**。生产多副本编排、
  Collector/告警路由部署与 Redis Cluster/Sentinel 仍属于未验证项。

## Backplane 原生超时分类增补（2026-07-31）

- Redis 客户端或底层连接抛出的 `TimeoutException` 现在与本地两秒取消预算统一记录为
  `outcome=timeout`，并返回既有安全超时描述；普通异常仍记录 `failure`，调用方取消继续原样传播。
- 该变更只修正 ready 指标与健康描述的故障分类，不修改探针两秒总预算、连接/异步一秒预算、
  Redis 重试、运行连接或发布语义，也不会把端点、连接字符串或异常正文写入健康响应。
- TDD 新增 Unit 方法 **1**：RED 时 Backplane Telemetry **5/6**，原生超时精确误记为
  “健康检查失败”；最小分支修复后 Telemetry **6/6**、Realtime **46/46**，Unit Release build
  **0 warning / 0 error**。
- `realtime-backplane-native-timeout-20260731-v2` slice 仅命中 Realtime；Integration Release
  build **0 warning / 0 error**、SQL Server/MySQL 聚焦 **7/7**。旧快照未用于验证，确保 Jobs
  capacity 完整性变更属于任务基线而非本切片影响集。

## 发布原生超时分类增补（2026-07-31）

- SignalR 发送任务抛出的 `TimeoutException` 现在记录为 `outcome=timeout`；调用方取消仍为
  `canceled`，普通异常仍为 `failure`，结果枚举保持封闭且不包含端点、用户、组名或异常文本。
- 超时异常对象继续原样传播，由现有上游或 Outbox 决定重试；本切片不新增发布预算、重试、
  吞异常或交付确认语义。`success` 仍只表示服务端发送任务完成。
- TDD 新增 Unit 方法 **1**：RED 时发布器 **5/6**，原生超时精确误记为 `failure`；最小分支
  修复后发布器 **6/6**、Realtime **47/47**，Unit Release build **0 warning / 0 error**。
- `realtime-publish-native-timeout-20260731` slice 仅命中 Realtime；Integration Release build
  **0 warning / 0 error**、SQL Server/MySQL 聚焦 **7/7**。Integration 方法数不变，任务未
  修改共享测试矩阵；Testcontainers teardown 后再按运行与残留容器为空正式释放 shared。

## Notifications Outbox 修复增补（2026-07-30）

- `AddFullNetRealtimePublisher` 为 Worker 提供不映射 Hub、不注册 JWT Bearer 适配器的发布端能力。
- Worker 发布端启用时必须存在 `Realtime:RedisBackplaneConnectionString` 或 `ConnectionStrings:redis`；缺失时启动期失败关闭，显式关闭 Realtime 时使用 `NullRealtimePublisher`。
- Notifications 公告、站内信送达和已读状态已经使用三个 v1 事务 Outbox 事件修复即时推送；发布异常向现有 Outbox 重试/死信路径传播。
- 注册与失败关闭 Unit 已覆盖 Worker-only 发布端、缺少 Backplane 失败关闭，以及 SignalR 查询令牌与 Identity 类型化 JWT 事件共存；Notifications Handler/注册、SQL Server/MySQL API 与真实 Outbox 载荷均保持通过。
- SQL Server/MySQL Worker 实时修复 Integration **2/2**：写入 API 关闭直接推送，独立 Worker 使用 Worker Profile 领取 Outbox，经 Redis Backplane 向另一 API 节点的已鉴权 SignalR 客户端发布站内信和当前未读数，并在发布成功后确认消息。
- SQL Server/MySQL 浏览器真实栈分别通过 Vue/Layui 两个项目：Playwright 先确认 Hub 握手消息，再显式断开恢复端的真实 WebSocket；独立 Worker 在离线窗口完成 Outbox/Redis 发布，在线观察端立即收到更新，恢复端重新连接后通过 HTTP 补拉恢复未读数。
- 真实栈同时修复并锁定两项启动边界：Worker 必须接收专用 Realtime Redis Backplane 配置；JWT Bearer 使用 `EventsType` 时，SignalR 查询令牌适配仍须转发 Identity 的会话校验事件。
- 生产多副本编排与告警仍未完成，因此状态保持 `Build-verified`。

## 管理端客户端增补（2026-07-27）

- 实施计划：[Notifications Realtime Admin Client](../superpowers/plans/2026-07-27-notifications-realtime-admin-client.md)
- 依赖：`@microsoft/signalr` **10.0.0**，MIT，已登记 `THIRD-PARTY-NOTICES`
- RED：共享连接器、Vue 状态、Layui 状态与动态未读徽标均先因能力缺失失败；Vue 全量随后发现快照订阅会创建身份控制器并覆盖 Pinia 状态，`App.test.ts` **3/3** 稳定复现
- GREEN：共享契约 **72/72**、Vue **197/197**、Layui **95/95**；Mock parity **99/99**（按矩阵跳过 **5**）；Vue/Layui/共享包生产构建通过
- Mock 边界：纯 Mock web server 通过 `VITE_REALTIME_ENABLED=false` 显式关闭 Hub 和初始未读查询；真实开发与真实栈默认启用，不以 404 探测能力
- 当时状态保持 `Build-verified`：该管理端基础切片只有 Mock/单元/构建证据；后续
  2026-07-30 双库真实浏览器断网恢复 E2E 已在 Notifications Outbox 修复增补中登记。

## 管理端首次连接恢复增补（2026-07-27）

- RED：首次 `start()` 失败后不会创建第二个连接，也不存在可由租户切换、匿名化或销毁取消的重试计时器，聚焦 **3/6** 失败
- GREEN：首次失败使用与 SignalR 自动重连一致的 **0/2s/10s/30s** 退避并在上限保持 30 秒；计时器绑定 `sessionId + tenantId`，旧上下文不会在切换后恢复连接
- 验证：`@fullnet/client-contracts` **75/75**、Vue 聚合 **200/200**、Layui **95/95**，TypeScript 构建通过；Vue/Layui 适配器继续消费同一共享控制器，无需双端复制重试状态机
- 状态保持 `Build-verified`：单元测试锁定调度和取消语义；后续 2026-07-30 已由独立
  Worker、Redis Backplane、在线观察端和离线恢复端完成双库真实浏览器断网/恢复 E2E。
