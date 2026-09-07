# Full.NET 仓库开发规则

本文件适用于仓库根目录及全部子目录。先遵守下列底线，再由 [规则索引](rules/README.md) 按任务加载细则；已在当前上下文读取且未变化的内容无需重读。

## 指令与执行范围

1. 系统、开发者和当前用户指令优先；项目规则与 Skill 不扩大任务授权。子目录规则仅适用于对应范围，不能静默放宽安全、数据、许可和验证底线。
2. 先区分咨询、只读审查与实施。只读任务直接交付分析，不修改文件、创建任务快照或运行无关构建。已授权且需求明确的修改直接推进，不为例行流程重复索要确认。
3. 小型、清晰任务直接执行；复杂、跨模块或高风险任务先列计划。普通计划留在会话，永久文档按 [文档产物分层](rules/development-quality.md#121-文档产物分层) 创建。
4. Skill 按真实任务需要选择：缺陷先定位原因，行为变化先建立可失败验证，完成前核对新鲜证据；不自动启动头脑风暴、工作树、多代理或独立计划执行流程。

## 开始与完成

- 修改前检查分支、`git status`、相关实现与已批准决策，保护既有和无关改动。代码、SQL、配置或脚本任务记录 `git rev-parse HEAD`；工作区已脏或任务跨窗口时使用 `pnpm test:task:start -- <task-id>` 创建快照。
- 按 [规则索引](rules/README.md) 读取受影响章节。认证、租户、事务、持久化与公共契约变化必须覆盖相应安全、数据或契约规则；影响 Host.Api 可达路径、依赖或 AOT 配置时读取 Native AOT 规则。范围不确定时先沿调用链确认。
- 新增或扩展模块、CRUD、Endpoint、Command/Query、Dapper 持久化或双库迁移时使用 [fullnet-module-delivery](.agents/skills/fullnet-module-delivery/SKILL.md)；性能分析与优化时使用 [fullnet-performance-hardening](.agents/skills/fullnet-performance-hardening/SKILL.md)。只读咨询或局部文字调整不因提到模块名称就触发完整交付流程。
- 行为变化先建立失败测试或可复现实验；文字和机械变更用直接相关的结构检查。注释使用中文，解释意图和约束，覆盖范围见 [注释规则](rules/code-comments.md)。
- 构建、测试、影响集、GitHub Actions 与能力状态统一按 [测试与验证](rules/development-quality.md#11-测试与验证) 执行（含 R-20260903-github-actions-first-verification）。入口和 Skill 不另设测试流程；未执行、失败或跳过不能报告为通过。
- 修改任务交付前检查本任务 `git diff --check`、`git status` 和分支，报告实际变更、验证与未验证项。只同步真实受影响文档；无演进证据时无需输出规则或 Skill 状态。

## Full.NET 不可隐式改变的基线

以下约束始终有效；具体实现、例外和验证按链接读取。

- 1.0 保持强化型模块化单体，API、Worker、Migrator 分离，AppHost 只负责编排；业务模块默认一个主项目，拆分须有真实消费者与证据。见 [架构与模块边界](rules/development-quality.md#3-架构与模块边界)。
- 模块内可 JOIN 并维护本地事务；跨模块不得读写对方表或建立外键、跨模块本地事务。权威读取走最小 Contract Port，跨模块写入走 Outbox、幂等、补偿与对账。见 [ADR-0002](docs/architecture/adr/ADR-0002-modular-monolith-evolution.md)。
- 业务数据访问使用 Dapper 与显式 SQL，经 Full.NET 自有执行与事务边界访问；禁止模块直连数据库、通用 Repository、自动 CRUD 或未经决策引入 EF Core。见 [数据规则](rules/development-quality.md#5-dapper事务与双数据库)。
- SQL Server 与 MySQL 为正式提供程序，数据行为变更必须成对实现与验证；迁移须保持恢复和兼容策略。见 [数据规则](rules/development-quality.md#5-dapper事务与双数据库)。
- 官方逻辑主键采用应用端 UUID v7，C# 使用 `Guid`；表按 `{owner}_{module}_{entity}` 命名，官方 owner 为 `fn`，`sys` 保留，列为 PascalCase，禁止运行时动态前缀。见 [命名规范](rules/naming-conventions.md)。
- 租户来自可信上下文，权限与输入验证独立，后台页面和受保护业务操作使用稳定精确权限；前端隐藏不能替代 Endpoint 授权，超级管理员也不得绕过隔离、会话或最后一名保护。见 [安全规则](rules/development-quality.md#4-安全权限与租户隔离)与 [客户端规则](rules/client-frontend.md)。
- 对外 HTTP 使用标准状态码与 ProblemDetails，Admin.NET 包络仅在兼容层；JSON 使用 System.Text.Json，可靠事件按受控边界使用 MemoryPack。见 [契约规则](rules/development-quality.md#7-api错误与序列化契约)。
- Host.Api Native AOT 可达路径保持静态闭包；源生成、DI、Dapper、native binding 和发布状态遵守 [Native AOT 规则](rules/native-aot.md)。
- 重要可靠业务事件通过事务 Outbox 发布；缓存、日志、Trace、Metrics 和 Audit 不使用 Outbox。CDC/Kafka 仅按已批准阶段建设，保持至少一次、Inbox 幂等、单一发布所有权及切流回退门禁。见 [事件规则](rules/development-quality.md#6-并发重试幂等与-outbox)与 [ADR-0006](docs/architecture/adr/ADR-0006-transactional-outbox-cdc-kafka-event-delivery.md)。
- 缓存统一 FusionCache 与 `.AsHybridCache()`，多实例失效使用直接 L1/L2 删除、Redis Backplane 及 TTL/版本/权威源兜底，强一致类别禁用 L1。见 [缓存规则](rules/development-quality.md#8-缓存实时通信和基础设施)。
- 生产参考为 Kubernetes + Helm 多实例模块化单体，月度可用性 SLO 99.9%；开发设计目标为 1 万同时在途，生产等价认证前保持 `Capacity-not-verified`。见 [ADR-0005](docs/architecture/adr/ADR-0005-high-concurrency-modular-monolith-multi-instance-production-baseline.md)。
- Admin.NET 仅作功能参考，不隐式改变架构或发布许可；框架采用 MIT，第三方及 Admin.NET.Pro 代码和资源须符合再分发授权。见 [许可规则](rules/development-quality.md#122-一般文档依赖与发布要求)。
- Vue `ui/admin` 是后台唯一持续交付线；Layui `ui/admin-layui` 冻结，仅允许明确授权的安全修复、迁移或退役。见 [客户端规则](rules/client-frontend.md)。
- 多语言使用规范 BCP 47 与稳定机器码，业务不依赖译文；种子数据分生产安全 Baseline 与环境 Overlay，Production 仅允许 Baseline，API/Worker 不播种。见 [多语言规则](rules/development-quality.md#r-20260717-full-stack-localization-boundary多语言必须覆盖协议组件库和服务端生成文本)与 [种子规则](rules/development-quality.md#r-20260717-seed-data-boundary生产-baseline环境-overlay-与场景测试数据必须分层)。
