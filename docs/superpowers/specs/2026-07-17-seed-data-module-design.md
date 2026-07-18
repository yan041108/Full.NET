# Full.NET 种子数据模块设计

- 日期：2026-07-17
- 状态：Approved
- 适用范围：Host.Migrator、Identity、Tenancy 及后续业务模块
- 关联设计：[Full.NET 总体架构](2026-07-17-fullnet-architecture-design.md)、[全栈多语言](2026-07-17-full-stack-localization-design.md)

## 1. 结论

Full.NET 增加独立的种子数据基础设施，采用“生产安全基线、环境数据叠加、模块贡献者管道、Dapper 双库执行、审计而不代替幂等”的方案。种子数据既用于生产首次初始化，也可供开发和测试复用；它只由 `Host.Migrator` 在数据库迁移成功后显式执行，API 和 Worker 不得在启动时自动播种。

种子数据分成三个有明确继承关系、但不混淆发布边界的层次：

1. **Baseline Seed**：生产安全初始化，包含系统权限、必要字典、首个宿主管理员等生产所需数据；安全敏感项必须使用显式 Secret。
2. **Development/Demo/Test Overlay**：分别在 Baseline 之上叠加本地开发、产品演示或自动化测试所需数据。
3. **Scenario Test Fixture**：单个测试场景的订单、冲突、失败等数据仍由测试工厂创建；Test profile 负责共享基线，Test Factory 负责每个用例的隔离状态。

首版提供 `baseline`、`development`、`demo` 和 `test` 四个 profile。默认 Migrator 只迁移数据库，不执行任何 Seed；Production 只允许显式 `baseline`，无条件拒绝其他三个 profile。

## 2. 现状与问题

当前 `Host.Migrator --seed-local` 在宿主入口中直接完成两项工作：调用 Tenancy 创建 `local` 租户，再调用 Identity Bootstrap 创建首个宿主管理员。现有实现已经具备真实 Dapper 写入、事务 Outbox、幂等账号引导和 SQL Server/MySQL 集成测试，但它存在以下扩展问题：

- Migrator 直接知道每个模块的业务服务，模块增加后入口会持续膨胀；
- `--seed-local` 把生产安全基线与开发叠加数据绑定在一个开关中，运行语义不够清晰；
- 没有贡献者依赖顺序、并发运行锁、执行审计和失败定位；
- 没有统一的 profile、版本、结果计数和生产环境门禁；
- 测试数据、开发数据和未来演示数据缺少明确隔离；
- 多语言设计落地后，演示数据的稳定 code、默认语言和可翻译内容容易混在一起。

本设计保留现有 `IIdentityBootstrapService` 的安全与幂等实现，但由 Baseline Contributor 调用它，使生产、开发和测试通过同一管道复用真实初始化逻辑。

## 3. 目标与非目标

### 3.1 目标

- 每个模块独立声明自己能创建的开发或演示数据；
- 同一 profile 重复执行不产生重复记录，不重置密码，不覆盖用户已修改内容；
- SQL Server 与 MySQL 具有等价的运行锁、执行审计和双库测试；
- 多实例或重复部署时只有一个 Seed 执行器进入贡献者管道；
- 失败可定位到具体 run、贡献者和稳定错误码，重新执行能够安全修复缺失数据；
- 生产部署能显式执行安全 Baseline；开发者能通过 Aspire 一次启动获得相同基线、local 租户和显式配置的管理员；
- 自动化测试可以运行 Baseline/Test profile，但测试专用 Contributor 不进入生产发布程序集；
- 后续模块只实现贡献者，无须修改 Migrator 主流程。

### 3.2 非目标

- 不建立万能 JSON/YAML/Excel 导入引擎；
- 不把 Seed 当作数据库迁移、备份恢复或生产数据修复工具；
- 不提供删除、清库、重置密码或覆盖业务数据的默认能力；
- 不通过 HTTP 暴露 Seed API，也不在管理后台提供一键生产播种；
- 不让自动化测试依赖 `development` 或 `demo`；共享系统数据通过 `baseline/test`，场景业务数据由隔离 Test Factory 创建；
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
    Baseline,
    Development,
    Demo,
    Test,
}
```

CLI 使用小写规范值 `baseline`、`development`、`demo`、`test`。未知值直接失败，不做模糊匹配。Profile 采用固定继承：Development、Demo、Test 都先执行 Baseline，再执行自己的 Overlay；自定义产品若需要 staging 等新语义，必须先扩展枚举、门禁、继承关系和测试，而不是透传任意字符串。

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

## 7. Profile 内容、继承与安全边界

### 7.1 Baseline

Baseline 可以在 Production 显式运行，只能包含生产运行所必需或安全的初始化数据：

- 当前代码定义的系统权限、系统角色和角色权限关系；
- 使用 Secret 创建或协调的首个宿主管理员；
- 后续模块明确声明为系统目录的字典、配置、菜单或内置任务定义；
- 生产必须存在且具有稳定 code 的基础数据。

现有 `IIdentityBootstrapService` 由 `identity.host-administrator` Baseline Contributor 调用。Username、Password 和 DisplayName 来自配置/Secret；重复执行继续同步系统授权但不覆盖已有密码。缺少必需 Secret 时 Baseline 失败，不能静默产生一个没有管理员的“成功初始化”。

Baseline 禁止包含示例订单、虚构客户、演示组织、可预测密码、随机大数据或只为 UI 好看的内容。

### 7.2 Development

首个贡献者为 `tenancy.local-tenant`：

- Identifier：`local`；
- Name：`Full.NET Local`；
- Domain：`localhost`；
- 默认语言：多语言 L0/L1 落地后使用 `zh-CN`。

Development 自动先运行全部 Baseline Contributor，再运行 `tenancy.local-tenant` 等开发 Contributor。Seeder 不内置默认密码，也不把密码写入日志、审计表或种子清单。

### 7.3 Demo

首版只预留 profile 和基础约束，不提前创建尚无真实业务消费者的数据。随着模块实现，Demo 可以增加：

- 演示租户和套餐；
- 组织、岗位、角色、菜单与数据权限；
- 禁用状态的示例账号，或使用显式 `Seeding:DemoUserPassword` Secret 创建可登录账号；
- Sample CRM 等真实样例模块的数据。

Demo 自动先运行 Baseline。Demo Contributor 必须使用稳定 code/自然键定位数据。没有显式密码 Secret 时，不得创建带可预测密码的活跃账号。

### 7.4 Test

Test 自动先运行 Baseline。Test 专用 Contributor 放在 `tests/` 或 Sample 测试项目中，不进入正式 NuGet、Host 或容器发布物，用于建立多个测试共享的系统目录和确定性租户基线。

具体测试用例的成功、冲突、并发、回滚和权限数据继续由 Test Factory 在每个临时数据库中创建并清理，禁止所有测试依赖同一个共享可变账号或订单。测试可以直接运行 `baseline` 验证生产初始化，也可以运行 `test` 叠加测试专用数据。

### 7.5 Production

当 `IHostEnvironment.IsProduction()` 为 true 时，只允许 `baseline`；`development`、`demo` 和 `test` 均在数据库锁与任何写入之前失败，错误码为 `seeding.profile_not_allowed`。首版不提供配置开关绕过该门禁。

生产部署必须显式传入 `--seed baseline` 才执行初始化。迁移与 Baseline 在日志、审计和退出码中分别报告，不互相掩盖失败。

## 8. 幂等、更新与删除策略

审计历史不作为跳过依据。每次 Seed 都重新执行适用 Contributor，由 Contributor 查询真实业务状态并幂等协调，这样人工删除部分演示数据后可以重新补齐。

Seed 不提供通用 `Down`。Contributor 可能通过领域服务写入 Outbox、引用数据或用户已修改内容，自动逆向删除无法证明安全。Development/Test 重置数据库使用 Testcontainers 临时库重建、受控 Drop/Recreate 或明确的备份恢复流程，与生产 Seeder 完全分离。版本升级时 Contributor 依据真实状态向前协调；删除、不可逆修正或大批量回填必须使用显式 Migration 或受审计管理命令。

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
-> 展开 Baseline + 目标 Overlay
-> 验证 Contributor 名称、版本和依赖图
-> 获取数据库级 Full.NET.Seeding 锁
-> 写入 fn_seed_run
-> 按拓扑顺序逐个执行 Contributor
-> 写入 fn_seed_run_item 计数与状态
-> 完成 run，释放数据库锁
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
| Profile | `baseline`、`development`、`demo` 或 `test` |
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
dotnet run --project src/Hosts/Full.NET.Host.Migrator -- --seed baseline
dotnet run --project src/Hosts/Full.NET.Host.Migrator -- --seed test
```

没有 `--seed` 时只执行迁移。`--seed-local` 在一个兼容周期内映射为 `--seed development` 并记录弃用警告；同时传入新旧参数属于配置错误。生产发布流水线显式选择 `baseline`，不能依赖环境名称自动播种。

AppHost 改为传入 `--seed development`。生产部署流水线在需要初始化/协调系统基线时显式传 `--seed baseline`，只迁移时不传 Seed 参数；误传其他 profile 会在任何 Seed 写入前失败。

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

- profile 解析、Baseline 继承与 Production 门禁；
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
- Production 允许 Baseline 且拒绝 Development/Demo/Test，拒绝门禁发生在任何 Seed 写入之前；
- `--seed-local` 与 `--seed development` 在兼容期产生等价数据。

自动化测试仍在临时 Testcontainers 数据库内运行 Baseline/Test 并创建场景数据，不读取开发 Seed 的账号或固定行；测试专用 Contributor 不进入生产发布物。

## 15. 交付阶段

| 阶段 | 范围 | 退出条件 |
| --- | --- | --- |
| S0 契约 | Abstractions、profile 继承、贡献者图 | Unit/Architecture 通过 |
| S1 双库执行器 | Dapper 锁、run/item 审计、双库迁移 | SQL Server/MySQL 锁与审计通过 |
| S2 Baseline/Development | Identity Baseline、Tenancy local、Migrator CLI、AppHost | 生产 Baseline 与开发继承的首次/重复/失败重跑双库通过 |
| S3 Demo | 随真实模块增加演示 Contributor | 无默认密码，模块验收与多语言资源通过 |
| S4 运维 | CI、发布说明、审计查询与清理策略 | 默认生产部署不执行 Seed |

当前只批准 S0-S2 进入实施计划。Test profile 的执行器契约进入 S0-S2，但测试专用业务数据随测试切片提供；S3 必须随真实业务模块逐项实现，禁止一次性制造与产品功能脱节的大批假数据。

## 16. 完成定义

种子数据模块只有满足以下条件才可标记为 `Implemented`：

- 默认 Migrator 不执行 Seed，生产部署可显式执行 Baseline；
- Production 拒绝 Development/Demo/Test 且没有绕过开关；
- Development/Demo/Test 确定性继承 Baseline；
- SQL Server/MySQL 的迁移、锁、审计、首次与重复执行测试全部通过；
- Baseline 管理员和系统授权幂等，local 租户不重复，失败重跑可恢复且已有密码不被覆盖；
- API/Worker 没有启动播种；
- Test profile 的共享基线可复用，场景 Fixture 保持隔离，测试专用 Contributor 不进入发布物；
- 文档、CLI、AppHost、测试数量门槛、规则与 Skill 候选同步更新；
- 没有默认密码、敏感日志、任意文件执行或删除数据入口。
