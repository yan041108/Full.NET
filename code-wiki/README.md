# Full.NET Code Wiki

> Full.NET 代码知识库 - 面向开发者的结构化项目文档

## 概述

Full.NET 是面向产品研发和项目快速交付的 .NET 10 基础框架。项目以**强化型模块化单体**作为默认部署形态，吸收 eShop 的边界与可观测性思路，并以 Admin.NET 的业务能力范围作为长期功能对标目标。

本 Wiki 提供代码层面的完整知识库，帮助开发者快速理解项目架构、模块职责、关键实现和开发流程。

---

## 文档导航

### 一、架构设计

| 文档 | 说明 |
|------|------|
| [架构总览](./architecture-overview.md) | 总体架构、分层设计、模块通信机制、部署形态 |
| [关键接口与类详解](./key-interfaces-and-classes.md) | 核心抽象、CQRS、模块系统、数据访问的类型定义 |
| [ADR 索引](../docs/architecture/adr/) | 架构决策记录（ADR-0001 ~ ADR-0011；含 Native AOT、Outbox/CDC/Kafka、UUID v7、OIDC SSO 等） |
| [命名规范摘要](./naming-conventions-summary.md) | 数据库、C#、HTTP/JSON、配置键的统一命名规则 |

### 二、基础设施层 (BuildingBlocks)

| 文档 | 说明 |
|------|------|
| [基础设施层总览](./building-blocks.md) | 所有 BuildingBlocks 项目的职责、依赖和关键能力 |
| [构建与测试指南](./build-and-test.md) | 环境要求、还原、构建、测试套件、集成测试分片、Native AOT 测试梯度 |
| [宿主与部署](./hosts-and-deployment.md) | API/Worker/Migrator/AppHost 四宿主、Docker 镜像、Helm Chart、K8s 部署、Native AOT 编译与发布 |
| [Dapper SQL 来源与门禁](./dapper-sql-sources.md) | 手写 SQL、Global 目录、CodeGeneration、SqlDataScope、SQL 安全门禁 |

### 三、业务模块 (Modules)

| 文档 | 说明 | 状态 |
|------|------|------|
| [模块系统总览](./modules-overview.md) | 模块注册机制、依赖图、Host Profile、垂直切片结构 | |
| [Identity 身份模块](./module-identity.md) | 用户、角色、菜单、授权、认证会话、API Key、TOTP、OIDC SSO | Build-verified |
| [核心业务模块详解](./modules-core.md) | Tenancy / Organization / Settings / Auditing / Jobs / Files / Document / Messaging / CodeGeneration / SerialNumbers / Notifications / Workflow / Ai / Calendar / Cryptography / DataApproval / GoView / ImportExport / K3Cloud / Mqtt / ObservabilityAdmin / Ocr / Payments / Platform / Printing / Regions / Reporting / Webhooks / EnterpriseRequest | Build-verified |

> 上述除 Identity 单独成文外，其他模块统一合并至 `modules-core.md`，避免维护分散的 `module-*.md` 文件。

### 四、前端与客户端

| 目录 | 说明 |
|------|------|
| `ui/admin` | Vue 3 + TypeScript + Element Plus 主管理端（持续交付线；`vite --port 5173`） |
| `ui/admin-layui` | Layui 2 原生管理端（自 2026-08-02 起存量冻结；`vite --port 5174`） |
| `clients/uniapp` | uni-app Vue 3 跨端（H5、微信小程序、支付宝小程序） |
| `clients/flutter` | Flutter 客户端 |
| `packages/admin-form-designer` | Admin 表单设计器 |
| `packages/admin-i18n` | 管理端 i18n 基础设施 |
| `packages/client-contracts` | TypeScript OpenAPI 契约与客户端 SDK |
| `packages/design-tokens` | 跨端设计令牌（CSS Variables） |

---

## 快速参考

### 核心技术栈

| 层级 | 技术选型 |
|------|----------|
| 运行时 | .NET 10.0.100 LTS / ASP.NET Core / C# (Nullable 开启) |
| 数据访问 | Dapper + 显式 SQL (无 EF Core) |
| 数据库 | SQL Server / MySQL (双提供程序一等支持) |
| 迁移引擎 | DbUp + 可审查 SQL 脚本（`{NNN}_{PascalCasePurpose}.sql`） |
| 缓存 | FusionCache (L1 内存 + L2 Redis + Backplane) |
| 消息 | 事务 Outbox + MessagePack / CDC Kafka / 消费 Inbox |
| 实时 | SignalR + 可选 Redis Backplane |
| 序列化 | System.Text.Json (HTTP) / MessagePack (内部事件) |
| 验证 | FluentValidation |
| 日志 | Serilog + 有界异步输出 |
| 可观测性 | OpenTelemetry + 健康检查 |
| 编排 | .NET Aspire AppHost (本地) / Helm (生产 K8s) |
| 前端 | Vue 3 + TypeScript + Vite + Element Plus |
| 测试 | MSTest SDK 4.3.2 + Microsoft.Testing.Platform + Testcontainers + Playwright |
| 包管理 | pnpm 10.26.0 (Node 24) / NuGet Central Package Management |

### 目录结构速查

```text
Full.NET/
├── src/
│   ├── BuildingBlocks/          # 基础设施层（不依赖业务模块）
│   ├── Modules/                 # 业务模块（拥有独立表和契约）
│   ├── Composition/             # 组合根（唯一可引用具体模块的位置）
│   ├── Hosts/                   # 运行宿主（API/Worker/Migrator/AppHost）
│   ├── Compatibility/          # 兼容适配层（Admin.NET 信封等）
│   ├── Generators/              # 源代码生成器
│   └── Tools/                   # CLI 工具（如 CodeGeneration.Cli）
├── ui/                          # 管理后台前端（admin Vue 3 / admin-layui 冻结）
├── clients/                     # 移动端客户端（uniapp / flutter）
├── packages/                    # NPM 共享包（admin-form-designer / admin-i18n / client-contracts / design-tokens）
├── samples/                     # 模块样板（如 enterprise-request）
├── templates/                   # 项目脚手架模板
├── tests/                       # .NET 测试（Unit/Architecture/Compatibility/Integration）+ Node 治理测试
├── benchmarks/                  # BenchmarkDotNet 基准
├── deploy/                      # Helm Chart、CDC/Kafka Compose、可观测性
├── docs/                        # 规格、ADR、路线图、运维手册
├── rules/                       # 项目开发规则
├── contracts/                   # OpenAPI、SQL 安全、命名、架构债务等治理契约
│   ├── architecture/            # global-sql-statements / module-table-access-debt 等
│   ├── database/                # object-comments / object-comment-overrides
│   ├── naming/                  # fullnet-naming-profile / pre-v1-name-map / naming-debt
│   ├── openapi/                 # vue-client-coverage / tenant-subscriptions 等
│   └── sql-safety/              # SQL 破坏性变更豁免 waivers
├── scripts/                     # 测试、治理、命名、SQL 校验脚本
├── eng/                         # Dockerfile、加载测试配置、CI 配置、testing test-matrix.json
├── localization/                # 全栈多语言目录
└── code-wiki/                   # 本 Wiki 文档（你正在阅读的目录）
```

> 构建产物目录（`artifacts/`、`BenchmarkDotNet.Artifacts/`、`TestResults/`、`arch-out/`、`node_modules/`）与运行时数据目录（`App_Data/`）为生成物，不在上方列出。

---

## 开发必读

1. **开始任何任务前**必须阅读 [AGENTS.md](../AGENTS.md) 和 [`rules/`](../rules/README.md) 下的适用规则
2. **新增/修改模块功能**优先使用 [fullnet-module-delivery Skill](../.agents/skills/fullnet-module-delivery/SKILL.md)
3. **性能相关任务**使用 [fullnet-performance-hardening Skill](../.agents/skills/fullnet-performance-hardening/SKILL.md)
4. **Host.Api/Worker Native AOT** 可达代码或依赖变更必须遵守 [Native AOT 规则](../rules/native-aot.md)，并按 ADR-0008/0009 精确范围声明能力状态
5. 所有数据库对象、公共标识符、稳定机器码必须遵循 [命名规范](./naming-conventions-summary.md)
6. 业务模块禁止直接访问其他模块的内部表，跨模块通信只能通过公开 Contracts

## 相关链接

- [项目 README](../README.md)
- [能力状态矩阵](../docs/roadmap/capability-status.md)
- [Admin.NET 功能对标路线](../docs/roadmap/adminnet-feature-parity.md)
- [本地开发指南](../docs/development/getting-started.md)
- [新人 Onboarding](../docs/development/onboarding.md)
- [Native AOT 开发指南](../docs/development/native-aot-development-guide.md)
