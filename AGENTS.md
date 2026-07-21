# Full.NET 仓库开发规则

本文件适用于仓库根目录及全部子目录，是开发代理进入 Full.NET 后必须读取的项目入口。详细规则位于 [`rules/`](rules/README.md)。

## 指令优先级

1. 系统、开发者和当前用户指令始终高于本文件及 `rules/`。
2. 子目录若存在更具体的 `AGENTS.md`，仅对该子目录追加或收紧约束；冲突时服从更高层级规则。
3. 规则不能扩大任务授权。涉及发布、外部写入、破坏性操作或范围外变更时，仍须取得相应授权。

## 每项任务必须执行

### 开始前

1. 必须读取本文件和 [`rules/README.md`](rules/README.md)。
2. 必须读取 [`rules/development-quality.md`](rules/development-quality.md)；涉及代码、SQL、配置或脚本时，还必须读取 [`rules/code-comments.md`](rules/code-comments.md)；新增或修改数据库对象、公共标识符、API/JSON、稳定机器码、配置键、缓存键或生成器产物时，还必须读取 [`rules/naming-conventions.md`](rules/naming-conventions.md)。
3. 必须检查 `.agents/skills/` 是否存在匹配当前任务的项目 Skill；新增或扩展模块、CRUD、Endpoint、Command/Query、Dapper 持久化或双库迁移时必须使用 [`fullnet-module-delivery`](.agents/skills/fullnet-module-delivery/SKILL.md)。
4. 必须检查当前分支、`git status`、相关设计与计划，保留用户已有和无关变更。
5. 必须确认需求、授权边界和验收条件；能从仓库安全确定的信息不得反复询问。
6. 产生或更新评估、规格、决策、计划或验证记录时，必须遵循 [`rules/development-quality.md`](rules/development-quality.md) 第 12.1 节的文档产物分层。

### 开发中

1. 行为变更和缺陷修复必须先建立可失败的验证，再实现最小正确变更。
2. 必须保持模块边界、租户隔离、数据一致性以及 SQL Server/MySQL 双提供程序约束。
3. 代码标识符使用英文，所有手写注释（含 XML 文档注释）使用清晰中文并解释意图、边界、不变量或风险，禁止逐行复述；细则见 [`rules/code-comments.md`](rules/code-comments.md)。
4. 不得静默更改公共 API、序列化契约、数据库结构、兼容适配器或许可证边界。

### 完成前

1. 必须执行与风险相称的构建、测试和静态检查，并依据新鲜输出报告结果。
2. 必须同步更新受影响的 README、开发文档、路线图、迁移说明和测试数量门槛。
3. 必须读取并执行 [`rules/rule-evolution.md`](rules/rule-evolution.md) 的遗漏复盘；满足升级门槛时，在同一任务中更新相应规则并在交付说明中披露。
4. 规则复盘后必须读取并执行 [`rules/skill-evolution.md`](rules/skill-evolution.md) 的 Skills 复盘；满足门槛时更新候选或按测试先行流程演进一个项目 Skill。
5. 必须检查 `git diff --check`、`git status` 和分支状态，不得把“测试未执行”表述为“测试通过”。

## Full.NET 不可隐式改变的基线

下列基线只陈述不变量并指向唯一权威源；执行细则与验证方式以链接的 `rules/` 文件或 ADR 为准，不在此内联复述，避免双写漂移。

- Full.NET 1.0 保持强化型模块化单体，API、Worker、Migrator 按运行角色分离，AppHost 只负责编排，禁止全面微服务化或提前引入网络边界；拆分门禁见 [`rules/development-quality.md`](rules/development-quality.md) 第 3 节与 [`ADR-0002`](docs/architecture/adr/ADR-0002-modular-monolith-evolution.md)。
- 业务模块物理拓扑默认采用“一个主项目＋按证据可选 Contracts/传输适配项目”；小功能、CRUD、实体、菜单和用例只能作为主项目内的垂直切片，禁止按功能机械增加 `.csproj`；项目拆分门禁见 [`rules/development-quality.md`](rules/development-quality.md) 第 3 节与[总体架构 Spec §4.2](docs/superpowers/specs/2026-07-17-fullnet-architecture-design.md#42-解决方案结构)。
- 业务数据访问默认使用 Dapper 与显式 SQL，未经明确架构决策不得引入 EF Core；细则见 [`rules/development-quality.md`](rules/development-quality.md) 第 5 节。
- Dapper 辅助能力只允许通过 Full.NET 自有边界使用，业务模块禁止直连数据库、通用 Repository 或自动 CRUD；禁用清单与验证以 [`rules/development-quality.md`](rules/development-quality.md) R-20260718-dapper-tooling-boundary 为准。
- 当前可靠业务 Integration Event 只通过事务 Outbox 发布；CDC Relay/Kafka 排在当前硬化和核心业务之后，必须经真实 SLA、瓶颈与双库运维门禁，且不得按瞬时 QPS 动态改变可靠性语义；细则见 [`rules/development-quality.md`](rules/development-quality.md) 第 6 节与[总体架构 Spec §9.1](docs/superpowers/specs/2026-07-17-fullnet-architecture-design.md#91-事件交付演进基线)。
- 数据库正式支持 SQL Server 与 MySQL，数据库行为变更必须同时验证两者；细则见 [`rules/development-quality.md`](rules/development-quality.md) 第 5、11 节。
- Full.NET 官方表逻辑主键为应用端生成的 UUID v7，C# 与业务模块只使用 `Guid`；物理类型、字节序、聚集索引与转换边界以 [`rules/naming-conventions.md`](rules/naming-conventions.md) 第 4、5 节为准。
- 数据库表采用 `{owner}_{module}_{entity}`（官方 OwnerKey 固定为 `fn`，`sys` 保留，禁止运行时动态表前缀），列使用 PascalCase 与 Dapper 投影直接映射；完整命名以 [`rules/naming-conventions.md`](rules/naming-conventions.md) 为准。
- 对外 HTTP API 使用标准状态码与 ProblemDetails，Admin.NET 统一包络只存在于兼容适配层；细则见 [`rules/development-quality.md`](rules/development-quality.md) 第 7 节。
- JSON 使用 System.Text.Json，内部高性能序列化按既定边界使用 MessagePack，服务契约可使用 gRPC；细则见 [`rules/development-quality.md`](rules/development-quality.md) 第 7 节。
- 缓存以 FusionCache 为唯一实现并通过 `.AsHybridCache()` 暴露双抽象；细则见 [`rules/development-quality.md`](rules/development-quality.md) 第 8 节。
- 后续功能以 Admin.NET 为功能参考目标，但实现必须遵守 Full.NET 的架构、安全和发布许可边界；对标方式见 [`rules/development-quality.md`](rules/development-quality.md) 第 3 节。
- 默认引导账号属于受保护的 `host-administrator` 超级管理员系统角色，动态投影授权目录权限且不得绕过租户隔离、账号/会话状态、精确 Endpoint 权限、审计与最后一名保护；细则以 [`rules/development-quality.md`](rules/development-quality.md) R-20260718-super-administrator-boundary 为准。
- 后台管理功能必须在 Vue 与 Layui 双管理端按同一模块同步开发，双端权限、租户、错误处理、关键流程与 E2E 全部通过后才可标记 `Verified`；细则见 [`rules/client-frontend.md`](rules/client-frontend.md) 第 2 节。
- Vue 主管理端、Layui 管理端、uni-app 与原生/桌面端的框架、UI 组件库、许可与资产来源边界见 [`rules/client-frontend.md`](rules/client-frontend.md)。
- 多语言采用“统一治理、平台原生实现”，全栈使用规范 BCP 47 语言标签与稳定机器码，业务逻辑不得依赖翻译文本，完成状态按跨端验证如实标记；细则以 [`rules/development-quality.md`](rules/development-quality.md) R-20260717-full-stack-localization-boundary 为准。
- 种子数据采用“生产安全 Baseline＋环境 Overlay”，Production 只允许 Baseline，API/Worker 不得启动播种，Contributor 必须幂等且通过双库验证；细则以 [`rules/development-quality.md`](rules/development-quality.md) R-20260717-seed-data-boundary 为准。

## 详细规则索引

- [`rules/README.md`](rules/README.md)：规则范围、用词和维护方式。
- [`rules/code-comments.md`](rules/code-comments.md)：中文代码注释与文档注释规范。
- [`rules/development-quality.md`](rules/development-quality.md)：常见遗漏防护和完成定义。
- [`rules/naming-conventions.md`](rules/naming-conventions.md)：数据库、C#、API、机器码、配置、缓存和生成器的统一命名边界。
- [`rules/client-frontend.md`](rules/client-frontend.md)：Vue/Layui 双管理端、uni-app、Flutter 与桌面端的框架、UI、许可与验收边界。
- [`rules/rule-evolution.md`](rules/rule-evolution.md)：自动复盘、规则升级、冲突与退役机制。
- [`rules/skill-evolution.md`](rules/skill-evolution.md)：项目 Skills 候选、测试先行、升级与退役机制。
- [`.agents/skills/fullnet-module-delivery`](.agents/skills/fullnet-module-delivery/SKILL.md)：完整业务模块纵向交付流程。
