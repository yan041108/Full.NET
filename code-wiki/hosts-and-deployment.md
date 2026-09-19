# 宿主与部署

## 1. 三宿主职责分离

Full.NET 按运行角色分离为三个独立宿主（AppHost 只负责本地编排，不是第四种角色）。

| 宿主 | 项目位置 | 职责 | 启动要求 |
|------|----------|------|----------|
| **API** | `src/Hosts/Full.NET.Host.Api/` | HTTP Endpoint + 实时 SignalR Hub + 健康检查 + OpenAPI | 数据库已迁移完成；Redis（缓存 + Backplane）；S3（文件）可选 |
| **Worker** | `src/Hosts/Full.NET.Host.Worker/` | Outbox 轮询/发布；Retention 清理；Kafka Consumer；CDC Shadow；本地投影同步 | 数据库就绪；Kafka（仅 CdcKafka 模式） |
| **Migrator** | `src/Hosts/Full.NET.Host.Migrator/` | DbUp 迁移；可选种子播种（Baseline/Development/Test/Demo）；迁移审计 | 数据库连接 + 执行 DDL 权限；生产仅 Baseline |
| **AppHost** | `src/Hosts/Full.NET.AppHost/` | .NET Aspire 本地编排：按依赖启动 SQL Server/Redis/Migrator(→API→Worker) | 仅开发/测试；需要 Docker |

### 1.1 API 宿主注册

> 文件：[`src/Hosts/Full.NET.Host.Api/Program.cs`](file:///G:/wwwroot/github_fork/Full.NET/src/Hosts/Full.NET.Host.Api/Program.cs)

```csharp
var builder = WebApplication.CreateBuilder(args);

// 1. ServiceDefaults（Serilog + OTel + Health + 限流 + 安全头基线）
builder.AddFullNetServiceDefaults();

// 2. BuildingBlocks 装配（固定顺序，确保中间件与可达路径稳定）
builder.Services.AddFullNetDataProtection(builder.Configuration, builder.Environment);
builder.Services.AddFullNetTrustedProxyForwarding(builder.Configuration);
builder.Services.AddFullNetOpenApi();
builder.Services.AddFullNetRateLimiter(builder.Configuration);
builder.Services.AddFullNetDapper(builder.Configuration, builder.Environment.EnvironmentName);
builder.Services.AddFullNetDatabaseSchemaModeGuard();
HostApiServiceRegistration.AddIntegrationEventSerialization(builder.Services);    // MemoryPack
HostApiServiceRegistration.AddKafkaReplayOperations(builder.Services, builder.Configuration);
builder.Services.AddFullNetCaching(builder.Configuration, builder.Environment.EnvironmentName);
builder.Services.AddFullNetRealtimeSignalR(builder.Configuration, builder.Environment.EnvironmentName);

// 3. Composition：所有业务模块按 Api Profile 装配，物化只读模块目录
builder.Services.AddFullNetApplicationModules(builder.Configuration, FullNetHostProfile.Api);

var app = builder.Build();

// 4. 中间件管道（固定四阶段插入位置）
app.UseFullNetTrustedProxyForwarding();
app.UseFullNetLocalization();
app.UseFullNetRequestLogging();
app.UseExceptionHandler();
app.UseCors(FullNetModuleCatalog.BrowserCorsPolicy);
app.UseRateLimiter();
app.UseFullNetModuleMiddleware(ModulePipelineStage.BeforeAuthentication);
app.UseAuthentication();
app.UseFullNetModuleMiddleware(ModulePipelineStage.BeforeAuthorization);
app.UseAuthorization();
app.UseFullNetModuleMiddleware(ModulePipelineStage.BeforeEndpoints);

// 5. Endpoints：OpenAPI/Scalar、Health、SignalR Hub 与模块 Endpoint
app.MapFullNetOpenApi();
app.MapScalarApiReference();
app.MapFullNetHealthEndpoints();
app.MapFullNetRealtime();
app.MapFullNetModules();
app.Run();
```

> 模块装配只能经组合根 `AddFullNetApplicationModules` 完成；中间件按 `BeforeAuthentication → Authentication → BeforeAuthorization → Authorization → BeforeEndpoints` 四阶段插入模块钩子。

### 1.2 Worker 宿主注册

> 文件：[`src/Hosts/Full.NET.Host.Worker/Program.cs`](file:///G:/wwwroot/github_fork/Full.NET/src/Hosts/Full.NET.Host.Worker/Program.cs)

```csharp
// 命令行先解析一次性 Outbox 版本退役扫描；失败立即以稳定错误码退出。
var commandLine = OutboxVersionRetirementCommandLine.Parse(args);

var builder = WebApplication.CreateBuilder(commandLine.HostArguments.ToArray());

// 1. ServiceDefaults + 数据访问 + AOT 条件编译 + 守卫
builder.AddFullNetServiceDefaults();
builder.Services.AddFullNetDataProtection(builder.Configuration, builder.Environment);
builder.Services.AddFullNetDapper(builder.Configuration, builder.Environment.EnvironmentName);
#if FULLNET_AOT_COMPILE
WorkerDapperAotRegistration.Register();      // Native AOT 物化器/参数绑定器
#endif
builder.Services.AddFullNetDatabaseSchemaModeGuard();
builder.Services.AddFullNetMemoryPack();

// 2. OpenTelemetry Meter/Source（Outbox/Retention/Shadow/Kafka 四组）
builder.Services.AddOpenTelemetry()
    .WithMetrics(m => m
        .AddMeter(OutboxBacklogTelemetry.MeterName)
        .AddMeter(OutboxRetentionTelemetry.MeterName)
        .AddMeter(ShadowEventComparisonProcessor.MeterName)
        .AddMeter(KafkaMessagingTelemetry.MeterName))
    .WithTracing(t => t
        .AddSource(KafkaMessagingTelemetry.ActivitySourceName)
        .AddSource(IntegrationEventConsumerTelemetry.ActivitySourceName));

// 3. Caching + Realtime Publisher（只发不收）+ Options 校验
builder.Services.AddFullNetCaching(builder.Configuration, builder.Environment.EnvironmentName);
builder.Services.AddFullNetRealtimePublisher(builder.Configuration, builder.Environment.EnvironmentName);
builder.Services.AddOptions<OutboxWorkerOptions>().Bind(...).ValidateOnStart();
builder.Services.AddOptions<OutboxRetentionOptions>().Bind(...).ValidateOnStart();
builder.Services.AddOptions<ShadowComparisonOptions>().Bind(...).ValidateOnStart();
builder.Services.AddOptions<MessagingWorkerOptions>().Bind(...).ValidateOnStart();

// 4. 模块后台能力（按 Worker Profile，只贡献 AddBackgroundServices）
builder.Services.AddFullNetApplicationModules(builder.Configuration, FullNetHostProfile.Worker);

// 5. 按 MessagingWorkerMode 注册后台服务（CdcKafka 已规范化为 HybridKafka）
//    LegacyPolling / ShadowCdc / HybridKafka：始终注册 OutboxProcessor + OutboxRetentionProcessor
//    ShadowCdc：注册 ShadowEventComparisonProcessor
//    HybridKafka：额外注册 AddFullNetKafkaMessaging

// 6. 启动前校验 IntegrationEventHandler 路由唯一性 + Shadow/Hybrid Kafka 目录
//    非 Worker 退役命令：app.MapFullNetHealthEndpoints(); await app.RunAsync();
//    退役命令：扫描后输出 JSON 报告并退出（不注册后台服务）
```

> Worker 不装入 HTTP 管道、认证或完整模块；后台服务按 `MessagingWorkerMode`（`LegacyPolling` / `ShadowCdc` / `HybridKafka`）选择注册；`CdcKafka` 作为过时别名在一版内规范化为 `HybridKafka`。一次性 Outbox 版本退役扫描使用独立 CLI 入口，扫描结果以机器可读 JSON 输出。

### 1.3 Migrator 宿主工作流

> 文件：[`src/Hosts/Full.NET.Host.Migrator/Program.cs`](file:///G:/wwwroot/github_fork/Full.NET/src/Hosts/Full.NET.Host.Migrator/Program.cs) 与 [`src/Hosts/Full.NET.Host.Migrator/MigratorWorkflow.cs`](file:///G:/wwwroot/github_fork/Full.NET/src/Hosts/Full.NET.Host.Migrator/MigratorWorkflow.cs)

```csharp
// Program.cs 装配顺序
var builder = Host.CreateApplicationBuilder(args);
builder.AddFullNetServiceDefaults();
builder.Services.AddFullNetDapper(builder.Configuration, builder.Environment.EnvironmentName);
builder.Services.AddRouting();
builder.Services.AddFullNetCaching(builder.Configuration, builder.Environment.EnvironmentName);
builder.Services.AddFullNetMemoryPack();
builder.Services.AddFullNetMigrations(builder.Configuration);
builder.Services.AddFullNetSeeding(builder.Configuration);
// 仅装配 Migrator Profile（最小闭包，只贡献 AddMigrationServices）
builder.Services.AddFullNetApplicationModules(builder.Configuration, FullNetHostProfile.Migrator);
builder.Services.AddScoped<MigratorWorkflow>();
```

```csharp
// MigratorWorkflow.RunAsync 工作流顺序
// 1. SeedCommandLine.Parse(args)：解析 --seed <profile>，错误以稳定错误码抛出
//    Production 默认只迁移，不播种
//    --seed baseline   : 生产安全基线（仅宿主管理员 + 菜单目录）
//    --seed development: Baseline + 本地租户 + 测试用户（AppHost 默认）
//    --seed test       : Development + 测试夹具
//    --seed demo       : Test + 示例业务数据
//    --seed-local      : 待退役别名，规范化为 development（UsesLegacyAlias=true）

// 2. migrationRunner.MigrateAsync(ct)：DbUp 执行 SQL Server 或 MySQL 迁移脚本
//    失败抛 MigratorWorkflowException(MigratorErrorCodes.MigrationFailed)

// 3. 迁移成功后才允许写入 Seed
//    seedOrchestrator.RunAsync(commandLine.Profile.Value, ct)
//    按 Profile 过滤 Contributor，按依赖拓扑排序；失败以 SeedError 抛出

// 4. 返回 MigratorWorkflowResult(ExecutedScriptCount, SeedProfile, UsesLegacyAlias)
//    Program 输出审计：执行脚本数、Seed Profile 摘要、Legacy Alias 警告
```

### 1.4 AppHost 本地编排

> 文件：[`src/Hosts/Full.NET.AppHost/Program.cs`](file:///G:/wwwroot/github_fork/Full.NET/src/Hosts/Full.NET.AppHost/Program.cs)

```csharp
var builder = DistributedApplication.CreateBuilder(args);

// 1. 资源：Redis、SQL Server 或 MySQL（按 UseMySql 切换）
var redis = builder.AddRedis("redis");
IResourceBuilder<IResourceWithConnectionString> database = useMySql
    ? builder.AddMySql("mysql").AddDatabase("fullnet")
    : builder.AddSqlServer("sql").AddDatabase("fullnet");

// 2. 隐藏式参数：bootstrap 凭据 + UUID 契约维护开关
var bootstrapUsername = builder.AddParameter("identity-bootstrap-username");
var bootstrapPassword = builder.AddParameter("identity-bootstrap-password", secret: true);
var uuidContractMaintenanceMode = builder.AddParameter("uuid-contract-maintenance-mode");
// … BackupVerified / LegacyWritersStopped / DdlApprovalId

// 3. Migrator 先就绪 → WaitFor(database)
//    默认携带 --seed development，本地完成基线 + 测试用户播种
var migrator = builder.AddProject<Projects.Full_NET_Host_Migrator>("migrator")
    .WithReference(database)
    .WithEnvironment("Database__Provider", provider)
    .WithEnvironment("Database__MySqlGuidStorageMode", "Binary16")
    .WithArgs("--seed", "development")
    .WaitFor(database);

// 4. API、Worker 均 WaitForCompletion(migrator)
//    本地共用一个 Redis；生产 Cache/Realtime 隔离由显式连接串门禁强制
builder.AddProject<Projects.Full_NET_Host_Api>("api")
    .WithReference(database).WithReference(redis)
    .WithEnvironment("Realtime__AllowSharedRedisInDevelopment", "true")
    .WaitForCompletion(migrator);

builder.AddProject<Projects.Full_NET_Host_Worker>("worker")
    .WithReference(database).WithReference(redis)
    .WithEnvironment("Realtime__AllowSharedRedisInDevelopment", "true")
    .WaitForCompletion(migrator);
```

> AppHost 仅作本地开发与测试编排；首次运行时交互输入宿主管理员用户名/密码（Secret Parameter）。`Aspire Dashboard: http://localhost:15200/`。

---

## 2. Docker 镜像

### 2.1 Dockerfile 结构

> 文件：[`eng/docker/Dockerfile`](file:///G:/wwwroot/github_fork/Full.NET/eng/docker/Dockerfile)

**多阶段 + 多最终目标（`# syntax=docker/dockerfile:1.7`）**：

```dockerfile
ARG DOTNET_VERSION=10.0

# ======== Build Stage：单一 SDK 同时还原并 publish 三个宿主 ========
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
ARG BUILD_CONFIGURATION=Release
ARG SOURCE_COMMIT=unknown
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props ./
COPY contracts/ ./contracts/
COPY src/ ./src/
RUN dotnet restore src/Hosts/Full.NET.Host.Api/Full.NET.Host.Api.csproj \
 && dotnet restore src/Hosts/Full.NET.Host.Worker/Full.NET.Host.Worker.csproj \
 && dotnet restore src/Hosts/Full.NET.Host.Migrator/Full.NET.Host.Migrator.csproj
RUN dotnet publish .../Full.NET.Host.Api.csproj      -o /app/publish/api      /p:SourceRevisionId=${SOURCE_COMMIT} \
 && dotnet publish .../Full.NET.Host.Worker.csproj    -o /app/publish/worker   /p:SourceRevisionId=${SOURCE_COMMIT} \
 && dotnet publish .../Full.NET.Host.Migrator.csproj  -o /app/publish/migrator  /p:SourceRevisionId=${SOURCE_COMMIT}

# ======== Final Target: API（暴露 8080 + ASPNETCORE_URLS）========
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS api
WORKDIR /app
COPY --from=build /app/publish/api .
ENV ASPNETCORE_URLS=http://+:8080 DOTNET_EnableDiagnostics=0 FULLNET_SOURCE_COMMIT=${SOURCE_COMMIT}
EXPOSE 8080
LABEL org.opencontainers.image.title="Full.NET API" \
      org.opencontainers.image.revision="${SOURCE_COMMIT}" \
      org.opencontainers.image.source="https://github.com/full-net/Full.NET"
USER $APP_UID
ENTRYPOINT ["dotnet", "Full.NET.Host.Api.dll"]

# ======== Final Target: Worker（不暴露端口，无 ASPNETCORE_URLS）========
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS worker
WORKDIR /app
COPY --from=build /app/publish/worker .
ENV DOTNET_EnableDiagnostics=0 FULLNET_SOURCE_COMMIT=${SOURCE_COMMIT}
LABEL org.opencontainers.image.title="Full.NET Worker" ...
USER $APP_UID
ENTRYPOINT ["dotnet", "Full.NET.Host.Worker.dll"]

# ======== Final Target: Migrator（一次性 Job 容器）========
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS migrator
WORKDIR /app
COPY --from=build /app/publish/migrator .
ENV DOTNET_EnableDiagnostics=0 FULLNET_SOURCE_COMMIT=${SOURCE_COMMIT}
LABEL org.opencontainers.image.title="Full.NET Migrator" ...
USER $APP_UID
ENTRYPOINT ["dotnet", "Full.NET.Host.Migrator.dll"]
```

### 2.2 构建镜像

```powershell
# 单角色镜像（推荐生产）
docker build -f eng/docker/Dockerfile `
  --target api -t fullnet-api:1.0.0 .
docker build -f eng/docker/Dockerfile `
  --target worker -t fullnet-worker:1.0.0 .
docker build -f eng/docker/Dockerfile `
  --target migrator -t fullnet-migrator:1.0.0 .

# 验证镜像（契约测试）
pnpm test:container-images
```

**安全要点**（镜像 + Helm `podSecurityContext` / `containerSecurityContext`）：
- 非 root 用户：Dockerfile 用 `USER $APP_UID`（aspnet 基础镜像提供）；Helm `runAsUser=runAsGroup=fsGroup=1654`
- 只读根文件系统（`readOnlyRootFilesystem: true`）
- 全部 capability DROP（`capabilities.drop: [ALL]`）
- `seccompProfile: RuntimeDefault`
- `DOTNET_EnableDiagnostics=0`（生产禁用诊断）
- `org.opencontainers.image.title/revision/source` OCI 标签 + `FULLNET_SOURCE_COMMIT` 环境变量
- API 镜像暴露 `8080` 并设置 `ASPNETCORE_URLS=http://+:8080`；Worker 与 Migrator 不暴露端口

---

## 3. Helm Chart 生产部署

### 3.1 Chart 基础信息

> 目录：[`deploy/helm/fullnet/`](file:///G:/wwwroot/github_fork/Full.NET/deploy/helm/fullnet) — [`Chart.yaml`](file:///G:/wwwroot/github_fork/Full.NET/deploy/helm/fullnet/Chart.yaml)、[`values.yaml`](file:///G:/wwwroot/github_fork/Full.NET/deploy/helm/fullnet/values.yaml)、[`values.schema.json`](file:///G:/wwwroot/github_fork/Full.NET/deploy/helm/fullnet/values.schema.json)

```yaml
# Chart.yaml
apiVersion: v2
name: fullnet
description: Full.NET modular monolith production Helm chart (API / Worker / Migrator roles only)
type: application
version: 0.1.0
appVersion: "1.0.0"
# Chart 不安装数据库、Redis、S3、可观测性后端、WAF 或分布式限流服务。
keywords: [fullnet, modular-monolith]
home: https://github.com/full-net/Full.NET
```

> `values.schema.json` 对所有键做 JSON Schema 校验；`ci/` 下为契约测试 values 文件。

### 3.2 生产角色分离（三 Release）

> 生产 `production=true` 时禁止同一 Release 同时启用多个角色。

```bash
# Release 1: Migrator（先运行，Job 成功后再升级 API/Worker）
helm upgrade --install fullnet-migrator deploy/helm/fullnet \
  --set production=true \
  --set roles.migrator=true \
  --set database.provider=SqlServer \
  --values deploy/helm/fullnet/ci/values-role-migrator.yaml

# Release 2: API
helm upgrade --install fullnet-api deploy/helm/fullnet \
  --set production=true \
  --set roles.api=true \
  --set api.replicaCount=3 \
  --values deploy/helm/fullnet/ci/values-role-api.yaml

# Release 3: Worker
helm upgrade --install fullnet-worker deploy/helm/fullnet \
  --set production=true \
  --set roles.worker=true \
  --values deploy/helm/fullnet/ci/values-role-worker.yaml
```

### 3.3 发布顺序

```
部署顺序（不可逆，Helm 契约测试强制执行）：
  1. Migrator Job ──等待成功──►
       │ 迁移 + Baseline Seed（backoffLimit=1, activeDeadlineSeconds=1800, hookWeight=-5）
       ▼
  2. API Deployment (HPA 滚动升级，PDB minAvailable=2)
       │ 等待 ReadyReplicas >= 旧版本
       ▼
  3. Worker Deployment
       │ Outbox/Retention/Kafka Consumer（PDB minAvailable=1）
       ▼
  4. 健康检查 / 回滚就绪
```

### 3.4 关键 values.yaml 配置

> 文件：[`deploy/helm/fullnet/values.yaml`](file:///G:/wwwroot/github_fork/Full.NET/deploy/helm/fullnet/values.yaml)。下表为节选；完整键以 `values.schema.json` 为权威来源。

```yaml
production: true                  # 默认开启；与 roles 三选一构成生产唯一角色门禁

roles: { api: false, worker: false, migrator: false }

image:
  repository: fullnet            # 各角色镜像名：{repository}-api|worker|migrator
  tag: "1.0.0"

podSecurityContext:              # 非 root，UID/GID 1654
  runAsNonRoot: true
  runAsUser: 1654
  runAsGroup: 1654
  fsGroup: 1654
  seccompProfile: { type: RuntimeDefault }

containerSecurityContext:
  allowPrivilegeEscalation: false
  readOnlyRootFilesystem: true
  capabilities: { drop: [ALL] }
  seccompProfile: { type: RuntimeDefault }

database:
  provider: SqlServer            # SqlServer | MySql
  connectionSecretName: fullnet-database
  connectionSecretKey: connectionString

cache:
  redisSecretName: fullnet-cache-redis
  redisSecretKey: connectionString

realtime:                        # SignalR Redis Backplane
  redisSecretName: fullnet-realtime-redis
  redisSecretKey: connectionString
  transportMode: Default        # Default | WebSocketsOnly
  skipNegotiation: false
  requireSessionAffinity: true   # 多实例场景：会话亲和

files:
  s3SecretName: fullnet-s3       # 对象存储 Secret

dataProtection:                  # ASP.NET Data Protection 密钥共享
  applicationName: Full.NET
  existingClaimName: ""          # 优先已有 RWX PVC；或 K8s Secret + 证书
  certificateSecretName: fullnet-dp-cert
  persistence:
    create: false                # 默认不创建 PVC，必须由已验证 RWX StorageClass 提供

databaseConnectionBudget:        # 默认 600 总预算
  total: 600
  apiMaxPoolSize: 40             # 12 * 40 = 480
  workerMaxPoolSize: 10          # 8 * 10 = 80
  migrationReserve: 20           # 480 + 80 + 20 = 580，留余量
  healthReserve: 2               # 健康检查静态余量
  workerCriticalReserve: 1       # Worker 关键续租/终态写入余量
  apiPermitLimit: 38             # 客户端准入上限（含健康检查余量）
  apiQueueLimit: 0
  apiAcquireTimeoutMilliseconds: 250
  workerPermitLimit: 7
  workerQueueLimit: 1
  workerAcquireTimeoutMilliseconds: 1000

edgeProtection:                  # 全局 Edge 能力（CDN/WAF/APIGateway）
  declared: false                # 生产必须声明已部署
  authDimension: client_id
  anonymousDimension: client_ip
  unavailablePolicy: fail-closed # fail-open | fail-closed
  secretName: ""
  serviceName: ""

applicationRateLimiting:         # 应用内每实例限流（纵深防御）
  globalApiPermitLimitPerMinutePerReplica: 100

api:
  replicaCount: 3
  terminationGracePeriodSeconds: 60
  preStopSleepSeconds: 10
  kafkaReplay:                   # API 同步范围重放默认关闭
    enabled: false
  resources:
    requests: { cpu: 250m, memory: 512Mi }
    limits:   { cpu: "2",  memory: 2Gi }
  hpa: { enabled: true, minReplicas: 3, maxReplicas: 12, targetCPUUtilizationPercentage: 70 }
  pdb: { enabled: true, minAvailable: 2 }
  service: { type: ClusterIP, port: 80, targetPort: 8080 }
  ingress:
    enabled: true
    className: nginx
    host: fullnet.example.com
    bodySize: 32m
    useForwardedHeaders: true    # 可信代理链由集群入口配置
  topologySpreadConstraints:     # 跨可用区打散
    enabled: true
    maxSkew: 1
    topologyKey: topology.kubernetes.io/zone
    whenUnsatisfiable: ScheduleAnyway

worker:
  replicaCount: 2
  maxConcurrency: 1
  messaging:
    mode: LegacyPolling          # LegacyPolling | ShadowCdc | HybridKafka
    kafka:
      bootstrapSecretName: ""
      consumerGroupProtocol: Classic  # Classic | CooperativeSticky
      brokerMajorVersion: 4
  terminationGracePeriodSeconds: 90
  preStopSleepSeconds: 15
  hpa: { enabled: true, minReplicas: 2, maxReplicas: 8, targetCPUUtilizationPercentage: 70 }
  pdb: { enabled: true, minAvailable: 1 }

migrator:                        # Helm Hook Job
  backoffLimit: 1
  activeDeadlineSeconds: 1800
  hookWeight: "-5"               # 早于 API/Worker 安装
  seedArgs: []                   # 生产默认不播种；CI/CD 可显式启用 baseline

networkPolicy:
  enabled: true                  # 入口仅 ingress-nginx 命名空间 + 同 Release 标签
  ingressNamespace: ingress-nginx

codeGeneration:                  # Host 代码生成 Apply/Rollback 工作区
  apply:
    enabled: false
    enabledWhenProduction: true  # 生产 Release 自动注入 Enabled=true
    workspaceRoot: /var/fullnet/codegeneration
    distributedGateEnabled: true
    maxRollbackChainLength: 16
  checkpointRetention:           # 回滚链路清理
    enabled: true
    retentionDays: 7
    pollSeconds: 300
    maxDeletesPerRun: 32
    maxCheckpointCount: 128
  workspace:
    existingClaimName: ""
    persistence: { create: false, size: 5Gi }
```

### 3.5 数据库连接预算

```
默认配置（total 600）：
  api.replicaCount(12) × 40 = 480 （apiMaxPoolSize）
  worker.replicaCount(8) × 10 = 80 （workerMaxPoolSize）
  + migrationReserve 20
  ─────────────────────────────────
  Total 580 ≤ budget 600
```

扩副本前必须核对数据库最大连接数 + 预算。

---

## 4. 生产参考拓扑（K8s + Helm）

```
                    ┌─────────────────────┐
                    │   Edge WAF / CDN    │ 限流、WAF、TLS 终止
                    │ （全局能力，Chart   │
                    │   只引用 Service）  │
                    └──────────┬──────────┘
                               │
                    ┌──────────▼──────────┐
                    │   Ingress (Nginx)   │
                    └──────────┬──────────┘
                               │
              ┌────────────────┼────────────────┐
              ▼                ▼                ▼
      ┌─────────────┐  ┌─────────────┐  ┌─────────────┐
      │ API HPA(3~12)│  │ API HPA(3~12)│  │  PDB: min=2 │
      └──────┬──────┘  └──────┬──────┘  └─────────────┘
             │                │
             └────────┬───────┘
                      │
          ┌───────────▼────────────┐
          │   Service (ClusterIP)  │
          └───────────┬────────────┘
                      │
     ┌────────────────┼──────────────────┐
     ▼                ▼                  ▼
SQL Server/MySQL    Redis           S3 / MinIO
 (外部管理)      (Cache + Backplane)  (文件 Blob)
     │                │                  │
     └───────────┬────┴──────┬───────────┘
                 ▼           ▼
         ┌─────────────┐ ┌─────────────┐
         │ Worker HPA  │ │ Kafka +     │
         │ Outbox/Ret  │ │ Debezium    │
         └─────────────┘ └─────────────┘

可观测性：OTel Collector → Prometheus/Grafana/Loki（外部管理）
```

**月度 SLO**：99.9%。**容量目标**：1 万同时在途动态请求（需专用环境认证，开发机不承诺）。

---

## 5. CDC/Kafka 消息栈部署

> 目录：[`deploy/messaging/`](file:///G:/wwwroot/github_fork/Full.NET/deploy/messaging)

### 5.1 Docker Compose 本地验证

```bash
# 启动 Kafka + Debezium + Schema Registry
docker compose -f deploy/messaging/compose.kafka-debezium.yml up -d

# SQL Server 启用 CDC（幂等脚本）
sqlcmd -S localhost -d FullNet -i deploy/messaging/sqlserver/enable-outbox-cdc.sql

# MySQL 验证 Binlog
mysql -h 127.0.0.1 < deploy/messaging/mysql/verify-binlog.sql
```

### 5.2 事件流所有权配置

```json
// 初始配置（Migrator Seed 时幂等注册，按 TopicCode 去重）
[
  {
    "StreamId": "fullnet.tenancy.tenant.changes",
    "TopicCode": "tenancy-tenant-changes-v1",
    "Owner": "LegacyPolling",    // 初始 Worker 轮询
    "TargetOwner": "CdcKafka",   // 受控切换目标
    "Partitions": 12,
    "ReplicationFactor": 3
  }
]
```

切换步骤（运维 Runbook）：
1. 配置 `Owner=ShadowCdc`（Worker `MessagingWorkerMode=ShadowCdc`）→ 两边同时发布，事件对比校验
2. 对比通过 → 切换 `Owner=CdcKafka`（CAS 原子 + PreviousOwner 检查；Worker 同步迁移到 `HybridKafka`）
3. 旧 Worker 停止领取该流，CDC Relay 开始捕获
4. 监控 Consumer Lag、最老消息年龄、重试队列深度
5. 回滚：反向从 `CdcKafka → LegacyPolling` 同样 CAS

> `EventDeliveryOwner` 枚举（`LegacyPolling=0` / `ShadowCdc=1` / `CdcKafka=2`）描述正式事件流的单一发布所有权；Worker `MessagingWorkerMode`（`LegacyPolling` / `ShadowCdc` / `HybridKafka`，`CdcKafka` 作为过时别名规范化为 `HybridKafka`）描述 Worker 进程自身的运行模式。两者需配套使用：`ShadowCdc` 模式同时跑 Outbox + CDC 影子 Topic 用于对比；`HybridKafka` 模式由 `IEffectiveEventDeliveryOwnerResolver` 按流跳过所有权为 `CdcKafka` 的消息（抛 `LegacyOwnerRevoked` 死信）。

---

## 6. 可观测性部署

> 目录：[`deploy/observability/`](file:///G:/wwwroot/github_fork/Full.NET/deploy/observability)

```yaml
# 推荐组件（Helm Values 或外部 Operator 管理）
- OpenTelemetry Collector（OTLP GRPC 接收 → 分流）
  ├── Metrics → Prometheus Remote Write
  ├── Traces → Tempo / Jaeger
  └── Logs → Loki
- Prometheus（规则：prometheus-rules.yaml）
- Grafana Dashboard：grafana-dashboard.json
- Fluent Bit（可选容器日志收集）：fluent-bit-values.yaml
```

**核心健康检查**：
| 探针 | 类型 | 端点 |
|------|------|------|
| API Startup | Startup | `/health/startup`（DB + Cache） |
| API Readiness | Readiness | `/health/ready`（DB + Cache + SignalR Backplane） |
| API Liveness | Liveness | `/health/live`（仅进程存活） |
| Worker Liveness | Liveness | 自定义 `IHealthCheck`（Outbox 不卡住、无 Kafka 致命错） |

---

## 7. Native AOT 编译与发布

> 规则：[`rules/native-aot.md`](file:///G:/wwwroot/github_fork/Full.NET/rules/native-aot.md) | ADR：[`ADR-0008`](file:///G:/wwwroot/github_fork/Full.NET/docs/architecture/adr/ADR-0008-api-native-aot-runtime-boundary.md)（Host.Api 运行边界）、[`ADR-0009`](file:///G:/wwwroot/github_fork/Full.NET/docs/architecture/adr/ADR-0009-host-api-native-aot-provider-runtime-boundary.md)（S3 / Kafka Replay Provider 边界）

### 7.1 启用条件与 csproj 配置

Host.Api 与 Host.Worker 通过 `FullNetPublishMode=NativeAot` 触发条件编译块；Migrator 不在 Native AOT 范围内。

> 文件：[`src/Hosts/Full.NET.Host.Api/Full.NET.Host.Api.csproj`](file:///G:/wwwroot/github_fork/Full.NET/src/Hosts/Full.NET.Host.Api/Full.NET.Host.Api.csproj) 与 [`src/Hosts/Full.NET.Host.Worker/Full.NET.Host.Worker.csproj`](file:///G:/wwwroot/github_fork/Full.NET/src/Hosts/Full.NET.Host.Worker/Full.NET.Host.Worker.csproj)

```xml
<PropertyGroup Condition="'$(FullNetPublishMode)' == 'NativeAot'">
  <PublishAot>true</PublishAot>
  <InvariantGlobalization>false</InvariantGlobalization>
  <!-- …其他 Trim/AOT 属性… -->
</PropertyGroup>

<ItemGroup Condition="'$(FullNetPublishMode)' == 'NativeAot'">
  <!-- API: 关闭 SqlClient 反射发现 + 关闭 SignalR 自定义 Awaitable + 保留 MemoryPack -->
  <RuntimeHostConfigurationOption Include="Microsoft.Data.SqlClient.EnableReflectionBasedAuthenticationProviderDiscovery" Value="false" Trim="true" />
  <RuntimeHostConfigurationOption Include="Microsoft.AspNetCore.SignalR.Hub.IsCustomAwaitableSupported" Value="false" Trim="true" />
  <TrimmerRootAssembly Include="MemoryPack.Core" />
  <RdXmlFile Include="NativeAotRoots.xml" />
</ItemGroup>
```

### 7.2 NativeAotRoots.xml

> 文件：[`src/Hosts/Full.NET.Host.Api/NativeAotRoots.xml`](file:///G:/wwwroot/github_fork/Full.NET/src/Hosts/Full.NET.Host.Api/NativeAotRoots.xml) 与 [`src/Hosts/Full.NET.Host.Worker/NativeAotRoots.xml`](file:///G:/wwwroot/github_fork/Full.NET/src/Hosts/Full.NET.Host.Worker/NativeAotRoots.xml)

两个宿主的 RD.XML 只保留 `Confluent.Kafka` 通过方法名反射绑定 `librdkafka` 所需的三个 Linux 候选类型：

```xml
<Directives xmlns="http://schemas.microsoft.com/netfx/2013/01/metadata">
  <Application>
    <Assembly Name="Confluent.Kafka">
      <Type Name="Confluent.Kafka.Impl.NativeMethods.NativeMethods" Dynamic="Required All" />
      <Type Name="Confluent.Kafka.Impl.NativeMethods.NativeMethods_Centos8" Dynamic="Required All" />
      <Type Name="Confluent.Kafka.Impl.NativeMethods.NativeMethods_Alpine" Dynamic="Required All" />
    </Assembly>
  </Application>
</Directives>
```

### 7.3 Worker 条件编译入口

`Worker Program.cs` 在 `FULLNET_AOT_COMPILE` 条件下调用 `WorkerDapperAotRegistration.Register()`，预先同步注册 Outbox / Retention / Shadow / Kafka 路径的 Dapper 物化器与参数绑定器（避免延迟注册与首个请求竞态）。

### 7.4 验证梯度与命令

| 变更范围 | 必跑命令 |
|----------|----------|
| Host.Api 可达代码 / AOT 条件 / JSON 源生成 / Dapper AOT | `pnpm test:aot:analyzers`、`pnpm test:dotnet:architecture --selection api-native-aot` |
| 发布闭包 / 第三方依赖 / RID / native 文件 / linker 配置 | 上面 + `pnpm test:aot:publish:linux` |
| 运行时路径 | + `pnpm test:aot:native:e2e`；S3 路径加 `pnpm test:aot:native:s3:e2e`；Kafka Replay 加 `pnpm test:aot:native:kafka-replay:e2e`；组合 Provider 加 `pnpm test:aot:native:providers:e2e` |

> 完成状态分四档：`Aot-analysis-clean` / `Aot-published` / `Native-provider-verified: s3` / `Native-provider-verified: kafka-replay`，必须按 ADR-0009 精确范围声明，不得外推到 Worker/Migrator Native AOT、完整 Kafka Delivery、CDC Relay、DLQ、Lag Observer 或 AWS 全凭据链。详细规则与失败诊断顺序见 [`rules/native-aot.md`](file:///G:/wwwroot/github_fork/Full.NET/rules/native-aot.md)。
