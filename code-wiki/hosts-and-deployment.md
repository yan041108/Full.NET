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

```csharp
// Program.cs (简化示例)
var builder = WebApplication.CreateBuilder(args);

// 1. 服务默认配置（Serilog + OTel + Health + 限流 + 安全头）
builder.AddServiceDefaults();

// 2. BuildingBlocks
builder.Services
    .AddFullNetCaching(builder.Configuration)
    .AddFullNetDataAccess(builder.Configuration)     // Dapper + DbSession + ScopeGuard
    .AddFullNetModularity()                            // CQRS 分发 + 模块目录
    .AddFullNetLocalization()
    .AddFullNetHosting(builder.Configuration)          // 异常处理/日志/OpenAPI/代理
    .AddFullNetSignalR(builder.Configuration)
    .AddFullNetKafkaMessaging(builder.Configuration);  // 若启用 Kafka

// 3. Composition：所有业务模块（按 Host Profile 选择）
builder.Services.AddFullNetModules(builder.Configuration, FullNetHostProfile.Api);

// 4. 兼容：Admin.NET 信封响应适配器（按需）
// builder.Services.AddAdminNetCompatibility();

var app = builder.Build();

app.UseFullNetServiceDefaults();                   // 中间件管道
app.MapHealthChecks("/health");
app.MapFullNetModuleEndpoints();                    // 所有业务模块 Endpoint
app.MapFullNetNotificationHub();                    // SignalR Hub
app.Run();
```

### 1.2 Worker 宿主注册

```csharp
// Program.cs
var builder = Host.CreateDefaultBuilder(args);

// 只装配后台能力，不装入 HTTP/认证/完整模块
builder.ConfigureServices((ctx, services) =>
{
    services.AddFullNetServiceDefaults(ctx.Configuration)
            .AddFullNetDataAccess(ctx.Configuration)
            .AddFullNetModularity()
            .AddFullNetCaching(ctx.Configuration)
            .AddFullNetKafkaMessaging(ctx.Configuration);

    // HostProfile.Worker：每个模块贡献 AddBackgroundServices
    services.AddFullNetModules(ctx.Configuration, FullNetHostProfile.Worker);

    // Worker 特有后台服务
    services.AddHostedService<OutboxProcessor>();
    services.AddHostedService<OutboxRetentionProcessor>();
    services.AddHostedService<KafkaConsumerWorker>();
    services.AddHostedService<AuditingRetentionRunner>();
    services.AddHostedService<DeletedHostFileBlobCleanupRunner>();
});
```

### 1.3 Migrator 宿主工作流

```csharp
// MigratorWorkflow.cs
public async Task<int> RunAsync(string[] args, CancellationToken ct)
{
    // 1. 只装配 Migration Profile（最小闭包，无 HTTP/认证）
    services.AddFullNetModules(config, FullNetHostProfile.Migration);

    // 2. 确定 Seed Profile
    //    Production 默认只迁移，不播种
    //    --seed baseline   : 生产安全基线（仅宿主管理员 + 菜单目录）
    //    --seed development: Baseline + 本地租户 + 测试用户
    //    --seed test       : Development + 测试夹具
    //    --seed demo       : Test + 示例业务数据

    // 3. 数据库执行租约（防止多个 Migrator 并发执行）
    //    SQL Server: sp_getapplock
    //    MySQL: GET_LOCK

    // 4. DbUp 迁移脚本执行（SQL Server 或 MySQL）
    await _migrationRunner.RunAsync(ct);

    // 5. 种子数据（按 Profile 过滤 Contributor，按依赖拓扑排序）
    if (seedProfile != SeedProfile.None)
        await _seedOrchestrator.ContributeAsync(seedProfile, ct);

    // 6. 输出执行审计（脚本列表、Contributor 结果、耗时）
}
```

---

## 2. Docker 镜像

### 2.1 Dockerfile 结构

> 文件：[`eng/docker/Dockerfile`](file:///G:/wwwroot/github_fork/Full.NET/eng/docker/Dockerfile)

**多阶段 + 多最终目标**：

```dockerfile
# ======== Build Stage（单一 SDK 构建三个宿主）========
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props ./
COPY contracts/ ./contracts/
COPY src/ ./src/
RUN dotnet restore Host.Api + Host.Worker + Host.Migrator
RUN dotnet publish 每个宿主 → /app/publish/{api|worker|migrator}

# ======== Final Target: API ========
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS api
COPY --from=build /app/publish/api .
ENV ASPNETCORE_URLS=http://+:8080
USER $APP_UID                                    # 非 root 运行
ENTRYPOINT ["dotnet", "Full.NET.Host.Api.dll"]

# ======== Final Target: Worker ========
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS worker
# ... 类似

# ======== Final Target: Migrator ========
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS migrator
# ... 类似
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

**安全要点**：
- 非 root 用户（UID/GID 1654）
- 只读根文件系统
- 全部 capability DROP
- `DOTNET_EnableDiagnostics=0`（生产禁用诊断）
- 镜像标签 + `org.opencontainers.image.*` 标签

---

## 3. Helm Chart 生产部署

### 3.1 Chart 基础信息

> 目录：[`deploy/helm/fullnet/`](file:///G:/wwwroot/github_fork/Full.NET/deploy/helm/fullnet)

```yaml
# Chart.yaml
apiVersion: v2
name: fullnet
type: application
version: 0.1.0
appVersion: "1.0.0"
# 不安装数据库、Redis、S3、可观测性后端、WAF、分布式限流
```

### 3.2 生产角色分离（三 Release）

> 生产 `production=true` 时禁止同一 Release 同时启用多个角色。

```bash
# Release 1: Migrator（先运行，Job 成功后再升级 API/Worker）
helm upgrade --install fullnet-migrator deploy/helm/fullnet \
  --set production=true \
  --set roles.migrator=true \
  --set database.provider=SqlServer \
  --values ci/values-role-migrator.yaml

# Release 2: API
helm upgrade --install fullnet-api deploy/helm/fullnet \
  --set production=true \
  --set roles.api=true \
  --set api.replicaCount=3 \
  --values ci/values-role-api.yaml

# Release 3: Worker
helm upgrade --install fullnet-worker deploy/helm/fullnet \
  --set production=true \
  --set roles.worker=true
```

### 3.3 发布顺序

```
部署顺序（不可逆，Helm 契约测试强制执行）：
  1. Migrator Job ──等待成功──►
       │ 迁移 + Baseline Seed
       ▼
  2. API Deployment (HPA 滚动升级，PDB minAvailable=2)
       │ 等待 ReadyReplicas >= 旧版本
       ▼
  3. Worker Deployment
       │ Outbox/Retention/Kafka Consumer
       ▼
  4. 健康检查 / 回滚就绪
```

### 3.4 关键 values.yaml 配置

```yaml
database:
  provider: SqlServer            # SqlServer | MySql
  connectionSecretName: fullnet-database
  connectionSecretKey: connectionString

cache:
  redisSecretName: fullnet-cache-redis
  redisSecretKey: connectionString

realtime:                         # SignalR Redis Backplane
  redisSecretName: fullnet-realtime-redis
  requireSessionAffinity: true    # 多实例场景：会话亲和

dataProtection:                   # ASP.NET Data Protection 密钥共享
  applicationName: Full.NET
  existingClaimName: ""           # 优先 RWX PVC；或 K8s Secret + 证书
  certificateSecretName: fullnet-dp-cert

api:
  replicaCount: 3
  resources:
    requests: { cpu: 250m, memory: 512Mi }
    limits:   { cpu: "2",  memory: 2Gi }
  hpa:
    enabled: true
    minReplicas: 3
    maxReplicas: 12
    targetCPUUtilizationPercentage: 70
  pdb:
    enabled: true
    minAvailable: 2
  ingress:
    enabled: true
    className: nginx
    host: fullnet.example.com
    tlsSecretName: fullnet-tls
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
1. 配置 `Owner=ShadowCdcKafka` → 两边同时发布，事件对比校验
2. 对比通过 → 切换 `Owner=CdcKafka`（CAS 原子 + PreviousOwner 检查）
3. 旧 Worker 停止领取该流，CDC Relay 开始捕获
4. 监控 Consumer Lag、最老消息年龄、重试队列深度
5. 回滚：反向从 `CdcKafka → LegacyPolling` 同样 CAS

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
