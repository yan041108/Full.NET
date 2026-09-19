# 构建与测试指南

## 1. 环境要求

| 工具 | 版本要求 | 说明 |
|------|----------|------|
| .NET SDK | **10.0.100** | [`global.json`](file:///G:/wwwroot/github_fork/Full.NET/global.json) 锁定：`rollForward=latestFeature`、`allowPrerelease=true`；MSTest SDK `4.3.2`；test runner `Microsoft.Testing.Platform` |
| Node.js | **24** | [`.nvmrc`](file:///G:/wwwroot/github_fork/Full.NET/.nvmrc) 仅含 `24`；`package.json` 的 `engines.node` 为 `>=24 <25` |
| pnpm | **10.26.0** | `package.json` 的 `packageManager` 字段；建议 `corepack enable` 启用 |
| Docker Desktop | 最新 | Windows 用 Linux Containers + WSL 2；或兼容 Docker Engine |
| Git | 任意 | 推荐支持长路径 + symlink |
| 浏览器 | Edge 130+ / Chrome 最新 | E2E 测试使用 Playwright |

```powershell
# 验证
dotnet --version            # 10.0.100 或更高 feature
node --version              # v24.x.x
corepack enable
pnpm --version              # 10.26.0
docker --version
```

---

## 2. 后端还原与构建

### 2.1 解决方案文件

- [`Full.NET.slnx`](file:///G:/wwwroot/github_fork/Full.NET/Full.NET.slnx) — 新式解决方案 XML（可读、可 diff）
- [`Directory.Build.props`](file:///G:/wwwroot/github_fork/Full.NET/Directory.Build.props) — 集中构建属性：`TargetFramework=net10.0`、`Nullable=enable`、`TreatWarningsAsErrors=true`、`AnalysisLevel=latest`、`EnforceCodeStyleInBuild=true`、`Deterministic=true`、`NuGetAudit=true`/`NuGetAuditMode=all`；CI 时启用 `ContinuousIntegrationBuild`
- [`Directory.Packages.props`](file:///G:/wwwroot/github_fork/Full.NET/Directory.Packages.props) — 集中 NuGet 包版本（`ManagePackageVersionsCentrally=true`）

### 2.2 标准构建

```powershell
dotnet restore Full.NET.slnx
dotnet build   Full.NET.slnx --configuration Release --no-restore
```

构建产物输出默认 `artifacts/bin/`。

### 2.3 构建单宿主

```powershell
dotnet publish src/Hosts/Full.NET.Host.Api/Full.NET.Host.Api.csproj `
  -c Release -o publish/api
dotnet publish src/Hosts/Full.NET.Host.Worker/Full.NET.Host.Worker.csproj `
  -c Release -o publish/worker
dotnet publish src/Hosts/Full.NET.Host.Migrator/Full.NET.Host.Migrator.csproj `
  -c Release -o publish/migrator
```

---

## 3. 测试套件体系

### 3.1 .NET 测试项目

> 全部基于 `MSTest.Sdk`（`global.json` 锁定 `MSTest.Sdk=4.3.2`、test runner 为 `Microsoft.Testing.Platform`）；CI 与本地均使用同一组 `dotnet test` 调用，差异仅在筛选器与 Testcontainers 启停。

| 项目 | 位置 | 框架与依赖 | 目标 |
|------|------|----------|------|
| **Unit** | [`tests/Full.NET.UnitTests/`](file:///G:/wwwroot/github_fork/Full.NET/tests/Full.NET.UnitTests/) | MSTest + `NSubstitute` + `Microsoft.CodeAnalysis.CSharp` | 纯单元：无外部依赖；包含 `CodeGeneration/` 代码生成器测试、`Ids/` UUID 生成器、`Messaging/` IntegrationEventHandler 注册器等 |
| **Architecture** | [`tests/Full.NET.ArchitectureTests/`](file:///G:/wwwroot/github_fork/Full.NET/tests/Full.NET.ArchitectureTests/) | MSTest + `NetArchTest.Rules` + `Microsoft.CodeAnalysis.CSharp` | 依赖方向、命名、SQL 作用域、Native AOT 静态闭包规则、MemoryPack 受控协议、权限目录门禁 |
| **Compatibility** | [`tests/Full.NET.CompatibilityTests/`](file:///G:/wwwroot/github_fork/Full.NET/tests/Full.NET.CompatibilityTests/) | MSTest + `FrameworkReference=Microsoft.AspNetCore.App` | Admin.NET 兼容适配器、旧协议兼容 |
| **Integration** | [`tests/Full.NET.IntegrationTests/`](file:///G:/wwwroot/github_fork/Full.NET/tests/Full.NET.IntegrationTests/) | MSTest + `Testcontainers.MsSql/MySql/Kafka/Redis` + `Microsoft.AspNetCore.Mvc.Testing` + `AWSSDK.S3` + `Microsoft.AspNetCore.SignalR.Client` | 真实 SQL Server/MySQL/Redis/Kafka/MinIO 的端到端测试，包含 Migration 恢复、Outbox/CDC、Native AOT E2E、OIDC 中心等子集 |

> 原 `Full.NET.GeneratorTests` 已合并入 `Full.NET.UnitTests/CodeGeneration/` 子目录，不再作为独立项目存在。

### 3.2 Node.js 治理测试

| 测试集 | 位置 | 命令 | 职责 |
|--------|------|------|------|
| Governance | `tests/governance/*.test.mjs` | `pnpm test:governance` | 规则一致性、Layui 冻结、性能门禁 |
| Naming | `tests/naming/*.test.mjs` + `tests/database/uuid-storage-contract.test.mjs` | `pnpm test:naming` | 数据库对象命名、UUID 存储 SQL、Pre-v1 映射 |
| Pre-v1 Naming | `tests/naming/pre-v1-name-map.test.mjs` | `pnpm test:pre-v1-naming` | 旧 → 新名称映射 |
| UUID Storage | `tests/database/uuid-storage-contract.test.mjs` | `pnpm test:uuid-storage` | UUID 存储契约（双库） |
| OpenAPI | `tests/openapi/*.test.mjs` | `pnpm test:openapi` | OpenAPI 契约 |
| OpenAPI Breaking | `scripts/openapi/check-openapi-breaking-changes.mjs` | `pnpm test:openapi:breaking` | 破坏性变更检查 |
| OpenAPI Client Generation | `tests/openapi/client-generation-readiness.test.mjs` | `pnpm test:openapi:client-generation` | 客户端生成就绪度 |
| SQL Safety | `tests/sql/*.test.mjs` | `pnpm test:sql-safety` | 无 WHERE 写、SELECT \*、破坏性 DDL |
| Helm | `tests/deployment/helm-contract.test.mjs` + `release-order-contract.test.mjs` + `container-image-contract.test.mjs` | `pnpm test:helm` | Chart 模板验证、发布顺序、镜像标签 |
| Container Images | `scripts/testing/run-container-image-contracts.mjs` | `pnpm test:container-images` | 镜像 OCI 标签与安全上下文契约 |
| Performance | `tests/performance/*.test.mjs` | `pnpm test:performance-governance` | 前端包体预算、k6 负载配置文件契约 |
| Load Profiles | `eng/load/validate-profiles.mjs` + `tests/performance/load-profile-contract.test.mjs` | `pnpm test:load-profiles` | k6 场景配置文件契约 |
| Localization | `tests/localization-contract.test.mjs` | `pnpm test:localization` | BCP 47 / 稳定机器码契约 |
| Templates | `tests/templates/*.test.mjs` | `pnpm test:templates` | 模板契约 |
| Skills | `tests/skills/validate_project_skills.py` | `pnpm test:skills` | Skill 元数据校验（Python） |
| Tooling | `tests/testing/*.test.mjs` | `pnpm test:integration:tooling` | 测试脚本自身一致性 |

### 3.3 前端 E2E

| 套件 | 命令 | 说明 |
|------|------|------|
| Admin Parity | `pnpm test:e2e:admin` | Playwright + Mock API，验证 Vue/Layui 双端 UI 对等 |
| Layui Frozen | `pnpm test:e2e:layui-frozen` | Layui 冻结增量回归 |
| Admin Real Stack | `pnpm test:e2e:real` | Playwright + 真实 Testcontainers 启动的 API + DB |
| Real Stack OIDC Center | `pnpm test:e2e:real:oidc-center` | OIDC 中心登录真实栈 |
| Real Stack MySQL | `pnpm test:e2e:real:mysql` | MySQL 真实栈 |
| Real Stack Provisioner | `pnpm test:e2e:provisioner` | 真实栈 Provisioner 流程 |
| UniApp H5 | `pnpm test:e2e:uniapp` | H5 构建 + Edge 浏览器多语言冒烟 |
| UniApp H5 Real | `pnpm test:e2e:uniapp:real` | H5 真实栈冒烟 |

### 3.4 Native AOT 测试梯度

> 完整规则与失败诊断顺序见 [`rules/native-aot.md`](file:///G:/wwwroot/github_fork/Full.NET/rules/native-aot.md)；测试矩阵见 [`eng/testing/test-matrix.json`](file:///G:/wwwroot/github_fork/Full.NET/eng/testing/test-matrix.json)。

Host.Api 与 Host.Worker 的 Native AOT 路径分四档验证，命令分别落在 `scripts/testing/run-*.mjs`：

| 档位 | API 命令 | Worker 命令 | 目标 |
|------|----------|-----------|------|
| 分析器 | `pnpm test:aot:analyzers` | `pnpm test:aot:worker:analyzers` | AOT/Trim 分析无未处理告警 |
| 架构选择 | `pnpm test:dotnet:architecture --selection api-native-aot` | — | Native AOT 静态闭包规则、MemoryPack 受控协议 |
| Linux 发布 | `pnpm test:aot:publish:linux` | `pnpm test:aot:worker:publish:linux` | 真实 `linux-x64` 原生发布 + manifest 检查 |
| 原生 E2E | `pnpm test:aot:native:e2e` | `pnpm test:aot:worker:native:e2e` | 原生可执行文件启动 + 关键外部进程 E2E |

按运行路径分组的 Provider 原生 E2E（API）：

| Provider / 路径 | 命令 |
|-----------------|------|
| Notifications | `pnpm test:aot:native:notifications:e2e` |
| Settings / Jobs | `pnpm test:aot:native:settings-jobs:e2e` |
| S3（ADR-0009 范围） | `pnpm test:aot:native:s3:e2e` |
| Kafka Replay（ADR-0009 范围） | `pnpm test:aot:native:kafka-replay:e2e` |
| OIDC | `pnpm test:aot:native:oidc:e2e` |
| 组合 Provider | `pnpm test:aot:native:providers:e2e` |

> `eng/testing/test-matrix.json` 为最低发现数、超时、RID、`minimumExecutableBytes` 等机器事实源；规则和本节文案均不复制可变数值。完成状态分四档：`Aot-analysis-clean` / `Aot-published` / `Native-provider-verified: s3` / `Native-provider-verified: kafka-replay`，必须按 ADR-0008/0009 精确范围声明，不得外推到 Worker/Migrator Native AOT、完整 Kafka Delivery、CDC Relay、DLQ、Lag Observer 或 AWS 全凭据链。

---

## 4. 常用 pnpm 脚本速查

> 完整列表见 [`package.json`](file:///G:/wwwroot/github_fork/Full.NET/package.json)；下方按场景分组。

```powershell
# ======== Governance（秒级，每次任务先跑）========
pnpm test:governance              # 规则一致性
pnpm test:naming                  # 命名合规（含 UUID 存储）
pnpm test:pre-v1-naming           # Pre-v1 旧 → 新名称映射
pnpm test:uuid-storage            # UUID 二进制存储契约
pnpm test:sql-safety              # SQL 安全
pnpm test:openapi                 # OpenAPI 契约
pnpm test:openapi:breaking        # OpenAPI 破坏性变更
pnpm test:localization            # BCP 47 / 稳定机器码
pnpm test:templates               # 模板契约
pnpm test:skills                  # Skill 元数据校验（Python）
pnpm test:integration:tooling      # 测试脚本自身一致性

# ======== .NET 单元/架构（秒级）========
pnpm test:dotnet:unit -- --no-build
pnpm test:dotnet:architecture -- --no-build
pnpm test:dotnet:compatibility -- --no-build

# ======== 任务影响集集成测试（推荐日常流程）========
$taskBase = git rev-parse HEAD     # 1. 记录任务基线
# ... 代码修改 ...
pnpm test:task:start -- --task-id my-feature-001   # 2. 创建快照（工作区脏时）
pnpm test:integration:affected:plan -- --base $taskBase --phase inner
#  → 输出将要运行的受影响测试
pnpm test:integration:affected -- --base $taskBase --phase inner
#  → 实际运行（按 UID 去重、合并为一次进程）
# Phase 快捷方式（默认 base = 工作区状态）：
pnpm test:inner                    # = run-affected-integration.mjs --phase inner
pnpm test:slice                    # = run-affected-integration.mjs --phase slice
# 功能切片关闭后：
pnpm test:integration:affected -- --base $taskBase --phase slice
# 合并候选：
pnpm test:integration:affected -- --base $taskBase --phase merge

# ======== 手动选择 Integration 分片 ========
pnpm test:integration:smoke                # 最小冒烟：SQL Server 单 API 健康
pnpm test:integration:api:sqlserver        # API 聚焦 + SQL Server
pnpm test:integration:api:mysql            # API 聚焦 + MySQL
pnpm test:integration:migrations           # 迁移 + 幂等 + 恢复（含 Migration229/101~119 等恢复测试）
pnpm test:integration:infrastructure       # 缓存/Outbox/CDC/可观测性
pnpm test:integration:messaging-heavy      # Outbox/Kafka/CDC 重负载
pnpm test:integration:full                 # 全部（仅限 main CI 并行分片）

# 验证分片不重复、不遗漏：
pnpm test:integration:partitions
pnpm test:integration:durations            # 输出 TRX 时长分析

# ======== Native AOT（API + Worker）========
pnpm test:aot:analyzers                    # API AOT 分析器
pnpm test:aot:worker:analyzers             # Worker AOT 分析器
pnpm test:aot:publish:linux                # API linux-x64 原生发布
pnpm test:aot:worker:publish:linux         # Worker linux-x64 原生发布
pnpm test:aot:native:e2e                   # API 原生 E2E
pnpm test:aot:worker:native:e2e            # Worker 原生 E2E
# Provider 原生 E2E（API）：
pnpm test:aot:native:notifications:e2e
pnpm test:aot:native:settings-jobs:e2e
pnpm test:aot:native:s3:e2e
pnpm test:aot:native:kafka-replay:e2e
pnpm test:aot:native:oidc:e2e
pnpm test:aot:native:providers:e2e         # 组合 Provider

# ======== 客户端 ========
pnpm install --frozen-lockfile
pnpm test:workspace                        # pnpm workspace 一致性
pnpm test:clients                          # 所有客户端 package 的单元测试（排除 E2E 与冻结的 layui）
pnpm build:clients                         # 所有可构建客户端（排除冻结的 layui）
pnpm test:bundle-budgets                   # 前端包体预算检查
pnpm openapi:client:snapshot               # OpenAPI 客户端快照
pnpm openapi:client:generate               # OpenAPI 客户端生成

# ======== 部署/治理 ========
pnpm test:helm                             # Helm Chart 契约 + 发布顺序 + 镜像标签
pnpm test:container-images                 # 镜像 OCI 标签与安全上下文
pnpm test:observability-deploy             # 可观测性部署契约
pnpm test:messaging-deploy                 # CDC/Kafka 部署契约
pnpm test:load-profiles                    # k6 负载配置文件契约
pnpm audit:dotnet                          # NuGet 漏洞审计
pnpm audit:clients                         # NPM 漏洞审计
```

> **唯一权威测试矩阵**：[`eng/testing/test-matrix.json`](file:///G:/wwwroot/github_fork/Full.NET/eng/testing/test-matrix.json)
> 定义了各类集成测试的最低发现数、超时、分片策略。**本地任务不得运行完整集合**，只运行受影响子集。

---

## 5. 集成测试原理

### 5.1 Testcontainers 按需启动

```
集成测试夹具启动流程：
  1. 解析选择器（SqlServer / MySql / Redis / Kafka / All）
  2. 只拉取选择器命中的容器镜像
     ├── SqlServer 聚焦 → 不启动 MySQL/Redis
     ├── MySQL 聚焦 → 不启动 SQL Server
     └── Infrastructure → 启动 Redis + Kafka
  3. 容器健康检查通过后，分配连接串
  4. 运行 Migrator（迁移 + 指定 Seed Profile）
  5. 创建 Test Server（WebApplicationFactory）
  6. 执行测试用例
  7. 全部测试结束后释放所有容器
```

### 5.2 影响集选择算法 (`run-affected-integration.mjs`)

```
输入：--base <基线提交> 或 --snapshot <task-id>
  1. git diff --name-only <基线> → 变更文件列表
  2. 映射 → 受影响模块：
     ├── src/Modules/Identity/* → Identity 相关测试 + 跨模块消费者
     ├── src/BuildingBlocks/Data.Dapper/* → 全部 Integration + Unit
     ├── migrations/* → migrations 分片 + 全部 API 分片
     ├── ui/admin/src/api/users.ts → E2E + 相关 Vue 单测
     └── scripts/testing/*.mjs → tooling 测试
  3. 去重 UID → 生成 dotnet test --filter
  4. 输出执行计划，或实际执行
```

### 5.3 集成测试 Phase

| Phase | 用途 | 严格度 |
|-------|------|--------|
| `inner` | 开发内循环 | 只跑选择器直接命中 |
| `slice` | 功能切片关闭 | 命中 + 直接消费者 + 相关分片 |
| `merge` | 合并候选门禁 | full + 破坏性变更补充用例 |

---

## 6. 基准与负载

### 6.1 BenchmarkDotNet 基准

```powershell
# 审计查询 10 万行双库基准
dotnet run --project benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj `
  -c Release -- audit-query

# SQL Server 查询计划 A/B 对比
dotnet run --project benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj `
  -c Release -- audit-query --mode sqlserver-plan-ab --providers sqlserver
```

### 6.2 k6 负载测试

```powershell
# 验证负载配置文件
pnpm test:load-profiles

# 运行实际负载（需要已部署的 API，非日常门禁）
k6 run eng/load/k6/scenarios/read-heavy.js `
  -e BASE_URL=https://staging.example.com `
  -e VUSERS=500 -e DURATION=5m
```

负载配置文件：`eng/load/profiles/{2k,5k,10k,soak}.json`。

---

## 7. 本地快速启动

```powershell
# 1. 还原
dotnet restore Full.NET.slnx
pnpm install --frozen-lockfile

# 2. 跑一轮基础测试（验证环境）
pnpm test:dotnet:unit
pnpm test:naming
pnpm test:sql-safety

# 3. Aspire 本地编排（启动 SQL Server + Redis + Migrator + API + Worker）
dotnet run --project src/Hosts/Full.NET.AppHost/Full.NET.AppHost.csproj
# 首次运行：
#   - 交互输入宿主管理员 Username + Password（Secret Parameter）+ UUID 契约维护参数
#   - Migrator 先迁移 + seed development → 成功退出（WaitFor(database)）
#   - API 在 http://localhost:5149/ 启动（launchSettings.json；与 ui/admin 的 vite proxy 默认 target 一致）
#   - Worker 启动（WaitForCompletion(migrator)；初始没有真实订阅会提示但不崩溃）
#   - Aspire Dashboard: http://localhost:15200/ (日志、指标、追踪一体化)

# 4. Vue 管理端
cd ui/admin
pnpm install
pnpm dev  # http://localhost:5173/（vite --host localhost --port 5173）

# 5. Layui 管理端（存量冻结，自 2026-08-02 起）
cd ui/admin-layui
pnpm install
pnpm dev  # http://localhost:5174/（vite --host localhost --port 5174）
```

---

## 8. 常见诊断命令

```powershell
# 检查 NuGet 包引用问题
dotnet list package --outdated
dotnet list package --vulnerable
pnpm audit:dotnet

# 检查 NPM 包
pnpm audit
pnpm audit:clients

# 检查 Git diff 格式
git diff --check   # 无尾随空格、正确换行

# 运行架构测试定位违规
pnpm test:dotnet:architecture -- --logger "console;verbosity=detailed"

# Integration 执行时间分析
pnpm test:integration:durations
```

---

## 9. CI 工作流

> 目录：[`.github/workflows/`](file:///G:/wwwroot/github_fork/Full.NET/.github/workflows)

| 工作流文件 | 触发 | 职责 |
|-----------|------|------|
| `ci.yml` | push 到 `main` + pull_request | 客户端构建、`pnpm test:governance`、`test:skills`、`test:performance-governance`、`test:naming`、`test:sql-safety`、`test:helm`、`test:observability-deploy`、`test:workspace`、`test:localization`、`test:openapi`、`openapi:client:snapshot --check --offline`、`test:openapi:breaking`（PR 或 push 含前提交时对比 base ref）、客户端 licenses 审计 |
| `api-native-aot-linux.yml` | 定期 + 显式触发 | Host.Api Native AOT 分析器、`linux-x64` publish、原生 E2E（核心路径 + S3 + Kafka Replay + Providers + Notifications + Settings/Jobs + OIDC） |
| `worker-native-aot-linux.yml` | 定期 + 显式触发 | Host.Worker Native AOT 分析器、`linux-x64` publish、Worker 原生 E2E |
| `jobs-capacity.yml` | 定期 | Jobs 模块容量与积压验证 |
| `kafka-capacity.yml` | 定期 | Kafka / Outbox 容量与 Lag 验证 |
| `sqlserver-cdc-nightly.yml` | 每夜 | SQL Server CDC Shadow 比对与切流回退门禁 |

> `ci.yml` 固定使用 `pnpm@10.26.0`、Node `24`、Python `3.12`（用于 `pnpm test:skills`），Helm `v3.16.4`；非 Linux discovery skip 不能替代原生 AOT 状态升级，所有 Native AOT 状态变更必须引用 fresh Linux CI run、提交 SHA、步骤结论与未验证边界（见 [`rules/native-aot.md`](file:///G:/wwwroot/github_fork/Full.NET/rules/native-aot.md) §2.3、§8.5）。
