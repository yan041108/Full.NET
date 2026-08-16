# Full.NET 本地开发与运行指南

新加入的开发者请先阅读[人类阅读入口（Onboarding）](onboarding.md)，再按本文搭建环境。当前能力和未完成边界以[能力状态矩阵](../roadmap/capability-status.md)为准；AI 开发代理还必须遵守根目录 [`AGENTS.md`](../../AGENTS.md) 和 [`rules/`](../../rules/README.md)。

## 1. 前置环境

- .NET 10 SDK；
- Node.js 24 和 pnpm 10.26.0，建议通过 Corepack 管理；
- Git；
- Docker Desktop，或兼容 Docker Engine。Windows 应启用 WSL 2，并使用 Linux containers。

先确认 Docker Client、Server 和容器运行均正常：

```powershell
docker version
docker run --rm hello-world
```

不要把真实口令、连接串、Token、证书或私钥提交到 `appsettings*.json`。本地使用环境变量或 .NET User Secrets；部署环境使用平台 Secret 管理能力。

## 2. 还原、构建与后端测试

```powershell
dotnet restore Full.NET.slnx
dotnet build Full.NET.slnx --configuration Release
pnpm test:dotnet:unit -- --no-build
pnpm test:dotnet:compatibility -- --no-build
pnpm test:dotnet:architecture -- --no-build
```

测试套件、最低发现数、超时与 Integration 分片只维护在 [`eng/testing/test-matrix.json`](../../eng/testing/test-matrix.json)。本文不复制测试数量，避免门槛随代码增长后漂移。

### 2.1 受影响 Integration 测试

工作区干净、单窗口开发时，先记录任务基线：

```powershell
$taskBase = git rev-parse HEAD
pnpm test:inner -- --base $taskBase --plan
pnpm test:slice -- --base $taskBase
```

工作区已经有其他改动或任务跨多个窗口时，使用任务快照：

```powershell
pnpm test:task:start -- <task-id>
pnpm test:inner -- --snapshot <task-id> --plan
pnpm test:slice -- --snapshot <task-id>
pnpm test:integration:affected -- --snapshot <task-id> --phase merge
```

常用专项入口：

```powershell
pnpm test:integration:smoke
pnpm test:integration:api:sqlserver
pnpm test:integration:api:mysql
pnpm test:integration:migrations
pnpm test:integration:infrastructure
pnpm test:integration:partitions
pnpm test:integration:durations
```

Integration 依赖按首次使用启动。单 Provider 聚焦测试不会无条件启动另一数据库和 Redis；数据库行为变更必须让受影响选择器命中 SQL Server 与 MySQL。完整 Integration 集合只由 `main` CI 的互斥分片执行，本地任务禁止以完整集合替代受影响验证。

测试结束后等待数据库容器和 Ryuk 自然退出，并检查没有遗留的 SQL Server、MySQL、Testcontainers 或任务 Runner。环境缺失、发现数为零、命令未执行都不能表述为测试通过。

## 3. 客户端工作区

首次还原客户端依赖：

```powershell
corepack enable
pnpm install --frozen-lockfile
```

标准客户端门禁：

```powershell
pnpm test:naming
pnpm test:sql-safety
pnpm test:openapi
pnpm test:openapi:breaking -- --base-ref main
pnpm test:workspace
pnpm test:clients
pnpm test:performance-governance
pnpm build:clients
pnpm test:bundle-budgets
pnpm test:e2e
pnpm test:e2e:uniapp
```

### 3.1 Vue 管理端

`ui/admin` 是后台产品的唯一持续交付线。`ui/admin-layui` 已冻结，不再新增页面、按钮、适配器或功能对等实现；只有明确授权的安全修复、迁移或退役任务可以修改。

直接连接本地 API 时：

```powershell
$env:VITE_API_BASE_URL = "http://localhost:5149"
pnpm --filter @fullnet/admin dev
```

涉及 Refresh Cookie 时，浏览器与 API 应使用同一个 `localhost` 站点语义，不要混用 `localhost` 和 `127.0.0.1`，否则 `SameSite=Strict` Cookie 可能被浏览器按跨站请求拒绝。

Vue 使用 `/api/v1`、标准 HTTP 状态码和 ProblemDetails，并从 `@fullnet/client-contracts` 消费共享契约。Access Token、租户和权限快照只保存在内存，页面刷新通过 Refresh Cookie 恢复，禁止写入 `localStorage` 或 `sessionStorage`。

无权限时 Vue 不创建对应页面或操作按钮，但前端隐藏只改善体验。所有受保护 Endpoint 仍必须使用同一稳定权限码重新授权，绕过前端直接调用必须返回 403。

### 3.2 真实后端浏览器测试

`pnpm test:e2e:real` **不是** inner 门禁，只用于功能 `Verified` 关闭，或修复真实 CORS、Cookie、CSRF、Session 与跨 Origin 凭据问题。日常代码迭代使用 `pnpm test:inner`。

真实栈 E2E 需要 Docker、.NET 10 SDK 和可用的本地端口：

```powershell
pnpm test:e2e:real
```

套件会启动数据库、Migrator、真实 API 和 Vue，并验证 Cookie、CSRF、CORS、登录、刷新、租户切换、精确页面/操作权限、直接 API 403、退出和 ProblemDetails。真实栈测试禁止用 `page.route` Mock 替代后端行为。

已有独立栈时可以跳过自动引导：

```powershell
$env:FULLNET_E2E_SKIP_BOOTSTRAP = "1"
$env:FULLNET_E2E_API_URL = "http://localhost:5149"
pnpm test:e2e:real
```

### 3.3 uni-app

uni-app 基础验证和三目标构建：

```powershell
pnpm --filter @fullnet/uniapp dev:h5
pnpm --filter @fullnet/uniapp test
pnpm --filter @fullnet/uniapp typecheck
pnpm --filter @fullnet/uniapp build:h5
pnpm --filter @fullnet/uniapp build:mp-weixin
pnpm --filter @fullnet/uniapp build:mp-alipay
pnpm test:e2e:uniapp
```

业务、API 和账号资料使用规范 BCP 47 标签 `zh-CN`、`en-US`。微信与支付宝构建完成不代表对应平台已验证；仍须在各自开发者工具中执行启动、语言切换、重启保持、真实登录、API Header、错误、会话失效和导航标题检查。缺少工具时只能记录 `Not executed — required tool not installed`。

## 4. 使用 Aspire 启动完整开发环境

可将开发 Bootstrap 账号写入 AppHost 的 User Secrets：

```powershell
dotnet user-secrets set "Parameters:identity-bootstrap-username" "admin" --project src/Hosts/Full.NET.AppHost/Full.NET.AppHost.csproj
dotnet user-secrets set "Parameters:identity-bootstrap-password" "<至少12位且含大小写、数字和特殊字符>" --project src/Hosts/Full.NET.AppHost/Full.NET.AppHost.csproj
dotnet run --project src/Hosts/Full.NET.AppHost/Full.NET.AppHost.csproj
```

也可以在 Aspire Dashboard 的未解析参数提示中输入并保存到 User Secrets。密码是 Secret Parameter，不应出现在命令日志、普通配置或提交历史中。

正常启动顺序：

1. 数据库和 Redis 就绪；
2. Migrator 执行 DbUp 迁移和 Development Seed，然后以代码 0 退出；
3. API 和 Worker 在 Migrator 成功后启动；
4. API 的健康端点反映实际运行状态。

AppHost 使用 Development Seed，幂等创建标识为 `local`、域名为 `localhost` 的开发租户和首个宿主管理员；账号已经存在时不会覆盖密码。

健康端点语义：

- `/health/live`：只证明进程能够响应，不检查数据库、Redis 或 Schema；
- `/health/ready`：检查当前数据库和已配置的 Redis/Backplane 等必要依赖；
- `/health/startup`：验证当前数据库已完成必须的 Schema Contract 和初始化阶段。

`ready` 或 `startup` 缺少真实检查时不得返回供编排器使用的假成功。

AppHost 默认使用 SQL Server。只对当前终端切换为 MySQL：

```powershell
$env:UseMySql = "true"
dotnet run --project src/Hosts/Full.NET.AppHost/Full.NET.AppHost.csproj
```

也可以在 `src/Hosts/Full.NET.AppHost/appsettings.json` 中设置：

```json
{
  "UseMySql": true
}
```

本地 AppHost 可以让 Cache 与 Realtime 共用 Redis；成熟生产参考拓扑必须按部署规则使用独立 Cache/Backplane Redis 与 Realtime Redis，例外需要容量和故障域证据。

## 5. 迁移、Seed 与部署顺序

生产部署顺序固定为：

1. 运行 `Full.NET.Host.Migrator`；
2. 确认迁移、必要 Baseline Seed 和 Schema Contract 成功；
3. 启动或滚动发布 API、Worker；
4. 发布 Vue 静态资源。

API 和 Worker 不在启动时执行生产迁移，也不运行 Seed。基础配置示例：

```text
Database__Provider=SqlServer        # 或 MySql
Database__ConnectionName=fullnet
Database__MySqlGuidStorageMode=Binary16
ConnectionStrings__fullnet=<由 Secret 管理器注入>
```

Migrator 默认只迁移。需要播种时显式使用：

```powershell
dotnet run --project src/Hosts/Full.NET.Host.Migrator/Full.NET.Host.Migrator.csproj -- --seed baseline
```

支持的 Profile 为 `baseline|development|demo|test`。Production 只允许 Baseline；Development、Demo、Test 作为环境 Overlay 确定性继承 Baseline。生产 Bootstrap Secret 和失败处理见[生产 Seed 运维说明](../operations/seed-production-baseline.md)。

MySQL 正式模式是 RFC 9562 大端字节序 `BINARY(16)`。业务模块和 Dapper 调用只处理 C# `Guid`，不得自行交换 UUID 字节。008/009 迁移已经完成代码与双库恢复验证；生产维护窗口、备份恢复和 RPO/RTO 演练仍按 [UUID Binary16 迁移 Runbook](uuid-binary-migration-runbook.md)执行。

## 6. Identity、可信代理与会话

浏览器会话的主要端点：

| 方法 | 路径 | 说明 |
|---|---|---|
| `POST` | `/api/v1/auth/login` | 校验精确 Origin、限流和账号状态，返回短期 Access Token |
| `POST` | `/api/v1/auth/refresh` | 校验 Refresh Cookie 与 `X-CSRF-Token`，原子轮换会话 |
| `POST` | `/api/v1/auth/logout` | 撤销会话并清除 Cookie |
| `GET` | `/api/v1/me` | 返回当前账号、有效作用域、语言偏好和资料版本 |
| `PUT` | `/api/v1/me/locale` | 以乐观并发更新账号语言偏好 |
| `GET` | `/api/v1/navigation` | 返回当前有效作用域的授权导航树 |
| `GET` | `/api/v1/tenancy/available` | 返回当前账号可进入的活动租户 |
| `PUT` | `/api/v1/tenancy/context` | 进入租户或返回 Host，并签发新 Access Token |

Refresh Token 只存在于 `Secure`、`HttpOnly`、`SameSite=Strict` Cookie，数据库只保存哈希。前端可读 CSRF Cookie 仅用于双提交校验。调用方不得信任客户端自定义租户 Header、权限快照或未签名 Claim。

Production 必须从 Secret 管理器提供 RSA 密钥和允许的管理端 Origin，例如：

```text
Identity__ActiveKeyId=<当前密钥标识>
Identity__SigningKeys__<当前密钥标识>__PublicKeyPem=<RSA 公钥 PEM>
Identity__SigningKeys__<当前密钥标识>__PrivateKeyPem=<RSA 私钥 PEM>
Identity__AllowedOrigins__0=https://admin.example.com
Tenancy__HostDomains__0=api.example.com
```

`Tenancy:HostDomains` 只接受精确主机名或 IP，不接受协议、端口、路径、通配符或全网范围。非法配置在宿主启动时失败。

API 默认不信任转发 Header。只有实际部署在可信代理之后时才启用 `TrustedProxy`，并登记最小代理 IP/CIDR 和精确 `ForwardLimit`；禁止 `0.0.0.0/0`、`::/0` 或公网客户端网段。业务模块统一读取规范化后的 `Connection.RemoteIpAddress`，不得自行解析 `X-Forwarded-For`。

## 7. 缓存与实时通信

FusionCache 是唯一缓存实现，通过 `.AsHybridCache()` 同时暴露 FusionCache 与 Microsoft `HybridCache` 抽象。业务条目必须通过统一策略注册表获得 C0/S0-L2/S1/S2/N0 分类和选项，禁止在模块中随意手写 TTL。

缓存失效不使用 Outbox。事务提交后当前实例直接清理 L1/L2，Redis Backplane 快速通知其他实例，TTL、版本和权威源读取负责最终收敛。强一致类别禁用 L1 或直接读取权威源；Redis Pub/Sub 不是可靠事务总线。

未配置 Redis 时可以作为单实例开发模式运行。多实例部署必须配置共享 L2 与 Backplane，并让 `/health/ready` 反映 Redis 可用性。缓存键与 Tag 统一通过 `CacheKeyBuilder` 构造，不在日志或指标中附加完整缓存键、租户名、域名或异常文本。

业务模块通过 `IRealtimePublisher` 发布实时通知，不直接依赖 SignalR Hub。多 API 节点可以设置 `Realtime:RedisBackplaneConnectionString`；配置后 `/health/ready` 增加 Realtime Backplane 检查。实时推送是尽力下行，客户端必须刷新权威 HTTP 状态，可靠业务传播仍使用事务 Outbox。故障演练见 [Realtime Redis Backplane 运维说明](../operations/realtime-redis-backplane.md)。

## 8. HTTP、JSON 与兼容层

默认 API 使用真实 HTTP 状态码。成功直接返回强类型 DTO；失败返回 RFC ProblemDetails，并携带稳定 `code`、`traceId` 和可选结构化校验集合。调用方依据机器码处理逻辑，不比较中文或英文错误文本。

HTTP JSON 使用 System.Text.Json。每个模块维护自己的 `JsonSerializerContext`，并由模块注册入口加入 Host 的 `TypeInfoResolverChain`。生产 Endpoint 的请求、响应、分页项和 ProblemDetails 类型受 Architecture 门禁覆盖；不要在请求热路径创建新的 `JsonSerializerOptions`，也不要把 Newtonsoft.Json 作为核心 API 默认实现。

需要兼容旧 Admin.NET 响应形状时，在自定义 Host 中显式启用：

```csharp
builder.AddFullNetServiceDefaults();
builder.Services.AddAdminNetCompatibility();
```

兼容层只改变普通 JSON 响应形状，仍保留真实 HTTP 状态码；文件、流、SSE、SignalR、健康检查和 `204 No Content` 不进入统一包络。

## 9. 事务、跨模块通信与 Outbox

模块内查询可以对本模块表使用参数化 `JOIN/LEFT JOIN`、批量查询和多结果集；模块内写入由一个 `ICommandTransaction` 原子维护本模块业务表、必要审计和 Outbox。

跨模块禁止直接读写、JOIN 或外键关联对方表。立即权威读取使用消费方最小 Contract Port；高频读取使用所有者版本化事件和消费方本地投影；跨模块写入使用各模块本地事务、Outbox、幂等、补偿和对账。详细标准见 [`ADR-0002`](../architecture/adr/ADR-0002-modular-monolith-evolution.md#模块内模块间数据关联与事务标准)。

事务内禁止等待 HTTP、gRPC、Broker、Redis、对象存储或文件副作用。写入后可能返回失败 `Result` 的路径使用 `ICommandTransaction.ExecuteResultAsync`，避免失败结果仍提交。

可靠 Integration Event 以 MessagePack 二进制写入 `fn_outbox_message`，同时保存 `MessageId`、`MessageType`、`SchemaVersion`、`ContentType`、Tenant、Trace 和发生时间。Worker 使用数据库租约至少一次处理，按 `MessageType + SchemaVersion` 精确匹配 Handler；重复处理必须幂等，坏载荷、未知版本或超过尝试上限进入可查询死信。

禁止为 Outbox 增加 JSON fallback、Typeless 或 Contractless MessagePack Resolver。版本发布采用 consumer-first、producer-second、最后退役旧消费者，具体见 [Outbox Worker 运维说明](../operations/outbox-worker-topology.md)。

## 10. 日志、指标与基准

业务代码使用 `ILogger<T>`，高频路径使用 `LoggerMessage` 源生成。普通和高优先级日志使用独立有界异步通道，共享一个退出预算；日志队列不是审计账本。不得记录请求体、Cookie、Authorization、连接串、私钥或消息 payload。故障处置见[日志降级运维说明](../operations/logging-degraded-mode.md)。

指标只使用低基数标签，禁止附加用户、租户、域名、完整路径、缓存键、消息 ID 或异常文本。容量结果在生产等价环境认证前统一标记 `Capacity-not-verified`。

审计查询基准：

```powershell
dotnet run --project benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj -c Release -- audit-query
dotnet run --project benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj -c Release -- audit-query --mode sqlserver-plan-ab --providers sqlserver
dotnet run --project benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj -c Release -- audit-query --mode cursor-ab
```

生产等价混合负载入口：

```powershell
dotnet run --project benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj -c Release -- mixed-load
```

基准工件写入 `BenchmarkDotNet.Artifacts/`，不提交仓库。本地结果只用于回归和决策输入，不能直接宣传为生产 SLA 或容量证明。

## 11. 通信技术边界

| 场景 | 当前选择 | 引入或升级条件 |
|---|---|---|
| 同进程模块通信 | 强类型 Contract Port、Command/Query | 默认模式，不制造网络边界 |
| 跨进程同步服务 | gRPC + Protobuf | 出现真实独立部署、SLA 或容量边界后 |
| 可靠异步事件 | MessagePack Outbox | 需要本模块状态与事件原子提交时 |
| 浏览器实时通信 | SignalR，按需使用 MessagePack | 尽力通知，不代替可靠业务事件 |
| AI / Agent / MCP | 供应商中立抽象和可替换 Provider | 独立业务规格、数据边界和审计获批后 |

不要为了“未来可能微服务化”提前把模块内调用改成 HTTP 或 gRPC。先保持数据所有权、契约、投影、幂等和对账边界稳定，再依据可测量的独立伸缩、故障隔离或发布需求拆分。
