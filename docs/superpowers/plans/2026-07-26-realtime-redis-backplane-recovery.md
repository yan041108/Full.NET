# Realtime Redis Backplane Recovery Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为 SignalR Redis Backplane 增加专用 ready 健康检查，并用 SQL Server/MySQL 下的双 API 节点证明跨节点投递、Redis 中断可见和无需重启宿主的恢复收敛。

**Architecture:** `Full.NET.Realtime.SignalR` 统一解析 Redis 配置，强制运行连接使用 `AbortOnConnectFail=false` 以保留重连能力；独立健康检查使用短连接和严格超时，只影响 `ready`。Integration 使用专用 Redis Testcontainer、两个共享数据库但独立宿主的 `FullNetApiFactory` 和真实 SignalR Client，避免停止共享 Redis 干扰其他测试。

**Tech Stack:** .NET 10、ASP.NET Core SignalR、Microsoft.AspNetCore.SignalR.Client、StackExchange.Redis、Microsoft Testing Platform、MSTest、Testcontainers Redis 8.6、SQL Server 2022、MySQL 8.4。

## Global Constraints

- Realtime 消息仍是不可靠即时下行，不得把 Redis Backplane 测试写成可靠业务事件或 Outbox 已完成的证据。
- Redis 中断不得被静默当成跨节点投递成功；Provider 可以快速抛出 Redis
  异常，也可以只暴露“远端未送达”，测试不得把入队或方法返回当成交付证明。
- 健康检查只进入 `ready`，不得让 `live` 或数据库 Schema `startup` 探针承担 Backplane 可用性。
- Channel Prefix 固定为 `fullnet:{environment}:signalr:`，不得加入租户、用户、连接或消息机器码。
- 测试必须使用专用 Redis 容器；禁止停止 `SharedDatabaseFixture` 的共享 Redis。
- 双库场景使用同一断言，且最终状态保持 `Build-verified`，不因自动化故障演练升级为 `Verified`。

---

### Task 1: Redis 配置与 ready 健康注册

**Files:**
- Create: `src/BuildingBlocks/Full.NET.Realtime.SignalR/RealtimeRedisConfiguration.cs`
- Create: `src/BuildingBlocks/Full.NET.Realtime.SignalR/Health/RealtimeBackplaneHealthCheck.cs`
- Create: `src/BuildingBlocks/Full.NET.Realtime.SignalR/Properties/AssemblyInfo.cs`
- Modify: `src/BuildingBlocks/Full.NET.Realtime.SignalR/ServiceCollectionExtensions.cs`
- Create: `tests/Full.NET.UnitTests/Realtime/RealtimeBackplaneRegistrationTests.cs`

**Interfaces:**
- Consumes: `RealtimeOptions.RedisBackplaneConnectionString`
- Produces: `RealtimeRedisConfiguration.Create(string, string) : ConfigurationOptions`
- Produces: ready 检查注册名 `realtime-backplane`

- [x] **Step 1: 写入配置与健康注册 RED**

  新建 `RealtimeBackplaneRegistrationTests`，锁定显式 `abortConnect=true` 仍被运行策略覆盖，并验证专用配置注册 ready 检查：

  ```csharp
  [TestMethod]
  public void Redis_configuration_keeps_reconnect_enabled_and_scopes_channels()
  {
      var configuration = RealtimeRedisConfiguration.Create(
          "127.0.0.1:6379,abortConnect=true",
          "Production");

      Assert.IsFalse(configuration.AbortOnConnectFail);
      Assert.AreEqual(
          "fullnet:production:signalr:",
          configuration.ChannelPrefix.ToString());
  }

  [TestMethod]
  public void Dedicated_backplane_registers_a_ready_health_check()
  {
      var services = new ServiceCollection();
      services.AddLogging();
      services.AddFullNetRealtimeSignalR(
          new ConfigurationBuilder()
              .AddInMemoryCollection(new Dictionary<string, string?>
              {
                  ["Realtime:RedisBackplaneConnectionString"] = "127.0.0.1:6379",
              })
              .Build(),
          "Testing");

      using var provider = services.BuildServiceProvider();
      var registrations = provider
          .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
          .Value.Registrations;
      Assert.IsTrue(registrations.Any(registration =>
          registration.Name == "realtime-backplane"
          && registration.Tags.Contains("ready")));
  }
  ```

- [x] **Step 2: 构建并确认 RED**

  ```powershell
  dotnet build tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --no-restore -m:1
  ```

  预期：仅因 `RealtimeRedisConfiguration` 尚不存在或 ready 注册缺失而失败。

- [x] **Step 3: 实现统一运行配置**

  `RealtimeRedisConfiguration.Create` 必须使用：

  ```csharp
  var configuration = ConfigurationOptions.Parse(connectionString);
  configuration.AbortOnConnectFail = false;
  configuration.ChannelPrefix = RedisChannel.Literal(
      $"fullnet:{environmentName.ToLowerInvariant()}:signalr:");
  return configuration;
  ```

  `ServiceCollectionExtensions` 改用对象配置：

  ```csharp
  signalRBuilder.AddStackExchangeRedis(redisOptions =>
      redisOptions.Configuration = RealtimeRedisConfiguration.Create(
          options.RedisBackplaneConnectionString,
          environmentName));
  ```

- [x] **Step 4: 实现专用健康检查**

  `RealtimeBackplaneHealthCheck` 使用两秒总超时、一次连接、无重试和 `PING`：

  ```csharp
  var configuration = ConfigurationOptions.Parse(
      options.Value.RedisBackplaneConnectionString!);
  configuration.AbortOnConnectFail = true;
  configuration.ConnectRetry = 0;
  configuration.ConnectTimeout = 1000;
  configuration.AsyncTimeout = 1000;
  await using var connection = await ConnectionMultiplexer
      .ConnectAsync(configuration)
      .WaitAsync(timeout.Token);
  _ = await connection.GetDatabase().PingAsync().WaitAsync(timeout.Token);
  ```

  超时、`RedisException`、`InvalidOperationException` 返回稳定中文 `Unhealthy`，不输出连接串。仅当 Realtime 已启用且配置了 Backplane 时注册：

  ```csharp
  services.AddHealthChecks().AddCheck<RealtimeBackplaneHealthCheck>(
      "realtime-backplane",
      tags: ["ready"]);
  ```

- [x] **Step 5: 运行聚焦 Unit 并确认 GREEN**

  ```powershell
  dotnet build tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --no-restore -m:1
  dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll `
    --filter "FullyQualifiedName~RealtimeBackplaneRegistrationTests" `
    --no-ansi --progress off --minimum-expected-tests 2 --timeout 10m
  ```

  预期：2/2 通过，失败 0、跳过 0。

### Task 2: HTTP ready 故障语义

**Files:**
- Modify: `tests/Full.NET.IntegrationTests/Api/HealthEndpointTests.cs`

**Interfaces:**
- Consumes: Task 1 的 `realtime-backplane` 注册
- Produces: 专用 Backplane 不可达时 `ready=503`、`startup=200`、`live=200`

- [x] **Step 1: 写入专用 Backplane 健康 RED**

  增加一项测试，只配置 `Realtime:RedisBackplaneConnectionString`，断言没有 `distributed-cache` 注册但存在 `realtime-backplane`：

  ```csharp
  var registrations = GetReadyRegistrationNames(app.Services);
  CollectionAssert.Contains(registrations, "database-connectivity");
  CollectionAssert.Contains(registrations, "realtime-backplane");
  CollectionAssert.DoesNotContain(registrations, "distributed-cache");
  Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
  Assert.AreEqual(HttpStatusCode.OK, startup.StatusCode);
  Assert.AreEqual(HttpStatusCode.OK, live.StatusCode);
  ```

  `StartMinimalHealthHostAsync` 增加
  `builder.Services.AddFullNetRealtimeSignalR(builder.Configuration, "Testing");`。

- [x] **Step 2: 构建 Integration 并确认行为**

  ```powershell
  dotnet build tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj `
    -c Release --no-restore -m:1
  dotnet tests/Full.NET.IntegrationTests/bin/Release/net10.0/Full.NET.IntegrationTests.dll `
    --filter "FullyQualifiedName~HealthEndpoints_ready_returns_service_unavailable_when_realtime_backplane_is_unreachable" `
    --no-ansi --progress off --minimum-expected-tests 1 --timeout 10m
  ```

  结果：Task 1 已由 Unit RED 驱动完成，因此该 Integration 用例首次运行即
  **1/1** 通过；没有把测试装配失败或未执行虚报为额外 RED。

- [x] **Step 3: 复跑全部 HealthEndpointTests**

  ```powershell
  dotnet tests/Full.NET.IntegrationTests/bin/Release/net10.0/Full.NET.IntegrationTests.dll `
    --filter "FullyQualifiedName~HealthEndpointTests" `
    --no-ansi --progress off --minimum-expected-tests 8 --timeout 15m
  ```

  预期：既有数据库、Schema、Cache 健康语义不变，全部通过。

### Task 3: 双节点 Backplane 中断与恢复

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj`
- Create: `tests/Full.NET.IntegrationTests/Realtime/RealtimeRedisBackplaneRecoveryTests.cs`

**Interfaces:**
- Consumes: `FullNetApiFactory`、`IRealtimePublisher`、`RealtimeGroups.User(Guid)`
- Produces: SQL Server/MySQL 各一项真实双节点故障恢复测试

- [x] **Step 1: 增加官方测试客户端依赖**

  集中登记并只在 Integration 测试项目引用：

  ```xml
  <PackageVersion Include="Microsoft.AspNetCore.SignalR.Client" Version="10.0.10" />
  ```

  ```xml
  <PackageReference Include="Microsoft.AspNetCore.SignalR.Client" />
  ```

- [x] **Step 2: 写入双节点故障恢复测试**

  SQL Server/MySQL 两个测试方法共享：

  ```csharp
  private static async Task VerifyBackplaneRecoversAsync(
      DatabaseProvider provider,
      string connectionString)
  ```

  测试使用 `new RedisBuilder("redis:8.6")` 创建专用容器，并把预留的空闲宿主
  TCP 端口显式绑定到容器 6379；两个
  `FullNetApiFactory` 只设置：

  ```csharp
  ["Realtime:RedisBackplaneConnectionString"] = redis.GetConnectionString()
  ```

  客户端固定 Long Polling 并复用第一个 TestServer handler：

  ```csharp
  var connection = new HubConnectionBuilder()
      .WithUrl("http://localhost/hubs/notifications", options =>
      {
          options.AccessTokenProvider = () => Task.FromResult<string?>(identity.AccessToken);
          options.Transports = HttpTransportType.LongPolling;
          options.HttpMessageHandlerFactory = _ => firstFactory.Server.CreateHandler();
      })
      .Build();
  ```

  断言顺序：

  1. 客户端连接节点 A，节点 B 的 `IRealtimePublisher.PublishToUserAsync` 发送 `realtime.backplane.before_outage`，五秒内收到。
  2. `redis.StopAsync()` 后两个 `/health/ready` 都收敛为 503。
  3. 节点 B 跨节点发布 `realtime.backplane.during_outage` 可以抛出
     `RedisException`；无论调用端表现如何，客户端都不得收到该机器码。
  4. `redis.StartAsync()` 后两个 ready 都恢复 200；循环发布 `realtime.backplane.after_recovery`，十秒内收到，且不重启节点 A/B 或客户端。

- [x] **Step 3: 运行双库聚焦测试**

  ```powershell
  dotnet build tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj `
    -c Release --no-restore -m:1
  dotnet tests/Full.NET.IntegrationTests/bin/Release/net10.0/Full.NET.IntegrationTests.dll `
    --filter "FullyQualifiedName~RealtimeRedisBackplaneRecoveryTests" `
    --no-ansi --progress off --minimum-expected-tests 2 --timeout 20m
  ```

  预期：2/2 通过，失败 0、跳过 0；若 Redis Provider 抛出更具体的
  `RedisConnectionException`，测试只捕获公共基类 `RedisException`，但不要求
  Provider 必须通过异常表达断连。

### Task 4: 状态、门槛、复盘与主线收口

**Files:**
- Modify: `README.md`
- Modify: `.github/workflows/ci.yml`
- Modify: `.agents/skills/fullnet-module-delivery/references/delivery-map.md`
- Modify: `docs/development/getting-started.md`
- Modify: `docs/roadmap/capability-status.md`
- Modify: `docs/superpowers/plans/2026-07-18-architecture-hardening.md`
- Modify: `docs/superpowers/plans/2026-07-26-realtime-signalr-foundation-vertical-slice.md`
- Modify: `docs/verification/realtime-signalr-foundation-2026-07-26.md`
- Modify: `docs/verification/test-threshold-audit-2026-07-19.md`
- Create: `docs/operations/realtime-redis-backplane.md`
- Create: `docs/verification/realtime-redis-backplane-recovery-2026-07-26.md`
- Modify: `scripts/testing/run-integration-shard.mjs`

**Interfaces:**
- Consumes: Tasks 1–3 的 RED/GREEN、健康和双库证据
- Produces: 最新 `main` 上 canonical 门槛 `392/7/49/189`，Infrastructure `57`

- [x] **Step 1: 同步状态与运维边界**

  文档记录：

  - Realtime 专用 Redis 进入 ready，故障不影响 live/startup；
  - 跨节点即时消息在 Backplane 中断期间不可靠，业务可靠通知必须经 Outbox；
  - Provider 自动重连后不要求重启 API 节点；
  - 状态保持 `Build-verified`，管理端客户端、多实例生产部署和真实告警仍未验证。

- [x] **Step 2: 同步当前分支门槛**

  当前分支在 `main@1745244` 的 `390/7/49/186` 基线上新增 Unit 2 项、
  Integration 3 项，因此四处 canonical 门槛为 `392/7/49/189`；Integration
  分片为 API SQL Server 35 + API MySQL 35 + Migrations 62 +
  Infrastructure 57 = 189，未覆盖 OpenAPI 或 Outbox 的前序门槛。

- [ ] **Step 3: 执行新鲜验证**

  ```powershell
  dotnet build Full.NET.slnx -c Release
  dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --no-ansi --progress off --minimum-expected-tests 392 --timeout 20m
  dotnet tests/Full.NET.CompatibilityTests/bin/Release/net10.0/Full.NET.CompatibilityTests.dll --no-ansi --progress off --minimum-expected-tests 7 --timeout 10m
  dotnet tests/Full.NET.ArchitectureTests/bin/Release/net10.0/Full.NET.ArchitectureTests.dll --no-ansi --progress off --minimum-expected-tests 49 --timeout 10m
  dotnet tests/Full.NET.IntegrationTests/bin/Release/net10.0/Full.NET.IntegrationTests.dll --no-ansi --progress off --minimum-expected-tests 189 --timeout 90m
  pnpm test:naming
  pnpm test:skills
  pnpm test:governance
  pnpm test:integration:tooling
  pnpm test:integration:partitions
  git diff --check
  git status --short --branch
  ```

- [x] **Step 4: 执行规则与 Skills 复盘**

  已先读 `rules/rule-evolution.md`，再读 `rules/skill-evolution.md`。本轮把容器
  stop/start 随机宿主端口漂移登记为首次候选经验；`fullnet-realtime-feature`
  候选次数更新为 2，但尚缺第二类业务消费者或生产编排流程，不创建新 Skill。

- [ ] **Step 5: 提交、同步并回到 main**

  提交隔离分支后等待日志、API Key 和 Outbox 分支完成；将最新 `main` 合入本分支，按
  `+2/+3` 重新计算门槛并重跑最终验证。随后将本分支合并到 `main`，确认工作树干净，
  删除 `codex/realtime-redis-backplane-recovery` 分支与对应工作树。

## Self-Review

- Spec coverage：覆盖 dedicated Redis ready、双节点交付、故障可见、自动恢复、双库、运维和状态；明确不做客户端、业务通知和 Outbox 推送。
- Placeholder scan：无 `TBD`、`TODO`、泛化“适当处理”或未定义类型。
- Type consistency：`RealtimeRedisConfiguration.Create`、`RealtimeBackplaneHealthCheck`、健康检查名与测试筛选在全部 Task 中一致。
