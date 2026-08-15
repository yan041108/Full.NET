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
| [ADR 索引](../docs/architecture/adr/) | 架构决策记录（ADR-0001 ~ ADR-0006） |
| [命名规范摘要](./naming-conventions-summary.md) | 数据库、C#、HTTP/JSON、配置键的统一命名规则 |

### 二、基础设施层 (BuildingBlocks)

| 文档 | 说明 |
|------|------|
| [基础设施层总览](./building-blocks.md) | 所有 BuildingBlocks 项目的职责、依赖和关键能力 |
| [构建与测试指南](./build-and-test.md) | 环境要求、还原、构建、测试套件、集成测试分片 |
| [宿主与部署](./hosts-and-deployment.md) | API/Worker/Migrator 三宿主、Docker 镜像、Helm Chart、K8s 部署 |

### 三、业务模块 (Modules)

| 文档 | 说明 | 状态 |
|------|------|------|
| [模块系统总览](./modules-overview.md) | 模块注册机制、依赖图、Host Profile、垂直切片结构 | |
| [Identity 身份模块](./module-identity.md) | 用户、角色、菜单、授权、认证会话、API Key、TOTP | Build-verified |
| [Tenancy 多租户模块](./module-tenancy.md) | 租户创建、租户解析、租户包、租户切换 | Build-verified |
| [Organization 组织模块](./module-organization.md) | 部门、岗位、职级、用户组织归属 | Build-verified |
| [Settings 配置模块](./module-settings.md) | 参数配置、字典管理、枚举目录、网格偏好 | Build-verified |
| [Auditing 审计模块](./module-auditing.md) | 操作日志、访问日志、异常日志、出站调用、数据保留 | Build-verified |
| [Files 文件模块](./module-files.md) | 文件上传/下载、引用声明、Blob 清理 | Build-verified |
| [Document 文档模块](./module-document.md) | 文档分类/标签/权限/分享/统计/回收站 | Build-verified |
| [Notifications 通知模块](./module-notifications.md) | 站内消息、SignalR 实时推送、收件箱 | Implementing |
| [Jobs 任务模块](./module-jobs.md) | 任务定义、调度、执行记录、触发 | Build-verified |
| [Messaging 消息模块](./module-messaging.md) | Outbox、Kafka/CDC、事件流所有权、Inbox、重放 | Implementing |
| [CodeGeneration 代码生成模块](./module-codegeneration.md) | 模板、CRUD 生成、Git 集成、回滚链路 | Implementing |
| [SerialNumbers 流水号模块](./module-serialnumbers.md) | 编号规则、生成、并发安全 | Build-verified |

### 四、前端与客户端

| 目录 | 说明 |
|------|------|
| `ui/admin` | Vue 3 + TypeScript + Element Plus 主管理端（持续交付线） |
| `ui/admin-layui` | Layui 2 原生管理端（自 2026-08-02 起存量冻结） |
| `clients/uniapp` | uni-app Vue 3 跨端（H5、微信小程序、支付宝小程序） |
| `packages/client-contracts` | TypeScript OpenAPI 契约与客户端 SDK |
| `packages/admin-i18n` | 管理端 i18n 基础设施 |
| `packages/design-tokens` | 跨端设计令牌 (CSS Variables) |

---

## 快速参考

### 核心技术栈

| 层级 | 技术选型 |
|------|----------|
| 运行时 | .NET 10 LTS / ASP.NET Core / C# (Nullable 开启) |
| 数据访问 | Dapper + 显式 SQL (无 EF Core) |
| 数据库 | SQL Server / MySQL (双提供程序一等支持) |
| 迁移引擎 | DbUp + 可审查 SQL 脚本 |
| 缓存 | FusionCache (L1 内存 + L2 Redis + Backplane) |
| 消息 | 事务 Outbox + MessagePack / CDC Kafka / 消费 Inbox |
| 实时 | SignalR + 可选 Redis Backplane |
| 序列化 | System.Text.Json (HTTP) / MessagePack (内部事件) |
| 验证 | FluentValidation |
| 日志 | Serilog + 有界异步输出 |
| 可观测性 | OpenTelemetry + 健康检查 |
| 编排 | .NET Aspire AppHost (本地) / Helm (生产 K8s) |
| 前端 | Vue 3 + TypeScript + Vite + Element Plus |

### 目录结构速查

```text
Full.NET/
├── src/
│   ├── BuildingBlocks/          # 基础设施层（不依赖业务模块）
│   ├── Modules/                 # 业务模块（拥有独立表和契约）
│   ├── Composition/             # 组合根（唯一可引用具体模块的位置）
│   ├── Hosts/                   # 运行宿主（API/Worker/Migrator/AppHost）
│   ├── Compatibility/           # 兼容适配层（Admin.NET 信封等）
│   ├── Generators/              # 源代码生成器
│   └── Tools/                   # CLI 工具
├── ui/                          # 管理后台前端
├── clients/                     # 移动端客户端（uni-app/Flutter）
├── packages/                    # NPM 共享包
├── tests/                       # .NET 测试（Unit/Architecture/Integration/E2E）
├── benchmarks/                  # BenchmarkDotNet 基准
├── deploy/                      # Helm Chart、CDC/Kafka Compose、可观测性
├── docs/                        # 规格、ADR、路线图、运维手册
├── rules/                       # 项目开发规则
├── contracts/                   # OpenAPI、SQL 安全、命名等治理契约
├── scripts/                     # 测试、治理、命名脚本
├── eng/                         # Dockerfile、加载测试配置、CI 配置
└── localzation/                 # 全栈多语言目录
```

---

## 开发必读

1. **开始任何任务前**必须阅读 [AGENTS.md](../AGENTS.md) 和 [`rules/`](../rules/README.md) 下的适用规则
2. **新增/修改模块功能**优先使用 [fullnet-module-delivery Skill](../.agents/skills/fullnet-module-delivery/SKILL.md)
3. **性能相关任务**使用 [fullnet-performance-hardening Skill](../.agents/skills/fullnet-performance-hardening/SKILL.md)
4. 所有数据库对象、公共标识符、稳定机器码必须遵循 [命名规范](./naming-conventions-summary.md)
5. 业务模块禁止直接访问其他模块的内部表，跨模块通信只能通过公开 Contracts

## 相关链接

- [项目 README](../README.md)
- [能力状态矩阵](../docs/roadmap/capability-status.md)
- [Admin.NET 功能对标路线](../docs/roadmap/adminnet-feature-parity.md)
- [本地开发指南](../docs/development/getting-started.md)
- [新人 Onboarding](../docs/development/onboarding.md)
