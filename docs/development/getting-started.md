# Full.NET 本地开发与运行指南

## 1. 前置环境

安装 .NET 10 SDK、Git 和 Docker Desktop。Windows 环境应启用 WSL 2，并让 Docker Desktop 使用 Linux containers。先确认 Docker Engine 可用：

```powershell
docker version
docker run --rm hello-world
```

不要把真实口令、连接串或访问令牌提交到 `appsettings*.json`。本地覆盖请使用环境变量或 .NET User Secrets，部署环境使用平台的 Secret 管理能力。

## 2. 构建与测试

```powershell
dotnet restore Full.NET.slnx
dotnet build Full.NET.slnx --configuration Release
dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --minimum-expected-tests 48
dotnet tests/Full.NET.CompatibilityTests/bin/Release/net10.0/Full.NET.CompatibilityTests.dll --minimum-expected-tests 4
dotnet tests/Full.NET.ArchitectureTests/bin/Release/net10.0/Full.NET.ArchitectureTests.dll --minimum-expected-tests 7
dotnet tests/Full.NET.IntegrationTests/bin/Release/net10.0/Full.NET.IntegrationTests.dll --minimum-expected-tests 6 --timeout 10m
```

集成测试会通过 Testcontainers 启动真实 SQL Server 和 MySQL，因此 Docker 必须保持运行。CI 不跳过任何数据库测试。

测试项目使用 Microsoft.Testing.Platform 的可执行测试宿主。先完成 Release 构建，再直接运行生成的测试 DLL；`--minimum-expected-tests` 可以防止测试发现异常被误判为成功。

### 2.1 客户端工作区与双管理端

客户端要求 Node.js 24 和 pnpm 10.26.0。首次进入仓库先启用 Corepack，再使用锁文件还原：

```powershell
corepack enable
pnpm install --frozen-lockfile
pnpm test:workspace
pnpm test:clients
pnpm build:clients
pnpm test:e2e
```

`pnpm test:clients` 运行共享契约、Vue 和 Layui 单元测试；`pnpm test:e2e` 启动两个本地服务，并用同一组 Playwright 场景验证壳层、403 和 ProblemDetails/TraceId。生产构建会把 Layui 2.13.8 从锁定的 MIT npm 包打入本地产物，不依赖公共 CDN，也不包含 layuiAdmin 产品主题源码或资产。

分别在两个终端启动开发服务：

```powershell
pnpm --filter @fullnet/admin dev
pnpm --filter @fullnet/admin-layui dev
```

Vue 默认访问 `http://127.0.0.1:5173`，Layui 默认访问 `http://127.0.0.1:5174`。两端使用同一个 `/api/v1` 契约和标准 ProblemDetails；目前壳层只提供会话契约探针，完整登录、刷新、退出与租户切换属于后续 C1 纵向切片。

## 3. 使用 Aspire 启动完整环境

```powershell
dotnet run --project src/Hosts/Full.NET.AppHost/Full.NET.AppHost.csproj
```

终端会输出带一次性登录令牌的 Aspire Dashboard 地址。Dashboard 中的正常启动顺序是：

1. 数据库和 Redis 就绪；
2. Migrator 执行 DbUp 迁移并以代码 0 退出；
3. API 和 Worker 启动；
4. API 的 `/health/live`、`/health/ready`、`/health/startup` 返回健康状态。

AppHost 默认使用 SQL Server。切换到 MySQL，可修改 `src/Hosts/Full.NET.AppHost/appsettings.json`：

```json
{
  "UseMySql": true
}
```

也可以仅对当前终端覆盖：

```powershell
$env:UseMySql = "true"
dotnet run --project src/Hosts/Full.NET.AppHost/Full.NET.AppHost.csproj
```

AppHost 给 Migrator 传入 `--seed-local`。它会幂等创建标识为 `local`、域名为 `localhost` 的开发租户；已存在时不会把启动视为失败。

## 4. 部署和迁移顺序

生产部署必须先运行 `Full.NET.Host.Migrator`，确认成功后再发布或启动 API/Worker。API 永远不在启动时执行生产迁移，Worker 也不会修改数据库结构。这样可以让迁移权限、运行时权限和回滚决策保持独立。

必要配置项：

```text
Database__Provider=SqlServer        # 或 MySql
Database__ConnectionName=fullnet
ConnectionStrings__fullnet=<由 Secret 管理器注入>
```

## 5. 缓存约定

FusionCache 是唯一缓存实现，业务代码可以依赖 `IFusionCache`，也可以依赖由 `.AsHybridCache()` 暴露的 Microsoft `HybridCache`；两者指向同一个底层实例。

没有 Redis 连接串时，框架退化为单进程内存缓存，适合单实例开发和轻量部署。多实例部署必须配置 `ConnectionStrings__redis` 或 `Cache__RedisConnectionString`，以启用分布式缓存和 Redis Backplane。租户缓存 key 必须通过 `CacheKeyBuilder` 构建，失效优先使用租户/域名 tag。

## 6. HTTP、JSON 与 Admin.NET 兼容

默认公共 API 使用真实 HTTP 状态码：成功返回直接 DTO；失败返回 RFC ProblemDetails，并在扩展字段中携带稳定错误 `code` 和 `traceId`。默认不会把所有结果包成 `success/data/code`。

每个模块应为自己的 HTTP DTO 提供 System.Text.Json `JsonSerializerContext`，并把生成的 context 插入 `TypeInfoResolverChain`。不要在请求热路径创建新的 `JsonSerializerOptions`，也不要引入 Newtonsoft.Json。

需要兼容现有 Admin.NET 前端时，在自定义 Host 中于服务默认值之后显式启用：

```csharp
builder.AddFullNetServiceDefaults();
builder.Services.AddAdminNetCompatibility();
```

该适配器只替换 `IApiResultMapper`，仍保留 400/404/409/500 等真实状态码，也不会包裹文件、流、SSE、SignalR、健康检查或 `204` 响应。

## 7. 验证管道约定

模块通过 `AddFullNetFluentValidation()` 启用适配层，并显式注册每个 `IValidator<TCommand>`。禁止程序集扫描；显式注册让模块依赖、启动成本和 Native AOT/裁剪行为保持可预测。

Validator 只放输入结构规则，例如必填、格式、范围和长度。数据库唯一性、权限、当前状态和其他依赖外部状态的业务规则仍由 Handler/Domain 负责。HTTP Request DTO 与内部 Command 保持分离；不要在 Endpoint 再复制一套只对 HTTP 生效的同类校验。

验证失败统一返回 `ErrorType.Validation` 和稳定错误码 `validation.failed`，由现有 API 映射器输出 HTTP 400 ProblemDetails。调度管道在事务终端委托之外执行，因此无效的事务命令不会打开 Dapper 事务，也不会调用 Handler。

## 8. 内部消息与 Outbox

当前进程内调用使用强类型 Contracts。事务性集成事件写入 `fn_outbox_message`，payload 固定为 `application/x-msgpack`，并同时保存事件 `Type`、`SchemaVersion`、租户、trace 和发生时间。

Worker 以租约方式批量获取消息，按 `(EventType, SchemaVersion)` 精确匹配唯一处理器。成功后才标记完成；失败会释放租约并指数退避。处理器必须幂等，因为至少一次投递允许重复执行。禁止为 Outbox 增加 JSON fallback、typeless 或 contractless MessagePack resolver；合约成员必须使用稳定、唯一的整数 key。

## 9. 日志与可观测性

应用代码使用 `ILogger<T>`，高频路径使用 `LoggerMessage` 源生成。Serilog Console Sink 位于有界异步队列后，默认队列满时丢弃而不阻塞请求；可通过以下设置调整：

```json
{
  "FullNet": {
    "Logging": {
      "AsyncBufferSize": 10000,
      "BlockWhenFull": false
    }
  }
}
```

应监控 `fullnet.logging.queue.depth`、`fullnet.logging.queue.capacity` 和 `fullnet.logging.events.dropped`。普通异步日志不承担审计账本职责；安全/业务审计后续使用独立可靠存储。日志不得记录请求体、Cookie、Authorization、连接串或消息 payload。

## 10. 通信技术矩阵

| 场景 | 当前选择 | 引入时机 |
|---|---|---|
| 模块内/模块间调用 | 进程内强类型 Contracts + CQRS | M1 已启用 |
| 跨进程服务调用 | gRPC + Protobuf | 出现真实服务拆分、独立部署与容量边界后 |
| 可靠异步事件 | MessagePack Outbox | M1 已启用 |
| 浏览器实时通信 | SignalR，优先 MessagePack、必要时 JSON | M2 Realtime |
| AI/Agentic Web | Microsoft.Extensions.AI、Agent Framework、MCP、AG-UI | 独立 M5+ 计划 |

不要仅为“将来可能微服务化”而提前把模块内调用改成 gRPC；先保持模块边界和合约稳定，再按真实部署需求拆分。
