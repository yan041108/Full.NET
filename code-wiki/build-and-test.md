# 构建与测试指南

## 1. 环境要求

| 工具 | 版本要求 | 说明 |
|------|----------|------|
| .NET SDK | **10** (LTS) | `global.json` 锁定 |
| Node.js | **24** | `.nvmrc` 锁定，`<25` |
| pnpm | **10.26.0** | `package.json` 的 packageManager 字段；建议 `corepack enable` 启用 |
| Docker Desktop | 最新 | Windows 用 Linux Containers + WSL 2；或兼容 Docker Engine |
| Git | 任意 | 推荐支持长路径 + symlink |
| 浏览器 | Edge 130+ / Chrome 最新 | E2E 测试使用 Playwright |

```powershell
# 验证
dotnet --version            # 10.x.x
node --version              # v24.x.x
corepack enable
pnpm --version              # 10.26.0
docker --version
```

---

## 2. 后端还原与构建

### 2.1 解决方案文件

- `Full.NET.slnx` — 新式解决方案 XML（可读、可 diff）
- `Directory.Build.props` — 集中构建属性
- `Directory.Packages.props` — 集中 NuGet 包版本（`ManagePackageVersionsCentrally=true`）

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

| 项目 | 位置 | 框架 | 目标 | 执行时间 |
|------|------|------|------|----------|
| **Unit** | `tests/Full.NET.UnitTests/` | MSTest | 纯单元：无外部依赖，快速 | 秒级 |
| **Architecture** | `tests/Full.NET.ArchitectureTests/` | MSTest + NetArchTest.Rules | 依赖方向、命名、SQL 作用域、权限目录门禁 | 秒级 |
| **Compatibility** | `tests/Full.NET.CompatibilityTests/` | MSTest | Admin.NET 兼容适配器、旧协议兼容 | 秒级 |
| **Integration** | `tests/Full.NET.IntegrationTests/` | MSTest + Testcontainers | 真实 SQL Server/MySQL/Redis/Kafka 的端到端测试 | 分钟级 |
| **Generator** | `tests/Full.NET.GeneratorTests/` | MSTest | 代码生成器生成/回滚/所有权声明 | 秒级 |

### 3.2 Node.js 治理测试

| 测试集 | 位置 | 命令 | 职责 |
|--------|------|------|------|
| Governance | `tests/governance/*.test.mjs` | `pnpm test:governance` | 规则一致性、Layui 冻结、性能门禁 |
| Naming | `tests/naming/*.test.mjs` | `pnpm test:naming` | 数据库对象命名、UUID 存储 SQL、Pre-v1 映射 |
| OpenAPI | `tests/openapi/*.test.mjs` | `pnpm test:openapi` | OpenAPI 契约 + 破坏性变更检查 |
| SQL Safety | `tests/sql/*.test.mjs` | `pnpm test:sql-safety` | 无 WHERE 写、SELECT \*、破坏性 DDL |
| Helm | `tests/deployment/helm-contract.test.mjs` | `pnpm test:helm` | Chart 模板验证、发布顺序 |
| Performance | `tests/performance/*.test.mjs` | `pnpm test:performance-governance` | 前端包体预算、k6 负载配置文件契约 |

### 3.3 前端 E2E

| 套件 | 命令 | 说明 |
|------|------|------|
| Admin Parity | `pnpm test:e2e:admin` | Playwright + Mock API，验证 Vue/Layui 双端 UI 对等 |
| Admin Real Stack | `pnpm test:e2e:real` | Playwright + 真实 Testcontainers 启动的 API + DB |
| UniApp H5 | `pnpm test:e2e:uniapp` | H5 构建 + Edge 浏览器多语言冒烟 |

---

## 4. 常用 pnpm 脚本速查

```powershell
# ======== Governance（秒级，每次任务先跑）========
pnpm test:governance              # 规则一致性
pnpm test:naming                  # 命名合规
pnpm test:sql-safety              # SQL 安全
pnpm test:openapi                 # OpenAPI 契约

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
# 功能切片关闭后：
pnpm test:integration:affected -- --base $taskBase --phase slice
# 合并候选：
pnpm test:integration:affected -- --base $taskBase --phase merge

# ======== 手动选择 Integration 分片 ========
pnpm test:integration:smoke                # 最小冒烟：SQL Server 单 API 健康
pnpm test:integration:api:sqlserver        # API 聚焦 + SQL Server
pnpm test:integration:api:mysql            # API 聚焦 + MySQL
pnpm test:integration:migrations           # 迁移 + 幂等 + 恢复
pnpm test:integration:infrastructure       # 缓存/Outbox/CDC/可观测性
pnpm test:integration:full                 # 全部（仅限 main CI 并行分片）

# 验证分片不重复、不遗漏：
pnpm test:integration:partitions
pnpm test:integration:durations            # 输出 TRX 时长分析

# ======== 客户端 ========
pnpm install --frozen-lockfile
pnpm test:workspace                        # pnpm workspace 一致性
pnpm test:clients                          # 所有客户端 package 的单元测试
pnpm build:clients                         # 所有可构建客户端
pnpm test:bundle-budgets                   # 前端包体预算检查

# ======== 部署/治理 ========
pnpm test:helm                             # Helm Chart 契约
pnpm test:observability-deploy             # 可观测性部署契约
pnpm test:messaging-deploy                 # CDC/Kafka 部署契约
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
#   - 交互输入宿主管理员 Username + Password（Secret Parameter）
#   - Migrator 先迁移 + seed development → 成功退出
#   - API 在 https://localhost:5001/ 启动
#   - Worker 启动（初始没有真实订阅会提示但不崩溃）
#   - Aspire Dashboard: http://localhost:15200/ (日志、指标、追踪一体化)

# 4. Vue 管理端
cd ui/admin
pnpm install
pnpm dev  # http://localhost:5173/

# 5. Layui 管理端（存量冻结）
cd ui/admin-layui
pnpm install
pnpm dev  # http://localhost:5174/
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
