# Full.NET 种子数据模块设计

- 日期：2026-07-17
- 状态：Approved
- 适用范围：Host.Migrator、Identity、Tenancy 及后续业务模块
- 关联设计：[Full.NET 总体架构](2026-07-17-fullnet-architecture-design.md)、[全栈多语言](2026-07-17-full-stack-localization-design.md)

## 1. 结论

Full.NET 增加独立的种子数据基础设施，采用“模块贡献者管道、显式运行 profile、Dapper 双库执行、审计而不代替幂等”的方案。种子数据只由 `Host.Migrator` 在数据库迁移成功后显式执行，API 和 Worker 不得在启动时自动播种。

种子数据分成三个互不混用的边界：

1. **System Bootstrap**：首个宿主管理员、系统角色和权限等生产安全初始化。它保留在所属业务模块中，必须使用显式密钥或确定性系统目录，不属于演示数据。
2. **Development/Demo Seed**：本地开发和产品演示所需的租户、组织、角色、用户及示例业务数据，由本设计的贡献者管道执行。
3. **Test Fixture**：自动化测试在临时 SQL Server/MySQL 中创建的数据，由测试工厂管理，不进入产品 Seed profile，也不依赖开发种子。

首版提供 `development` 和 `demo` 两个 profile。默认 Migrator 只迁移数据库，不执行任何 Seed；Production 环境无条件拒绝这两个 profile。

## 2. 现状与问题

当前 `Host.Migrator --seed-local` 在宿主入口中直接完成两项工作：调用 Tenancy 创建 `local` 租户，再调用 Identity Bootstrap 创建首个宿主管理员。现有实现已经具备真实 Dapper 写入、事务 Outbox、幂等账号引导和 SQL Server/MySQL 集成测试，但它存在以下扩展问题：

- Migrator 直接知道每个模块的业务服务，模块增加后入口会持续膨胀；
- `--seed-local` 同时承担生产安全 Bootstrap 和开发数据，运行语义不够清晰；
- 没有贡献者依赖顺序、并发运行锁、执行审计和失败定位；
- 没有统一的 profile、版本、结果计数和生产环境门禁；
- 测试数据、开发数据和未来演示数据缺少明确隔离；
- 多语言设计落地后，演示数据的稳定 code、默认语言和可翻译内容容易混在一起。

本设计不否定现有 Bootstrap。它把宿主硬编码的开发数据迁移到可扩展管道，同时保留安全初始化的独立语义。

## 3. 目标与非目标

### 3.1 目标

- 每个模块独立声明自己能创建的开发或演示数据；
- 同一 profile 重复执行不产生重复记录，不重置密码，不覆盖用户已修改内容；
- SQL Server 与 MySQL 具有等价的运行锁、执行审计和双库测试；
- 多实例或重复部署时只有一个 Seed 执行器进入贡献者管道；
- 失败可定位到具体 run、贡献者和稳定错误码，重新执行能够安全修复缺失数据；
- 开发者能通过 Aspire 一次启动获得可用的本地租户和显式配置的管理员；
- 后续模块只实现贡献者，无须修改 Migrator 主流程。

### 3.2 非目标

- 不建立万能 JSON/YAML/Excel 导入引擎；
- 不把 Seed 当作数据库迁移、备份恢复或生产数据修复工具；
- 不提供删除、清库、重置密码或覆盖业务数据的默认能力；
- 不通过 HTTP 暴露 Seed API，也不在管理后台提供一键生产播种；
- 不把自动化测试依赖到 `development` 或 `demo` profile；
- 不在首版为尚未实现的 Organization、Notifications 或示例 CRM 创建空贡献者。

## 4. 方案比较

| 方案 | 优点 | 主要问题 | 结论 |
| --- | --- | --- | --- |
| Migrator 继续硬编码模块服务 | 实现最快，文件少 | 宿主持续耦合业务模块，缺少依赖图、审计和统一门禁 | 不采用 |
| 模块贡献者管道 | 模块自治、可测试、易审计，可复用现有领域服务 | 需要两个基础项目和双库执行记录 | 采用 |
| 外部 JSON/YAML 清单解释器 | 非开发人员可编辑，数据量扩展方便 | 绕过领域规则，引用关系、版本、安全和多语言复杂度高 | 后续独立 Import 模块评估 |

## 5. 项目与依赖边界

新增两个 BuildingBlock：

```text
src/BuildingBlocks/Full.NET.Seeding.Abstractions
├── SeedProfile.cs
├── SeedContext.cs
├── SeedContributionResult.cs
├── IDataSeedContributor.cs
└── SeedErrors.cs

src/BuildingBlocks/Full.NET.Seeding.Dapper
├── SeedOrchestrator.cs
├── SeedContributorGraph.cs
├── SeedExecutionStore.cs
├── SeedExecutionLease.cs
├── SeedOptions.cs
└── ServiceCollectionExtensions.cs
```

依赖方向固定为：

```text
业务模块 -> Full.NET.Seeding.Abstractions
Host.Migrator -> Full.NET.Seeding.Dapper + 业务模块
Full.NET.Seeding.Dapper -> Seeding.Abstractions + Data.Abstractions/Dapper
```

`Full.NET.Seeding.Dapper` 不引用 Identity、Tenancy 或其他业务模块。业务模块只引用纯契约项目，不得依赖 Seed 执行器、数据库锁或 Migrator。

## 6. 核心契约

### 6.1 Profile

首版 profile 是封闭枚举：

```csharp
public enum SeedProfile
{
    Development,
    Demo,
}
```

CLI 使用小写规范值 `development`、`demo`。未知值直接失败，不做模糊匹配。自定义产品若需要 staging 等新语义，必须先扩展枚举、生产门禁和测试，而不是透传任意字符串。

### 6.2 Contributor

```csharp
public interface IDataSeedContributor
{
    string Name { get; }

    int Version { get; }

    IReadOnlySet<SeedProfile> Profiles { get; }

    IReadOnlyCollection<string> Dependencies { get; }

    Task<SeedContributionResult> SeedAsync(
        SeedContext context,
        CancellationToken cancellationToken = default);
}
```

约束如下：

- `Name` 使用稳定的小写点分标识，例如 `tenancy.local-tenant`，发布后不可静默改名；
- `Version` 从 1 开始，只在贡献者声明的数据契约发生变化时递增；
- `Dependencies` 引用其他贡献者的 `Name`，执行前统一检查缺失、重复和循环；
- Contributor 通过构造函数取得所属模块服务，禁止从 `SeedContext` 使用 Service Locator；
- `SeedContext` 只包含 RunId、Profile、EnvironmentName、DefaultLocale 和 CorrelationId 等运行元数据；
- 返回值报告 Created、Updated、Skipped 数量和稳定结果 code，不返回密码、Token 或连接信息。

### 6.3 结果语义

`SeedContributionResult` 的 `CreatedCount`、`UpdatedCount`、`SkippedCount` 用于日志和审计。成功但没有变化是正常结果。失败通过异常边界映射为稳定错误码，原始异常只进入受保护的结构化日志，不写入数据库审计详情。

## 7. Profile 内容与安全边界

### 7.1 Development

首个贡献者为 `tenancy.local-tenant`：

- Identifier：`local`；
- Name：`Full.NET Local`；
- Domain：`localhost`；
- 默认语言：多语言 L0/L1 落地后使用 `zh-CN`。

首个宿主管理员仍由 `IIdentityBootstrapService` 创建，用户名和密码必须来自 Secret。Seeder 不内置默认密码，也不把密码写入日志、审计表或种子清单。

### 7.2 Demo

首版只预留 profile 和基础约束，不提前创建尚无真实业务消费者的数据。随着模块实现，Demo 可以增加：

- 演示租户和套餐；
- 组织、岗位、角色、菜单与数据权限；
- 禁用状态的示例账号，或使用显式 `Seeding:DemoUserPassword` Secret 创建可登录账号；
- Sample CRM 等真实样例模块的数据。

Demo Contributor 必须使用稳定 code/自然键定位数据。没有显式密码 Secret 时，不得创建带可预测密码的活跃账号。

### 7.3 Production

当 `IHostEnvironment.IsProduction()` 为 true 时，`development` 和 `demo` 均在数据库锁与任何写入之前失败，错误码为 `seeding.profile_not_allowed`。首版不提供配置开关绕过该门禁。

生产所需的 System Bootstrap 通过独立显式参数和 Secret 执行。迁移、Bootstrap 和 Development/Demo Seed 在日志与退出码中分别报告，不互相掩盖失败。

## 8. 幂等、更新与删除策略

审计历史不作为跳过依据。每次 Seed 都重新执行适用 Contributor，由 Contributor 查询真实业务状态并幂等协调，这样人工删除部分演示数据后可以重新补齐。

统一规则：

- 使用稳定 code、Identifier、Username 等业务自然键查找，不依赖随机名称；
- 缺失数据可以创建；系统管理字段可以协调到当前版本；
- 密码、用户修改的显示名称、业务状态和自定义授权默认不覆盖；
- 不删除不再出现在新版本中的数据；需要删除时另写显式迁移或受审计管理命令；
- Contributor 必须在自己的事务内完成一组原子变更；跨 Contributor 不建立大事务；
- 使用现有 Command/Domain Service 和参数化 Dapper SQL，不直接绕过领域与租户边界；
- 创建会产生 Outbox 的业务对象时继续使用真实 Outbox 契约，重复执行不得重复发布创建事件。

## 9. 执行流程

```text
Host.Migrator 启动
-> 执行 DbUp 迁移
-> 解析显式 --seed <profile>
-> 检查 Environment/Profile 门禁
-> 验证 Contributor 名称、版本和依赖图
-> 获取数据库级 Full.NET.Seeding 锁
-> 写入 fn_seed_run
-> 按拓扑顺序逐个执行 Contributor
-> 写入 fn_seed_run_item 计数与状态
-> 完成 run，释放数据库锁
-> 执行显式 System Bootstrap
-> 按任一失败返回非零退出码
```

贡献者失败时立即停止后续贡献者。已提交的前置贡献者不回滚；整条管道通过幂等性支持修复后重跑。取消信号必须传递到锁等待、SQL、领域服务和审计写入。

## 10. 双数据库锁与执行审计

### 10.1 数据库锁

- SQL Server 使用 session 级 `sp_getapplock`，资源名 `Full.NET.Seeding`；
- MySQL 使用 `GET_LOCK('Full.NET.Seeding', timeoutSeconds)`；
- 持锁连接在整个贡献者管道期间保持打开；
- 默认等待 30 秒，超时返回 `seeding.lock_timeout`；
- 释放失败只记录警告，原始执行结果不能被释放异常覆盖。

### 10.2 审计表

双库新增同序号迁移，创建：

`fn_seed_run`：

| 字段 | 语义 |
| --- | --- |
| Id | GuidV7 RunId |
| Profile | `development` 或 `demo` |
| EnvironmentName | 执行环境，不含机器秘密 |
| Status | Running/Succeeded/Failed/Cancelled |
| ApplicationVersion | 可用时记录程序集版本 |
| CorrelationId | 部署关联标识 |
| StartedAt/CompletedAt | UTC 时间 |
| ErrorCode | 稳定错误码，不保存异常消息 |

`fn_seed_run_item`：

| 字段 | 语义 |
| --- | --- |
| RunId + Contributor | 主键 |
| ContributorVersion | 本次代码版本 |
| Status | Running/Succeeded/Failed/Cancelled |
| Created/Updated/SkippedCount | 结果计数 |
| StartedAt/CompletedAt | UTC 时间 |
| ErrorCode | 稳定错误码 |

审计表只用于观察和故障定位，不记录 Seed 输入正文，不保存密码、个人数据或异常堆栈，也不决定是否跳过 Contributor。

## 11. CLI 与兼容策略

目标命令：

```powershell
dotnet run --project src/Hosts/Full.NET.Host.Migrator -- --seed development
dotnet run --project src/Hosts/Full.NET.Host.Migrator -- --seed demo
```

没有 `--seed` 时只执行迁移和显式配置的 System Bootstrap。`--seed-local` 在一个兼容周期内映射为 `--seed development` 并记录弃用警告；同时传入新旧参数属于配置错误。

AppHost 改为传入 `--seed development`。部署流水线默认不传 Seed 参数，Production 即使误传也会在写入前失败。

首版不提供 `--reset`、`--force`、`--delete` 或任意 Seed 文件路径参数。

## 12. 多租户与多语言

- Host 级 Contributor 必须声明 Host 数据范围；租户 Contributor 必须通过受信任的 TenantContext 执行；
- Seed 输入中的 TenantId 不来自 HTTP 或用户输入，而来自前置 Contributor 的受控查询结果；
- 租户缓存、Outbox 和后续通知继续包含 TenantId；
- Seed profile 使用规范 BCP 47 标签，默认 `zh-CN`；
- Username、Identifier、PermissionCode、Enum、ErrorCode 等稳定机器值不本地化；
- 模块具有翻译表后，Contributor 同时写入 `zh-CN/en-US` 资源；在翻译表落地前只写业务主记录，不引入通用 JSON 翻译字段；
- 日期、金额和时区保存为业务不变量，不把格式化文本写入业务表。

## 13. 日志、错误与可观测性

每个 run 和 Contributor 使用结构化日志记录 RunId、Profile、Contributor、Version、Duration 和结果计数。日志禁止记录连接串、密码、Token、完整 Seed 输入或用户敏感字段。

稳定错误码至少包括：

- `seeding.invalid_profile`；
- `seeding.profile_not_allowed`；
- `seeding.duplicate_contributor`；
- `seeding.missing_dependency`；
- `seeding.dependency_cycle`；
- `seeding.lock_timeout`；
- `seeding.contributor_failed`；
- `seeding.cancelled`。

失败日志保留内部异常供运维诊断；CLI 只输出安全摘要并返回非零退出码。

## 14. 测试策略

### 14.1 Unit

- profile 解析、Production 门禁；
- 重复名称、缺失依赖和依赖循环；
- 确定性拓扑顺序；
- 失败即停、取消传播和结果计数；
- Development Contributor 新建、已存在和冲突数据语义；
- 日志与审计结果不包含 Secret。

### 14.2 Architecture

- 业务模块只引用 `Seeding.Abstractions`；
- `Seeding.Dapper` 不引用任何业务模块；
- API/Worker 不引用或执行 Seed Orchestrator；
- Seed 只由 Migrator 装配。

### 14.3 Integration

SQL Server 和 MySQL 必须分别验证：

- 双库迁移创建审计表和约束；
- `development` 首次执行创建 local 租户并写入成功审计；
- 第二次执行不增加租户和 Outbox 创建消息，SkippedCount 增加；
- Contributor 失败后 run/item 为 Failed，修复后重跑可成功；
- 两个并发执行器只有一个持锁，另一个超时或等待后安全执行；
- Production 门禁发生在任何 Seed 写入之前；
- `--seed-local` 与 `--seed development` 在兼容期产生等价数据。

自动化测试仍在临时 Testcontainers 数据库内创建自己的测试数据，不读取开发 Seed 的账号或固定行。

## 15. 交付阶段

| 阶段 | 范围 | 退出条件 |
| --- | --- | --- |
| S0 契约 | Abstractions、profile、贡献者图 | Unit/Architecture 通过 |
| S1 双库执行器 | Dapper 锁、run/item 审计、双库迁移 | SQL Server/MySQL 锁与审计通过 |
| S2 Development | Tenancy local Contributor、Migrator CLI、AppHost | 首次/重复/失败重跑双库通过 |
| S3 Demo | 随真实模块增加演示 Contributor | 无默认密码，模块验收与多语言资源通过 |
| S4 运维 | CI、发布说明、审计查询与清理策略 | 默认生产部署不执行 Seed |

当前只批准 S0-S2 进入实施计划。S3 必须随真实业务模块逐项实现，禁止一次性制造与产品功能脱节的大批假数据。

## 16. 完成定义

种子数据模块只有满足以下条件才可标记为 `Implemented`：

- 默认 Migrator 不执行 Development/Demo Seed；
- Production 拒绝 Development/Demo 且没有绕过开关；
- SQL Server/MySQL 的迁移、锁、审计、首次与重复执行测试全部通过；
- local 租户不重复，失败重跑可恢复，System Bootstrap 不覆盖已有密码；
- API/Worker 没有启动播种；
- Test Fixture 与产品 Seed 不互相依赖；
- 文档、CLI、AppHost、测试数量门槛、规则与 Skill 候选同步更新；
- 没有默认密码、敏感日志、任意文件执行或删除数据入口。
