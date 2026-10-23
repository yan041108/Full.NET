# Full.NET 仓库开发规则

本文件适用于仓库根目录及全部子目录。先遵守下列底线，再由 [规则索引](rules/README.md) 按任务加载细则；已在当前上下文读取且未变化的内容无需重读。

## 指令与执行范围

1. 系统、开发者和当前用户指令优先；项目规则与 Skill 不扩大任务授权。子目录规则仅适用于对应范围，不能静默放宽安全、数据、许可和验证底线。
2. 先区分咨询、只读审查与实施。只读任务直接交付分析，不修改文件、创建任务快照或运行无关构建。已授权且需求明确的修改直接推进，不为例行流程重复索要确认。
3. 小型、清晰任务直接执行；复杂、跨模块或高风险任务先列计划。普通计划留在会话，永久文档按 [文档产物分层](rules/development-quality.md#121-文档产物分层) 创建。
4. Skill 按真实任务需要选择：缺陷先定位原因，开发时明确可验证场景，完成前核对新鲜证据；测试执行时机统一按下述批次规则，不自动启动头脑风暴、工作树、多代理或独立计划执行流程。
5. 本项目是尚未用于任何实际项目的开发底座；在授权任务范围内，发现设计或代码不合理、需要升级时直接采用最佳工程方案，不要求向后兼容、兼容层或过渡方案，并同步更新受影响实现、测试与文档。见 [开发底座直接演进规则](rules/development-quality.md#r-20261017-direct-evolution开发底座直接演进优先)。

## 开始与完成

- 修改前检查分支、`git status`、相关实现与已批准决策，保护既有和无关改动。代码、SQL、配置或脚本任务记录 `git rev-parse HEAD`；工作区已脏或任务跨窗口时使用 `pnpm test:task:start -- <task-id>` 创建快照。
- 按 [规则索引](rules/README.md) 读取受影响章节。认证、租户、事务、持久化与公共契约变化必须覆盖相应安全、数据或契约规则；影响 Host.Api 可达路径、依赖或 AOT 配置时读取 Native AOT 规则。范围不确定时先沿调用链确认。
- 环境异常或首次运行时使用 [AI 工作流](docs/development/ai-workflow.md) 的只读自检；故障排查和前端验收沿用该文档的项目路由。查询 .NET、ASP.NET Core、SqlClient 或 SQL Server 文档时优先使用可用的 Microsoft Learn MCP，并匹配项目版本；连接不可用时回退官方文档。
- 新增或扩展模块、CRUD、Endpoint、Command/Query、Dapper 持久化或双库迁移时使用 [fullnet-module-delivery](.agents/skills/fullnet-module-delivery/SKILL.md)；性能分析与优化时使用 [fullnet-performance-hardening](.agents/skills/fullnet-performance-hardening/SKILL.md)。只读咨询或局部文字调整不因提到模块名称就触发完整交付流程。
- 行为变化在开发时明确验收场景，可同步编写测试，默认在功能批次完成后集中运行；缺陷定位所需的最小复现按需提前。文字和机械变更用直接相关的结构检查。注释使用中文，解释意图和约束，覆盖范围见 [注释规则](rules/code-comments.md)。
- 全项目验收以规定范围的本地测试实际通过为准，CI 与专用生产等价环境不再是强制前置；构建、测试、影响集与能力状态统一按 [测试与验证](rules/development-quality.md#11-测试与验证) 执行（R-20260930-local-acceptance）。入口和 Skill 不另设测试流程；未执行、失败或跳过不能报告为通过。
- 按完整功能划分任务，同一模块或约定多模块范围先集中完成关联实现，再集中测试；接口、DTO、页面和测试用例作为内部检查项，不各自触发验收或等待用户回复“继续”。跨对话统一遵守 [集中验收与构建复用](rules/development-quality.md#r-20261008-concentrated-acceptance模块批次集中验收与测试资源复用)，区分功能已实现与验收通过；已发现的安全、隔离与数据缺陷及时处理。
- 多会话只同步关键事件，冻结实际验收输入，独立开发连续推进；互不冲突的构建按资源预算允许 2～3 个并行，共享输出和未隔离的重型验收继续互斥，执行细则与入口统一见 [集中验收与构建复用](rules/development-quality.md#r-20261008-concentrated-acceptance模块批次集中验收与测试资源复用)。
- 修改任务交付前检查本任务 `git diff --check`、`git status` 和分支，报告实际变更、验证与未验证项。只同步真实受影响文档；无演进证据时无需输出规则或 Skill 状态。


## 本地构建与验收产物

- 2026-10-17 项目所有者要求清理非必要 `artifacts`，并停止后续无界生成；本地开发默认不在仓库根创建或长期保留 `artifacts/`。
- 禁止为每轮验收、诊断或源码冻结复制整仓，也不能改名放入 `.tmp`、其他目录或其他磁盘继续堆积。使用当前工作树、Git 基线/差异摘要及已有可复用构建；确需隔离时优先复用已有目录。
- 必需的临时测试输出进入独立系统临时目录，成功后清理；失败仅保留定位问题需要的最小原始记录，任务结束清理可重新生成的源码副本、依赖、二进制、临时数据库及大转储。删除前确认资源不在使用，保留源代码、现行契约和资金迁移证据。
- 正式发布导出、用户明确指定的保存位置及 CI 上传所需产物按其明确输出范围执行；本规则不关闭构建、测试或原始结果校验，也不降低验收门槛。

## Full.NET 不可隐式改变的基线

以下约束始终有效；具体实现、例外和验证按链接读取。

- 1.0 保持强化型模块化单体，API、Worker、Migrator 分离，AppHost 只负责编排；业务模块默认一个主项目，拆分须有真实消费者与证据。见 [架构与模块边界](rules/development-quality.md#3-架构与模块边界)。
- 模块内可 JOIN 并维护本地事务；跨模块不得读写对方表或建立外键、跨模块本地事务。权威读取走最小 Contract Port，跨模块写入走 Outbox、幂等、补偿与对账。见 [ADR-0002](docs/architecture/adr/ADR-0002-modular-monolith-evolution.md)。
- 业务数据访问使用 Dapper 与显式 SQL，经 Full.NET 自有执行与事务边界访问；禁止模块直连数据库、通用 Repository、自动 CRUD 或未经决策引入 EF Core。见 [数据规则](rules/development-quality.md#5-dapper事务与双数据库)。
- SQL Server 与 MySQL 为正式提供程序，数据行为变更必须成对实现与验证；当前阶段可直接调整目标结构，仍须保证部署与失败恢复正确。见 [数据规则](rules/development-quality.md#5-dapper事务与双数据库)。
- 官方逻辑主键采用应用端 UUID v7，C# 使用 `Guid`；表按 `{owner}_{module}_{entity}` 命名，官方 owner 为 `fn`，`sys` 保留，列为 PascalCase，禁止运行时动态前缀。见 [命名规范](rules/naming-conventions.md)。
- 租户来自可信上下文，权限与输入验证独立，后台页面和受保护业务操作使用稳定精确权限；前端隐藏不能替代 Endpoint 授权，超级管理员也不得绕过隔离、会话或最后一名保护。见 [安全规则](rules/development-quality.md#4-安全权限与租户隔离)与 [客户端规则](rules/client-frontend.md)。
- 对外 HTTP 使用标准状态码与 ProblemDetails，Admin.NET 包络仅在兼容层；JSON 使用 System.Text.Json，可靠事件按受控边界使用 MemoryPack。见 [契约规则](rules/development-quality.md#7-api错误与序列化契约)。
- Host.Api Native AOT 可达路径保持静态闭包；源生成、DI、Dapper、native binding 和发布状态遵守 [Native AOT 规则](rules/native-aot.md)。
- 重要可靠业务事件通过事务 Outbox 发布；缓存、日志、Trace、Metrics 和 Audit 不使用 Outbox。CDC/Kafka 仅按已批准阶段建设，保持至少一次、Inbox 幂等、单一发布所有权及切流回退门禁。见 [事件规则](rules/development-quality.md#6-并发重试幂等与-outbox)与 [ADR-0006](docs/architecture/adr/ADR-0006-transactional-outbox-cdc-kafka-event-delivery.md)。
- 缓存统一 FusionCache 与 `.AsHybridCache()`，多实例失效使用直接 L1/L2 删除、Redis Backplane 及 TTL/版本/权威源兜底，强一致类别禁用 L1。见 [缓存规则](rules/development-quality.md#8-缓存实时通信和基础设施)。
- 生产参考为 Kubernetes + Helm 多实例模块化单体，月度可用性 SLO 99.9%；开发设计目标为 1 万同时在途，未完成相应容量实测前保持 `Capacity-not-verified`，本地测试可作为验收证据，结论注明硬件与范围。见 [ADR-0005](docs/architecture/adr/ADR-0005-high-concurrency-modular-monolith-multi-instance-production-baseline.md)。
- Admin.NET 仅作功能参考，不隐式改变架构或发布许可；框架采用 MIT，第三方及 Admin.NET.Pro 代码和资源须符合再分发授权。见 [许可规则](rules/development-quality.md#122-一般文档依赖与发布要求)。
- Vue `ui/admin` 是后台唯一持续交付线；Layui `ui/admin-layui` 冻结，仅允许明确授权的安全修复、迁移或退役。见 [客户端规则](rules/client-frontend.md)。
- 多语言使用规范 BCP 47 与稳定机器码，业务不依赖译文；种子数据分生产安全 Baseline 与环境 Overlay，Production 仅允许 Baseline，API/Worker 不播种。见 [多语言规则](rules/development-quality.md#r-20260717-full-stack-localization-boundary多语言必须覆盖协议组件库和服务端生成文本)与 [种子规则](rules/development-quality.md#r-20260717-seed-data-boundary生产-baseline环境-overlay-与场景测试数据必须分层)。
