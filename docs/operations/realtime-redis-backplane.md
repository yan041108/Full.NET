# Realtime Redis Backplane 故障与恢复

本文说明 SignalR 多 API 节点使用 Redis Backplane 时的就绪信号、故障边界和恢复步骤。
它不把即时消息提升为可靠业务事件，也不替代业务查询或事务 Outbox。

## 配置与信号

优先使用独立配置，避免把 Realtime 的发布拓扑与缓存连接生命周期隐式绑定：

```json
{
  "Cache": {
    "RedisConnectionString": "cache-redis:6379"
  },
  "Realtime": {
    "Enabled": true,
    "HubPath": "/hubs/notifications",
    "RedisBackplaneConnectionString": "realtime-redis:6379",
    "AllowSharedRedisInDevelopment": false,
    "TransportMode": "Default",
    "SkipNegotiation": false,
    "RequireSessionAffinity": true
  }
}
```

Production/Staging 必须显式配置 `Realtime:RedisBackplaneConnectionString`，且不得与
`Cache:RedisConnectionString` 相同。Development/Testing 仅在
`Realtime:AllowSharedRedisInDevelopment=true` 时可回退 `ConnectionStrings:redis` 或与 Cache
共用。`TransportMode=Default` 要求 Ingress 会话亲和；只有
`WebSocketsOnly + SkipNegotiation=true` 才允许 `RequireSessionAffinity=false`。
生产连接由平台统一设置 `AbortOnConnectFail=false`，并使用
`fullnet:{environment}:signalr:` Channel Prefix；短暂中断后 API 节点会继续尝试重连。
ready 探针对单次 Redis 抖动返回 Degraded（HTTP 200），连续失败才 Unhealthy（503）。
环境名会直接成为 Channel Prefix 的单一命名段，只允许 ASCII 字母、数字和中间连字符，
且必须以字母或数字开头、结尾；空白、冒号、斜杠、Unicode 或首尾连字符会在服务注册阶段
失败关闭。API 与 Worker 必须使用完全相同的规范环境名，禁止依赖修剪或字符替换把两个原始值
静默折叠到同一 Redis 通道。
`Realtime:HubPath` 必须使用非根绝对路径，且不得包含尾随 `/`、重复 `//`、空白、
查询字符串或片段；无效配置在服务注册阶段失败关闭，避免根路由占用或非规范
negotiate 路径进入运行期。
浏览器查询令牌只在该 Hub 根路径及其精确 `/negotiate` 路径读取，并且
`access_token` 必须只有一个非空查询值；重复值不会隐式合并为 Bearer Token。同前缀下的
其它 HTTP 后代路径也会忽略该参数，避免未来新增 Endpoint 时意外获得 Bearer 身份。

Outbox Worker 只注册发布端，不承载 Hub 连接。其 Realtime 启用时必须配置上述任一
Redis 连接；缺失连接会在启动期失败关闭，避免向 Worker 本地空 Hub 发布后误记
Outbox 修复成功。不需要实时修复的部署必须显式设置 `Realtime:Enabled=false`。

Hub 分组使用认证主体中的 `sub`、`fullnet_scope` 与可选 `fullnet_tenant_id`：

- 三类安全 Claim 各自必须保持单值；重复的相同值或不同值都失败关闭，不按出现顺序取第一项。
- 用户私有组要求 `sub` 是有效且非全零的 UUID，且下述 Host/租户作用域之一完整成立。
- Host 广播组要求 `fullnet_scope` 精确为 `host`，且不存在租户 Claim。
- 租户广播组要求非全零的 `fullnet_tenant_id`，且
  `fullnet_scope=tenant:{TenantId:N}` 与其精确一致。
- 缺失、畸形或相互矛盾的 Claim 会立即终止 Hub 连接，且不得加入用户、Host 或任意
  租户组；不得保留已认证但没有自洽 Full.NET 作用域的空转连接。

- `/health/live`：只表示 API 进程存活，不检查 Backplane。
- `/health/startup`：只检查 Schema Contract，不检查 Backplane。
- `/health/ready`：配置 Backplane 后包含 `realtime-backplane`；Redis 不可达时返回
  503，恢复后自动回到 200。

健康检查使用独立短连接、两秒总预算、无重试 `PING`，不会输出连接字符串、异常堆栈
或 Redis 内部类型。

## 指标与告警基线

Realtime 启用后，API 与发布端宿主会注册 `fullnet.realtime` Meter；未配置 Redis 的
单节点模式也会导出发布指标。ready 探针只在配置 Backplane 时记录：

| Instrument | 类型/单位 | 标签 | 语义 |
|---|---|---|---|
| `fullnet.realtime.backplane.readiness.state` | Gauge / `{state}` | 无 | 本次探针成功为 `1`，超时或失败为 `0` |
| `fullnet.realtime.backplane.readiness.checks` | Counter / `{check}` | `outcome` | 按 `healthy`、`timeout`、`failure` 累计探针结果 |
| `fullnet.realtime.backplane.readiness.duration` | Histogram / `ms` | `outcome` | 按同一结果枚举记录探针端到端耗时 |
| `fullnet.realtime.publish.attempts` | Counter / `{attempt}` | `target`、`outcome` | 按 `user|group` 与 `success|timeout|failure|canceled` 累计发布结果 |
| `fullnet.realtime.publish.duration` | Histogram / `ms` | `target`、`outcome` | 按同一目标与结果枚举记录 SignalR 服务端发送耗时 |
| `fullnet.realtime.hub.authorization.decisions` | Counter / `{decision}` | `outcome` | 按 `authorized_host`、`authorized_tenant`、`rejected_invalid_subject`、`rejected_scope_claim_mismatch` 累积分组授权决策 |
| `fullnet.realtime.hub.connections.active` | UpDownCounter / `{connection}` | 无 | 授权且完成分组的连接建立记 `+1`，对应连接断开记 `-1` |
| `fullnet.realtime.hub.connection.duration` | Histogram / `ms` | `outcome` | 已计数授权连接从建立完成到断开回调的存活时长，结果仅为 `completed|failure` |
| `fullnet.realtime.hub.group.assignments` | Counter / `{assignment}` | `target`、`outcome` | 按 `user|broadcast` 与 `success|failure|canceled` 累计实际分组结果 |
| `fullnet.realtime.hub.group.assignment.duration` | Histogram / `ms` | `target`、`outcome` | 按同一目标与结果枚举记录单次 `AddToGroupAsync` 耗时 |

标签值来自封闭枚举，不包含实例地址、连接字符串、环境、租户、用户、组名、消息机器码
或异常文本。调用方取消不计为发布/分组失败或 Backplane 故障；指标导出器或监听器失败
也不会改变 ready、发布或分组结果。

生产初始告警按实际 ready 调用周期换算，且必须逐实例计算：

1. 任一实例连续两个 ready 周期 `state=0` 时告警并停止向该实例继续分配新流量；不得用
   全体副本平均值掩盖单实例持续失败。
2. 15 分钟窗口至少有 20 次探针时，非 `healthy` 比例超过 1% 触发预警；5 分钟窗口超过
   20% 触发升级。上线后保留至少七天基线，再根据已记录的变更窗口和误报调整阈值。
3. `timeout` 同时覆盖本地两秒探针预算与 Redis 客户端原生超时，优先检查 Redis 饱和、
   连接建立延迟和网络丢包；`failure` 优先检查端点、
   TLS、认证、协议和配置格式。两者不得合并为不可定位的通用异常标签。
4. `duration` 的 P95/P99 接近两秒总预算但尚未失败时属于提前容量信号；不得只等
   `/health/ready=503` 后再处置。
5. 发布窗口至少有 20 次尝试时，15 分钟 `timeout` 或 `failure` 各自比例超过 1% 触发
   预警；5 分钟超过 10% 触发升级。必须分别观察结果与 `user|group`，避免 Redis/网络
   饱和超时被协议、配置等普通失败混淆，或广播故障被用户单播量稀释。
6. 非部署/停机窗口内，5 分钟 `canceled` 比例超过 5% 时检查上游请求取消、Worker
   关闭时序和 Outbox 租约；计划内优雅关闭应作为变更注释，而不是归类为 Redis 失败。
7. `publish.duration` P99 连续十分钟超过同实例、同时段七天基线两倍时触发预警，同时
   对照 ready 耗时、Redis 饱和和网络指标；没有相同环境证据时不得据此宣称吞吐退化。
8. Hub 分组窗口至少有 20 次操作时，15 分钟 `failure` 比例超过 1% 触发预警；5 分钟超过
   10% 触发升级。必须分别观察 `user` 与 `broadcast`，并与授权拒绝、Redis ready 和发布
   失败对照，避免把 Claim 合同错误与实际组操作故障混为一类。
9. 非部署/停机窗口内，5 分钟分组 `canceled` 比例超过 5%，或
   `hub.group.assignment.duration` P99 连续十分钟超过同实例、同时段七天基线两倍时，
   检查客户端断连、宿主关闭、Redis 延迟和 SignalR 连接生命周期。
10. `hub.connections.active` 必须逐实例观察；接近宿主连接容量、连接持续单调增长且业务
    在线量未同步增长，或发布/分组吞吐归零但连接数不回落时，检查客户端重连风暴、
    代理空闲超时与断开回调。部署滚动和实例重启会自然重置该进程级值。
11. `hub.connection.duration` 至少累计 20 次断开后再评估分位数。P50/P95 同时明显缩短且
    active 值反复升降时，优先检查客户端重连策略、代理空闲超时与网络抖动；
    `outcome=failure` 持续出现时再对照服务端异常日志。该结果只描述 Hub 断开回调，
    不证明浏览器已处理消息，也不得把异常类型或连接标识加入标签。

上述门槛是平台无关的最小基线。Prometheus、OpenTelemetry Collector 和告警路由中的
具体查询、静默、升级与值班所有权必须由部署仓库落地并演练。

Hub 授权指标在尝试加入 SignalR 组之前记录，因此 `authorized_host` 或
`authorized_tenant` 只表示 Claim 合同通过。后续用户组和广播组操作由独立分组指标记录；
其中成功也只表示服务端组操作完成，不表示客户端仍在线或已收到消息。指标不包含用户、
租户、连接、实际组名或 Claim 原值。生产环境应分别观察两类拒绝结果：连续三个采集周期
出现拒绝，或五分钟内至少有 20 次授权决策且拒绝比例超过 1% 时触发 Warning，优先核对
认证方案、Token 颁发与 `fullnet_scope`/`fullnet_tenant_id` 合同，不得把拒绝主体降级加入
Host 组或保留连接来消除告警。

## 故障语义

Redis 中断时，同一 API 节点内的连接仍可能保持在线，但跨节点组播不可用。发布调用
可能快速抛出 Redis 异常，也可能只表现为远端未收到；调用返回不得作为最终交付证明。
`outcome=success` 只表示 SignalR 服务端发送任务完成，不表示浏览器已经收到或处理消息。

Realtime 消息是尽力即时下行：

- 客户端重连或收到异常后必须重新查询权威 HTTP API。
- 账号、权限、通知持久化等业务状态以数据库为准。
- 需要可靠传播的业务事件必须写入事务 Outbox，由消费者幂等处理。
- 不得通过无限重试 Realtime 发布来模拟可靠消息队列。

## 故障处置

1. 确认 `/health/live=200` 且 `/health/ready=503`，排除 API 进程故障。
2. 检查 `realtime-backplane` 健康项和 Redis 服务端连通性；不要在工单或日志中复制
   含密码的完整连接字符串。
3. 检查 API 与 Worker 的 Redis 端点、TLS、认证、网络策略和环境级 Channel Prefix
   是否一致。
4. 恢复 Redis 后持续观察所有 API 副本的 `/health/ready`，应自动收敛为 200；
   正常情况无需重启 API。
5. 从一个 API 节点建立已认证 Hub 连接，在另一 API 节点和 Worker 发布测试机器码，
   确认跨节点收到；测试数据不得触发真实业务副作用。
6. 若 Redis 已可达但个别节点持续 503 或无法跨节点投递，先保留该节点连接失败日志和
   指标，再滚动替换异常副本；不要同时重启全部副本。

## 自动化证据边界

SQL Server/MySQL 集成测试使用两个独立 API 宿主、专用 Redis 8.6 容器和真实 SignalR
Long Polling 客户端，覆盖故障前跨节点投递、Redis stop/start、双节点 ready
503→200 和无宿主重启恢复。该证据支持 `Build-verified`，但不代表生产编排器、网络抖动、
Redis Cluster/Sentinel、TLS 或告警路由已经完成验收。低基数 ready 指标与告警基线
已由 Unit 和 Release build 锁定，但生产 Collector、规则部署与通知升级仍需部署环境证据。
