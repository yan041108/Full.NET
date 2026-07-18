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
6. 产生或更新架构评估、设计规格、重大决策、实施计划或验证记录时，必须按 [`rules/development-quality.md`](rules/development-quality.md) 第 12.1 节执行文档产物分层；评估和计划不得静默覆盖已批准规格，计划完成状态不得代替验证证据。

### 开发中

1. 行为变更和缺陷修复必须先建立可失败的验证，再实现最小正确变更。
2. 必须保持模块边界、租户隔离、数据一致性以及 SQL Server/MySQL 双提供程序约束。
3. 代码标识符使用英文；所有手写源代码注释（包括 XML 文档注释）必须使用清晰中文，专业术语可保留英文。
4. 注释必须解释意图、边界、不变量或风险，禁止逐行复述代码。
5. 不得静默更改公共 API、序列化契约、数据库结构、兼容适配器或许可证边界。

### 完成前

1. 必须执行与风险相称的构建、测试和静态检查，并依据新鲜输出报告结果。
2. 必须同步更新受影响的 README、开发文档、路线图、迁移说明和测试数量门槛。
3. 必须读取并执行 [`rules/rule-evolution.md`](rules/rule-evolution.md) 的遗漏复盘；满足升级门槛时，在同一任务中更新相应规则并在交付说明中披露。
4. 规则复盘后必须读取并执行 [`rules/skill-evolution.md`](rules/skill-evolution.md) 的 Skills 复盘；满足门槛时更新候选或按测试先行流程演进一个项目 Skill。
5. 必须检查 `git diff --check`、`git status` 和分支状态，不得把“测试未执行”表述为“测试通过”。

## Full.NET 不可隐式改变的基线

- Full.NET 1.0 采用强化型模块化单体，API、Worker、Migrator 按运行角色分离，AppHost 只负责编排；禁止全面微服务化或以未来假设提前引入网络边界。局部模块拆分必须满足 [`ADR-0002`](docs/architecture/adr/ADR-0002-modular-monolith-evolution.md) 的全部门禁并新增独立 ADR。
- 数据访问以 Dapper 和显式 SQL 为默认实现；未经明确架构决策不得引入 EF Core 作为业务数据访问层。
- Dapper 辅助能力只允许通过 Full.NET 自有边界使用：原生 `QueryMultiple` 经多结果集执行器暴露，`Dapper.SqlBuilder` 仅在真实动态列表消费者出现时由专用查询构建层封装；禁止业务模块直接引用 Dapper/ADO.NET、`Dapper.ProviderTools`、`Dapper.Transaction`、通用 Repository 或自动 CRUD 扩展。
- 数据库正式支持 SQL Server 与 MySQL；数据库行为变更必须同时验证两者。
- 数据库表采用 `{owner}_{module}_{entity}`：Full.NET 官方表的 OwnerKey 固定为 `fn`，项目表使用脚手架阶段冻结的项目 OwnerKey；`sys` 是保留 OwnerKey，禁止运行时动态表前缀。数据库列使用 PascalCase 与 C# Dapper 投影直接映射，详细命名服从 [`rules/naming-conventions.md`](rules/naming-conventions.md)。
- 外部 HTTP API 使用标准状态码与 ProblemDetails；Admin.NET 统一包络只存在于兼容适配层。
- JSON 使用 System.Text.Json；内部高性能序列化按既定边界使用 MessagePack，服务契约可使用 gRPC。
- 缓存以 FusionCache 为唯一实现，并通过 `.AsHybridCache()` 同时暴露 HybridCache 抽象。
- 后续功能以 Admin.NET 为功能参考目标，但实现必须遵守 Full.NET 的架构、安全和发布许可边界。
- 默认引导账号属于受保护的 `host-administrator` 超级管理员系统角色，并按当前可信 Host/Tenant 上下文动态拥有授权目录中的全部适用权限；超级管理员不得绕过租户隔离、账号/会话状态、精确 Endpoint 权限、审计和高风险确认，且系统必须保护最后一名有效超级管理员。
- 后台管理功能必须在 Vue 主管理端与 Layui JS/HTML 管理端按同一模块同步开发；只有两端的权限、租户、错误处理、关键流程和 E2E 都通过后，客户端功能才可标记为 `Verified`。
- Vue 主管理端采用 Vue 3 + TypeScript + Vite + Element Plus，并以 MIT 的 Art Design Pro 作为管理壳层与交互基线；Apache-2.0 的 ECharts 是默认图表引擎，必须模块化注册和按需加载。富文本默认采用 MIT Tiptap Core，由 Vue/Layui 分别适配，禁止默认引入付费 Pro 扩展。只引入经许可证和资产来源审计的代码，Full.NET 自有认证、租户、权限、ProblemDetails、路由白名单和 OpenAPI 契约不得被模板内置 Mock/请求层替代。
- Layui 管理端只依赖 MIT 的 Layui 核心库并独立实现；layuiAdmin 仅可作为公开页面的功能/交互参考，未经允许公开源码并以 MIT 再发布的明确书面授权，禁止复制或提交其源码及产品资产。
- H5、微信小程序和支付宝小程序统一采用 uni-app Vue 3，默认 UI 组件库为官方 uni-ui；原版 uView 2 不进入默认依赖，其他组件库只能在缺口、Vue 3/三目标兼容、许可证和体积门禁通过后按需引入。原生 Android/iOS 与 Windows/macOS/Linux 桌面端默认采用 Flutter 3.44 的 Material 3 + Cupertino 官方组件和 Full.NET 设计令牌，不绑定第三方整套 UI 框架；.NET MAUI 只在真实 C#/Windows 企业需求命中决策门禁后作为可选模板引入。
- 多语言采用“统一治理、平台原生实现”：全栈使用规范 BCP 47 语言标签和稳定错误/权限/枚举代码，各客户端与服务端分别使用平台原生资源机制；业务逻辑不得依赖翻译文本，完成状态必须按路线图和跨端验证如实标记，不能用管理壳层翻译代替全栈支持。
- 种子数据采用“生产安全 Baseline＋环境 Overlay”：默认 Migrator 只迁移，Production 只允许显式 Baseline，Development/Demo/Test 先执行 Baseline 再叠加各自数据；API/Worker 不得启动播种，测试专用 Contributor 不进入发布物，Contributor 必须幂等且通过 SQL Server/MySQL 双库验证。

## 详细规则索引

- [`rules/README.md`](rules/README.md)：规则范围、用词和维护方式。
- [`rules/code-comments.md`](rules/code-comments.md)：中文代码注释与文档注释规范。
- [`rules/development-quality.md`](rules/development-quality.md)：常见遗漏防护和完成定义。
- [`rules/naming-conventions.md`](rules/naming-conventions.md)：数据库、C#、API、机器码、配置、缓存和生成器的统一命名边界。
- [`rules/rule-evolution.md`](rules/rule-evolution.md)：自动复盘、规则升级、冲突与退役机制。
- [`rules/skill-evolution.md`](rules/skill-evolution.md)：项目 Skills 候选、测试先行、升级与退役机制。
- [`.agents/skills/fullnet-module-delivery`](.agents/skills/fullnet-module-delivery/SKILL.md)：完整业务模块纵向交付流程。
