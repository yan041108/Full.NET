# Full.NET 仓库开发规则

本文件适用于仓库根目录及全部子目录，是开发代理进入 Full.NET 后必须读取的项目入口。详细规则位于 [`rules/`](rules/README.md)。

## 指令优先级

1. 系统、开发者和当前用户指令始终高于本文件及 `rules/`。
2. 子目录若存在更具体的 `AGENTS.md`，仅对该子目录追加或收紧约束；冲突时服从更高层级规则。
3. 规则不能扩大任务授权。涉及发布、外部写入、破坏性操作或范围外变更时，仍须取得相应授权。

## 每项任务必须执行

### 开始前

1. 必须读取本文件和 [`rules/README.md`](rules/README.md)。
2. 必须读取 [`rules/development-quality.md`](rules/development-quality.md)；涉及代码、SQL、配置或脚本时，还必须读取 [`rules/code-comments.md`](rules/code-comments.md)；新增或修改数据库对象、公共标识符、API/JSON、稳定机器码、配置键、缓存键或生成器产物时，还必须读取 [`rules/naming-conventions.md`](rules/naming-conventions.md)；修改 Host.Api 可达代码或依赖、AOT 编译条件、JSON/配置源生成、Dapper AOT、Provider native binding、AOT 测试或工作流时，还必须读取 [`rules/native-aot.md`](rules/native-aot.md)。
3. 必须检查 `.agents/skills/` 是否存在匹配当前任务的项目 Skill；新增或扩展模块、CRUD、Endpoint、Command/Query、Dapper 持久化或双库迁移时必须使用 [`fullnet-module-delivery`](.agents/skills/fullnet-module-delivery/SKILL.md)；性能分析、基准、负载测试或请求/SQL/缓存/Worker/客户端包体优化必须使用 [`fullnet-performance-hardening`](.agents/skills/fullnet-performance-hardening/SKILL.md)。
4. 必须检查当前分支、`git status`、相关设计与计划，保留用户已有和无关变更。代码、SQL、配置或脚本任务必须记录 `git rev-parse HEAD`；工作区已脏或任务会跨多个窗口时，还必须运行 `pnpm test:task:start -- <task-id>` 创建任务快照，避免把既有改动混入影响集。
5. 必须确认需求、授权边界和验收条件；能从仓库安全确定的信息不得反复询问。
6. 产生或更新评估、规格、决策、计划或验证记录时，必须遵循 [`rules/development-quality.md`](rules/development-quality.md) 第 12.1 节的文档产物分层。

### 开发中

1. 行为变更和缺陷修复必须先建立可失败的验证，再实现最小正确变更。
2. 必须保持模块边界、租户隔离、数据一致性以及 SQL Server/MySQL 双提供程序约束。
3. 代码标识符使用英文，所有手写注释（含 XML 文档注释）使用清晰中文并解释意图、边界、不变量或风险，禁止逐行复述。后端新增或修改的类、记录、结构、接口、枚举、构造函数和方法，不分可见性，必须提供中文 XML 文档注释；枚举成员必须有中文说明，每个方法或构造函数参数必须有对应的中文 `<param>`；关键业务逻辑代码块必须在最接近实现的位置添加中文注释，说明业务规则、决策原因或必须保持的约束。细则与有限例外见 [`rules/code-comments.md`](rules/code-comments.md)。
4. 不得静默更改公共 API、序列化契约、数据库结构、兼容适配器或许可证边界。

### 完成前

1. 必须执行与风险相称的构建、测试和静态检查，并依据新鲜输出报告结果。默认先在本地运行编译、静态检查、治理测试和不依赖容器的直接单元/架构测试；工作区已脏时使用 `pnpm test:integration:affected:plan -- --snapshot <task-id> --phase <inner|slice|merge>` 审查影响集，干净单窗口任务可把 `--snapshot <task-id>` 替换为 `--base <任务基线>`。Docker/Testcontainers、SQL Server/MySQL 双库 Integration、Kafka/CDC/Capacity、真实浏览器和 Linux Native AOT 等环境重型验证，取得提交与推送授权后默认交给 GitHub Actions，必须核对目标提交的必需工作流并修复失败；只有定位 CI 失败、GitHub Actions 不可用或用户明确要求时，才在本地运行选择器命中的环境重型影响集。本地禁止用 `test:e2e:real`、完整 `test:e2e:admin`、`test:integration:full` 或 `messaging-heavy` 冒充内循环，完整集合只保留给 `main` CI 的互斥并行分片门禁。细则见 [`rules/development-quality.md`](rules/development-quality.md) R-20260816-local-test-inner-budget 与 R-20260903-github-actions-first-verification。
2. 只更新被行为、配置、迁移或使用方式真实影响的 README、开发文档和路线图；测试数量只修改 [`eng/testing/test-matrix.json`](eng/testing/test-matrix.json)，禁止在多份文档复制门槛。
3. 按 [`rules/rule-evolution.md`](rules/rule-evolution.md) 检查是否命中用户纠正、重复失败、高风险新类别或规则冲突；未命中时只在交付中写一行结论，不更新规则候选。
4. 只有命中 [`rules/skill-evolution.md`](rules/skill-evolution.md) 的真实 Skill 缺口或里程碑集中复盘时才修改 Skill 或候选；普通任务禁止机械累计次数。
5. 必须检查 `git diff --check`、`git status` 和分支状态，不得把“测试未执行”表述为“测试通过”。

## Full.NET 不可隐式改变的基线

下列基线只陈述不变量并指向唯一权威源；执行细则与验证方式以链接的 `rules/` 文件或 ADR 为准，不在此内联复述，避免双写漂移。

- Full.NET 1.0 保持强化型模块化单体，API、Worker、Migrator 按运行角色分离，AppHost 只负责编排，禁止全面微服务化或提前引入网络边界；拆分门禁见 [`rules/development-quality.md`](rules/development-quality.md) 第 3 节与 [`ADR-0002`](docs/architecture/adr/ADR-0002-modular-monolith-evolution.md)。
- 模块内查询可以关联本模块表并由本模块本地事务维护强不变量；跨模块禁止读写、JOIN 或外键关联对方表，新流程禁止依赖跨模块本地事务。立即权威读取使用最小 Contract Port，高频读取使用版本化事件与本地投影，跨模块写入使用 Outbox、幂等、补偿和对账；完整标准见 [`ADR-0002`](docs/architecture/adr/ADR-0002-modular-monolith-evolution.md#模块内模块间数据关联与事务标准)与[总体架构 Spec §5.3、§9](docs/superpowers/specs/2026-07-17-fullnet-architecture-design.md#53-模块通信规则)。
- 业务模块物理拓扑默认采用“一个主项目＋按证据可选 Contracts/传输适配项目”；小功能、CRUD、实体、菜单和用例只能作为主项目内的垂直切片，禁止按功能机械增加 `.csproj`；项目拆分门禁见 [`rules/development-quality.md`](rules/development-quality.md) 第 3 节与[总体架构 Spec §4.2](docs/superpowers/specs/2026-07-17-fullnet-architecture-design.md#42-解决方案结构)。
- 业务数据访问默认使用 Dapper 与显式 SQL，未经明确架构决策不得引入 EF Core；细则见 [`rules/development-quality.md`](rules/development-quality.md) 第 5 节。
- Dapper 辅助能力只允许通过 Full.NET 自有边界使用，业务模块禁止直连数据库、通用 Repository 或自动 CRUD；禁用清单与验证以 [`rules/development-quality.md`](rules/development-quality.md) R-20260718-dapper-tooling-boundary 为准。
- 当前需要事务原子性和可靠重试的重要业务 Integration Event 只通过事务 Outbox 发布；缓存失效、日志、Trace、Metrics、普通 HTTP Operation Log 和 Audit 禁止使用 Outbox。项目已批准提前实施“追加式 Outbox + CDC Relay + Kafka + 消费 Inbox”，但只允许按 ADR-0006 分阶段建设和影子验证；生产切流前必须完成双库 CDC、至少一次幂等、单一发布所有权、排空、回退和运维门禁，不得按瞬时 QPS 动态改变可靠性语义；细则见 [`rules/development-quality.md`](rules/development-quality.md) 第 6、8、9 节、[`ADR-0006`](docs/architecture/adr/ADR-0006-transactional-outbox-cdc-kafka-event-delivery.md)与[事件交付 Spec](docs/superpowers/specs/2026-08-08-transactional-outbox-cdc-kafka-design.md)。
- 数据库正式支持 SQL Server 与 MySQL，数据库行为变更必须同时验证两者；细则见 [`rules/development-quality.md`](rules/development-quality.md) 第 5、11 节。
- Full.NET 官方表逻辑主键为应用端生成的 UUID v7，C# 与业务模块只使用 `Guid`；物理类型、字节序、聚集索引与转换边界以 [`rules/naming-conventions.md`](rules/naming-conventions.md) 第 4、5 节为准。
- 数据库表采用 `{owner}_{module}_{entity}`（官方 OwnerKey 固定为 `fn`，`sys` 保留，禁止运行时动态表前缀），列使用 PascalCase 与 Dapper 投影直接映射；完整命名以 [`rules/naming-conventions.md`](rules/naming-conventions.md) 为准。
- 对外 HTTP API 使用标准状态码与 ProblemDetails，Admin.NET 统一包络只存在于兼容适配层；细则见 [`rules/development-quality.md`](rules/development-quality.md) 第 7 节。
- JSON 使用 System.Text.Json，可靠 Integration Event 按既定边界使用 MemoryPack，服务契约可使用 gRPC；细则见 [`rules/development-quality.md`](rules/development-quality.md) 第 7 节与 [`ADR-0008`](docs/architecture/adr/ADR-0008-api-native-aot-runtime-boundary.md)。
- Host.Api Native AOT 可达路径必须保持静态闭包；JSON 元数据、闭合泛型 DI、Dapper 参数/物化、第三方 native binding 和状态声明必须通过对应分析、Linux publish 与原生进程门禁，细则见 [`rules/native-aot.md`](rules/native-aot.md)、[`ADR-0008`](docs/architecture/adr/ADR-0008-api-native-aot-runtime-boundary.md)与[`ADR-0009`](docs/architecture/adr/ADR-0009-host-api-native-aot-provider-runtime-boundary.md)。
- 缓存以 FusionCache 为唯一实现并通过 `.AsHybridCache()` 暴露双抽象；多实例失效采用当前实例 L1/L2 删除 + Redis Backplane + TTL/版本/权威源兜底，强一致类别禁用 L1；细则见 [`rules/development-quality.md`](rules/development-quality.md) 第 8 节。
- 成熟生产参考采用 Kubernetes + Helm 的模块化单体多实例拓扑，月度可用性 SLO 为 99.9%；开发阶段以 1 万同时在途为设计目标但不承担容量达标门禁，专用生产等价环境认证前必须标记 `Capacity-not-verified`。正式边界见 [`ADR-0005`](docs/architecture/adr/ADR-0005-high-concurrency-modular-monolith-multi-instance-production-baseline.md) 与[总体架构 Spec §20.5](docs/superpowers/specs/2026-07-17-fullnet-architecture-design.md#205-性能基线)。
- 后续功能以 Admin.NET 为功能参考目标，但实现必须遵守 Full.NET 的架构、安全和发布许可边界；对标方式见 [`rules/development-quality.md`](rules/development-quality.md) 第 3 节。
- 默认引导账号属于受保护的 `host-administrator` 超级管理员系统角色，动态投影授权目录权限且不得绕过租户隔离、账号/会话状态、精确 Endpoint 权限、审计与最后一名保护；细则以 [`rules/development-quality.md`](rules/development-quality.md) R-20260718-super-administrator-boundary 为准。
- Vue 主管理端 `ui/admin` 是后台产品的唯一持续交付线；Layui 管理端 `ui/admin-layui` 自 2026-08-02 起进入存量冻结，禁止新增或扩展业务功能，也不再参与新功能的 `Verified` 门槛；只有明确授权的安全修复、迁移或退役任务可以修改。细则见 [`rules/client-frontend.md`](rules/client-frontend.md) 第 2、5 节。
- 后台页面与所有调用受保护 API、读取敏感数据或产生业务副作用的操作必须使用独立稳定权限码；无权限时 Vue 不创建对应操作入口，直接绕过客户端调用仍必须由精确 Endpoint 权限失败关闭；角色授权页必须能按“模块/页面/操作”分层授权。细则见 [`rules/client-frontend.md`](rules/client-frontend.md) 第 3 节与 [`rules/development-quality.md`](rules/development-quality.md) R-20260802-admin-action-authorization。
- 多语言采用“统一治理、平台原生实现”，全栈使用规范 BCP 47 语言标签与稳定机器码，业务逻辑不得依赖翻译文本，完成状态按跨端验证如实标记；细则以 [`rules/development-quality.md`](rules/development-quality.md) R-20260717-full-stack-localization-boundary 为准。
- 种子数据采用“生产安全 Baseline＋环境 Overlay”，Production 只允许 Baseline，API/Worker 不得启动播种，Contributor 必须幂等且通过双库验证；细则以 [`rules/development-quality.md`](rules/development-quality.md) R-20260717-seed-data-boundary 为准。

## 详细规则索引

- [`rules/README.md`](rules/README.md)：规则范围、用词和维护方式。
- [`rules/code-comments.md`](rules/code-comments.md)：中文代码注释与文档注释规范。
- [`rules/development-quality.md`](rules/development-quality.md)：常见遗漏防护和完成定义。
- [`rules/performance-engineering.md`](rules/performance-engineering.md)：性能证据、请求链、双库、Worker 与客户端包体门禁。
- [`rules/naming-conventions.md`](rules/naming-conventions.md)：数据库、C#、API、机器码、配置、缓存和生成器的统一命名边界。
- [`rules/native-aot.md`](rules/native-aot.md)：Host.Api Native AOT 静态闭包、编码约束、第三方依赖与发布/E2E 门禁。
- [`rules/client-frontend.md`](rules/client-frontend.md)：Vue 单一后台交付线、Layui 存量冻结、逐页面/逐操作权限，以及 uni-app、Flutter 与桌面端的框架、UI、许可和验收边界。
- [`rules/rule-evolution.md`](rules/rule-evolution.md)：自动复盘、规则升级、冲突与退役机制。
- [`rules/skill-evolution.md`](rules/skill-evolution.md)：项目 Skills 候选、测试先行、升级与退役机制。
- [`.agents/skills/fullnet-module-delivery`](.agents/skills/fullnet-module-delivery/SKILL.md)：完整业务模块纵向交付流程。
- [`.agents/skills/fullnet-performance-hardening`](.agents/skills/fullnet-performance-hardening/SKILL.md)：性能基线、瓶颈定位、语义门禁与验证流程。
