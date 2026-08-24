# Full.NET 开发质量与遗漏防护规则

## 1. 完成的定义

代码能编译只是最低门槛。任务只有在需求覆盖、风险处理、分层验证证据、必要文档同步、治理触发检查和 Git 状态均清楚后才能声明完成。无法执行的验证必须明确列为未验证项，禁止把“预计通过”写成“已通过”。

### 当前已识别的高频遗漏

本规则基于 Full.NET 前期开发与审查中最容易遗漏的事项形成，后续发现必须按规则演进机制继续补充或自动化：

| 高频遗漏 | 造成的风险 | 对应防护 |
| --- | --- | --- |
| 只实现主路径，未逐项映射需求 | 功能看似完成但验收缺项 | 第 2、14 节 |
| 注释复述代码、语言混用或随实现过期 | 关键设计意图无法维护 | `code-comments.md` |
| DI 生命周期、启动顺序和健康检查只完成注册表面 | 运行时作用域错误或假健康 | 第 3、8 节 |
| 租户标识来自不可信输入，验证代替授权 | 越权与跨租户数据泄露 | 第 4 节 |
| 事务只覆盖业务表，Outbox、外部调用或失败路径遗漏 | 数据与消息不一致 | 第 5、6 节 |
| SQL Server 完成后假设 MySQL 等价，或反之 | 迁移、锁和 SQL 在另一数据库失败 | 第 5、11 节 |
| 只验证迁移日志幂等，未模拟未记账的部分 DDL | 非事务或隐式提交 DDL 失败后无法重跑，导致生产升级不可恢复 | 第 5、11 节 |
| 恢复测试用排除未来脚本的黑名单表达迁移边界 | 新增迁移后旧恢复用例越界执行，产生与目标迁移无关的结构冲突 | R-20260726-migration-recovery-test-boundary |
| 只看构建成功，未确认测试运行器和测试数量 | 零测试或漏测被误报为通过 | 第 11 节 |
| 缓存、Worker、SignalR 只按单实例思考 | 横向扩展后重复处理或状态不一致 | 第 6、8 节 |
| README 和路线图把计划能力写成已实现 | 使用者产生错误预期 | 第 12 节 |
| 只看直接依赖许可证，未核对发布物和授权范围 | MIT 发布时产生合规风险 | 第 12 节 |
| 完成后遗留分支、无关差异、换行或绝对路径 | 合并污染与跨平台失败 | 第 13 节 |
| 凭感觉宣称性能改善，缺少基准和真实数据规模 | 复杂度增加但收益不可证实 | 第 5、9 节 |
| 浏览器 E2E 只模拟 API，未验证真实带凭据预检 | 本地/生产登录被拦截或错误放宽跨域权限 | R-20260717-credentialed-cors |
| 客户端把服务端动态导航当作可执行配置 | 任意组件加载、路由混淆或 XSS | R-20260717-client-navigation-boundary |
| 用页面可见性或粗粒度 `*.write` 代替逐操作授权 | 无权限按钮仍可见、直接调用 API 越权或角色无法最小授权 | R-20260802-admin-action-authorization |
| 只替换管理壳层文案，遗漏组件库、服务端生成文本和其他客户端 | 各端语言状态漂移，错误与后台任务无法按用户语言交付，却被误报为全栈已支持 | R-20260717-full-stack-localization-boundary |
| 没有区分生产 Baseline、环境 Overlay 和场景测试数据 | 默认密码、生产误播种、测试耦合和重复脏数据 | R-20260717-seed-data-boundary |
| 把框架、项目和数据库系统对象都命名为 `sys_*`，或在各模板重复实现命名转换 | 所有权混淆、跨库大小写故障、Dapper 隐式映射和生成漂移 | `naming-conventions.md` |
| 用用户名、通配符或无条件授权实现超级管理员，或允许移除最后一名 | 权限绕过、租户泄漏和平台失去恢复入口 | R-20260718-super-administrator-boundary |
| 在业务模块直接引入 Dapper 扩展、连接、事务或自动 CRUD | 绕过租户守卫、事务/Outbox 和 SQL 审查边界 | R-20260718-dapper-tooling-boundary |
| 认证前或租户上下文内读取 Host 目录，却把 SQL 误标为 `HostOnly` | API Key 等认证入口不可用，或进入租户后 Host 目录查询异常 | R-20260726-host-catalog-sql-scope |
| 把每个 CRUD、菜单、实体或用例拆成独立项目，或把 `Contracts`/`.Http` 当作模块标配 | 项目数量膨胀、构建与装配成本上升，并形成没有业务意义的物理边界 | 第 3 节 |
| 每次代码迭代都跑完整浏览器 E2E、真实栈或 Integration 全量 | 内循环被无关套件拖成数十分钟，真正缺陷被噪音淹没 | R-20260816-local-test-inner-budget |

## 2. 任务开始与范围控制

1. 必须先确认当前请求是分析、诊断、实现、审查、合并还是发布；不得把只读请求扩展为代码修改。
2. 必须检查 `git status`、当前分支、最近提交和适用的 `AGENTS.md`，保护用户已有改动。
3. 多步骤任务必须建立可追踪计划；实现必须能映射到用户需求或已批准设计。
4. 不确定项应优先从代码、测试和文档中查证。会显著改变功能、数据或外部状态的假设必须停止并取得授权。
5. 禁止顺手重构无关区域、批量改格式或删除不明文件。

### R-20260730-generated-artifact-delete-boundary：生成器删除产物必须先声明所有权并保留恢复证据

- 状态：强制
- 来源：CodeGeneration 陈旧产物删除审查发现，路径验哈希后直接删除以及 rename 后只验一次哈希，都可能被编辑器路径替换或保留写句柄穿透并造成用户修改丢失
- 适用范围：代码生成器、脚手架、迁移器和仓库脚本对既有文件的自动删除、替换或清理
- 风险：检查与删除分离、覆盖恢复或提交后静默清理会不可逆删除人工修改，并让清单状态与磁盘内容失配
- 规则：自动删除必须由上一版稳定清单按精确路径和摘要证明所有权，禁止对未拥有、已修改或大小写别名文件执行删除。执行时必须先以同卷无覆盖 rename 声明目录项，再对声明文件复验摘要；失败恢复不得覆盖重新出现的目标。清单必须最后提交；提交前必须再次验证声明文件，提交后默认生成流程只能将待恢复阶段元数据原子切换为已提交墓碑，并保留配对 recovery，不得自动物理 unlink。有效的已提交墓碑不得阻断后续生成；待恢复、无效或孤立配对必须保留证据并失败关闭。物理清理只能作为独立、显式授权且可审计的操作，在目标平台证明文件身份和内容后执行；禁止使用“先检查再 `File.Delete`”、依赖不可移植的共享句柄语义或吞掉提交后的阶段转换异常
- 验证：`GenerationWritePlannerTests` 覆盖所有权、人工修改、缺失与路径别名；`GenerationWorkspaceStoreTests` 覆盖 claim 前替换、claim 后保留句柄写入、manifest 后写入、取消、并发恢复、无效 UTF-8、阶段转换、孤立配对和中断 recovery
- 例外：无。明确由用户请求删除的非生成文件仍必须遵守更高层授权与破坏性操作边界，不能借本规则扩大删除范围

## 3. 架构与模块边界

1. Full.NET 采用模块化单体优先的架构；模块通过明确契约通信，禁止直接引用其他模块的内部实现或表结构。生产模块的项目引用只能指向其他模块的公开 Contracts，禁止通过具体 `Module` 类型、实现项目引用或 `InternalsVisibleTo` 建立跨模块依赖；具体模块入口只允许由 Composition 组合根引用。
2. 一个内聚业务边界默认只创建一个 `Full.NET.Modules.<Module>` 主项目；小功能、CRUD、实体、菜单、Command/Query 和 Endpoint 必须作为主项目内的目录或垂直切片，禁止按功能机械增加 `.csproj`。业务边界按数据所有权、业务不变量、生命周期和公开契约判断，不得仅按前端菜单分组，也不得为了减少项目数把无关能力塞入大杂烩模块。
3. `Full.NET.Modules.<Module>.Contracts` 只有在存在至少一个真实跨模块或外部编译期消费者，并且需要以程序集隔离稳定公开接口、DTO、权限定义或集成事件时才可创建；没有消费者时 `Contracts/` 只是主项目内目录。禁止为了未来可能复用提前创建 Contracts 项目，禁止把业务契约上移到 BuildingBlocks 以减少项目数量。
4. `.Http`、`.Worker` 或其他传输/运行适配项目只有在同一模块核心被非该传输宿主真实复用，且程序集隔离能带来可验证的依赖、打包或安全收益时才可创建。API、Worker、Migrator 的运行角色分离本身不构成适配项目拆分证据；应先在主项目中使用显式注册入口和 Host Profile 控制能力集合。新增可选项目必须在已批准 Spec 或计划中列出真实消费者、依赖方向、收益和架构测试；缺少任一项时保持一个主项目。
5. 依赖方向必须从 Host/Module 指向 BuildingBlocks 抽象与实现，抽象层不得反向依赖基础设施或宿主。
6. 新横切能力必须先判断应属于 Abstractions、BuildingBlocks、Compatibility 还是具体 Module，禁止把业务逻辑堆入 Host。
7. 服务注册必须审查生命周期、线程安全和作用域捕获；禁止 Singleton 直接持有 Scoped 服务或请求级租户状态。
8. 启动、迁移和后台 Worker 的职责必须分离；API Host 不得引用、注册、解析或执行 DbUp 迁移能力，迁移只能由 Migrator 或显式测试基础设施执行。Migrator 只允许装配 Migration/Seed 与 Contributor 的最小依赖闭包，不得复用 API Profile 装入认证、授权、CORS、限流或 HTTP Endpoint 服务。
9. 参考 `dotnet/eShop` 时只吸收可解释的架构思想，不机械复制微服务复杂度；参考 Admin.NET 时对标功能，不复制其耦合方式。
10. 宿主注册完整模块时必须同时注册该模块声明的全部 `Dependencies`；模块排序依赖必须使用稳定模块键而不是另一个模块的具体类型。Worker 或 Migrator 只消费后台/迁移能力时，应由模块提供并使用可验证的最小服务注册入口，禁止为了单个消费者或 Contributor 注册完整 HTTP 模块或留下不完整依赖图。
11. Full.NET 官方 Api、Worker、Migrator 必须通过 `Full.NET.Composition` 的显式 Host Profile 选择模块能力；新增模块只能更新共享目录和对应测试，禁止在各宿主 `Program.cs` 恢复手工完整模块清单。Composition 可以依赖具体 Modules，通用 BuildingBlock 禁止反向引用业务模块。
12. Full.NET 1.0 必须保持强化型模块化单体以及 API、Worker、Migrator 运行角色分离；局部模块只有满足 [`ADR-0002`](../docs/architecture/adr/ADR-0002-modular-monolith-evolution.md) 的全部拆分门禁并新增独立 ADR 后才能拆分。禁止用未来扩容、团队增长或技术偏好代替可测量证据，禁止把角色分离误报为微服务能力。
13. 模块内读取可以关联本模块拥有的表；跨模块读取只能使用最小只读 Port/公开 Contract 的批量投影，或由版本化 Integration Event 建立消费方本地投影。禁止通过直接 SQL、视图、同义词、存储过程、触发器或动态 SQL 读取/写入其他模块的表，也禁止跨模块数据库外键。`015_HostRoleDataScope` 的 Identity → Organization 外键是 ADR-0002 已登记的存量债务，只允许按硬化计划移除，不构成新增例外。
14. 新业务流程不得依赖跨模块本地事务。跨模块强不变量必须收敛到唯一数据所有者；其他模块通过 Contract 获取当下权威回答，或通过 Outbox 与幂等消费者最终一致。共享数据库、Scoped `DbSession` 或技术上可以加入同一事务均不构成例外。
15. 领域参数归执行相应业务不变量的模块所有，必须使用强类型、作用域、版本和生效语义表达。Settings 只承载平台通用设置与管理能力，禁止把预约、支付、工作流等业务参数降级为无所有者的字符串或任意 JSON 配置。

## 4. 安全、权限与租户隔离

1. 认证、授权、数据过滤和输入验证必须分别实现；通过验证不代表拥有权限。
2. 租户标识必须来自受信任的认证上下文或经过授权的管理操作，禁止直接信任请求体、查询字符串或客户端 Header。
3. 所有租户数据查询、更新、缓存键、Outbox 消息和实时连接分组必须包含租户边界；全局管理查询必须显式命名和授权。
4. 管理端点必须执行权限策略，不得仅依赖前端隐藏菜单或路由。
5. 日志、ProblemDetails、追踪和 AI 上下文禁止泄露密码、令牌、连接串、密钥、个人敏感信息或内部堆栈。
6. 上传、导入、模板、表达式、反射和动态执行功能必须检查路径穿越、内容类型、大小限制、注入和资源耗尽。
7. 依赖、镜像和工具版本必须可追踪；发现高危漏洞时必须评估影响并记录处置结果。Critical 漏洞不得通过例外放行；确需暂时接受 High 漏洞时，必须使用入库策略精确限制公告编号、包名、依赖路径、缓解措施和复核截止日，并由 CI 在官方数据源重新审计。禁止使用无期限、无路径边界或全局忽略参数绕过审计。

### R-20260717-credentialed-cors：带凭据浏览器客户端必须验证精确 CORS 边界

- 状态：强制
- 来源：Identity 双管理端会话审查发现，Playwright 路由模拟掩盖了真实 API 缺失 CORS 响应的问题
- 适用范围：任何与 API 不同 Origin 且携带 Cookie、Authorization 或其他凭据的浏览器客户端
- 风险：配置过窄会导致浏览器在 Endpoint 前阻断请求；配置过宽会造成跨站凭据暴露、Login CSRF 或未授权来源调用
- 规则：必须使用显式、精确的受信 Origin 列表，禁止为凭据请求使用任意 Origin；策略必须读取宿主最终 Options 配置，并在认证授权前进入中间件管道。登录、Refresh、Logout 等匿名认证/会话写端点还必须执行精确 Origin 校验并使用有界命名限流策略，拒绝状态必须为 429；Refresh/Logout 继续要求独立 CSRF 防护。前端路由模拟 E2E 不能替代真实 Host 预检验证
- 验证：集成测试必须分别发送受信和不受信 Origin 的 `OPTIONS` 预检，断言受信来源返回精确 `Access-Control-Allow-Origin` 与凭据头，不受信来源不返回允许头；真实 API 测试还必须覆盖不可信 Origin 的认证/会话写请求和限流拒绝状态
- 例外：浏览器与 API 严格同源且部署验证能证明不会发生跨域请求时，可不启用 CORS，但必须在部署文档中明确同源约束

### R-20260717-client-navigation-boundary：动态导航只能映射本地可信能力

- 状态：强制
- 来源：Identity 租户上下文与双管理端动态权限导航交付
- 适用范围：Vue、冻结的 Layui 存量端及后续任何消费服务端菜单、组件、路由或按钮元数据的客户端
- 风险：账号或服务端数据一旦被污染，客户端可能加载任意组件、跳转未声明路由、注入 HTML，或把前端可见性错误当成服务端授权
- 规则：服务端动态导航必须通过严格运行时结构校验，并按语义标识映射到客户端本地维护的精确组件、路由和路径白名单；未知标识必须拒绝，禁止动态导入任意路径、执行字符串代码或插入未清理 HTML。动态导航和按钮隐藏只负责体验，管理端点仍必须执行服务端权限策略
- 验证：共享契约测试覆盖畸形输入，Vue 单元测试覆盖精确白名单和安全文本渲染，Vue E2E 必须覆盖未知组件拒绝、无权限和服务端错误；Layui 仅在明确授权修改其冻结代码时运行聚焦回归
- 例外：无。静态公开页面可以不请求动态导航，但仍必须使用本地声明路由

### R-20260802-admin-action-authorization：后台页面与业务操作必须端到端精确授权

- 状态：强制
- 来源：项目所有者 2026-08-02 明确要求后台页面上的业务按钮均可单独授权；无权限按钮不得显示，绕过客户端直接调用仍必须由后端拦截，角色授权页必须同时授权页面与按钮
- 适用范围：所有后台管理模块、授权目录、角色权限持久化、Vue 管理端、管理 API、OpenAPI/客户端契约、代码生成器和相关测试
- 风险：只隐藏菜单、复用粗粒度 `*.write`、把 URL 当权限码或允许未登记 Endpoint 默认放行，会造成越权、权限迁移脆弱、角色无法最小授权以及前后端授权漂移
- 规则：每个受保护页面必须绑定稳定页面权限；每个调用受保护 API、读取敏感数据、导出数据或产生业务副作用的操作必须绑定独立稳定权限码，禁止新增以 URL/HTTP 路径作为权限标识，禁止用一个粗粒度写权限隐式覆盖语义不同的高风险操作。Vue 无权限时不得创建对应操作入口；客户端可见性只负责体验，所有管理 Endpoint 必须显式绑定已登记的精确权限并在 Host/租户、账号和会话边界内重新校验。角色授权必须按模块、页面、操作展示同一权威目录；授权子操作必须同时具备其页面权限，撤销页面权限必须清除后代操作。纯本地的取消、关闭、分页和布局切换不进入权限目录
- 验证：Authorization Catalog Unit 覆盖未知、重复、孤立操作和父页面约束；Architecture Tests 拒绝未声明或引用未知权限的生产 Endpoint；SQL Server/MySQL Integration 覆盖角色权限替换、存量权限迁移、权限撤销与直接 API `403 authorization.permission_denied`；Vue Unit/E2E 覆盖页面可见、逐按钮不渲染、单项授权和撤销刷新；生成器与模块交付门禁检查每个服务端业务操作的权限、Endpoint 与 Vue 绑定一致
- 例外：公开匿名 Endpoint、明确仅需认证而不需要业务权限的 Endpoint，以及纯本地无受保护数据/副作用的控件可以不登记操作权限，但必须由架构元数据显式声明类别；超级管理员通过动态目录获得全部已知权限，不得绕过账号、会话、作用域、Endpoint、审计和最后一名保护

### R-20260726-trusted-proxy-boundary：转发 Header 必须由宿主可信代理边界统一规范化

- 状态：强制
- 来源：Task 16 安全审查发现，覆盖完整 IPv4-mapped 地址空间的 IPv6 CIDR 可在双栈服务器上等价信任全部 IPv4 来源
- 适用范围：所有读取客户端地址、请求协议或 `X-Forwarded-*` 的 API 宿主、中间件、业务模块、测试和部署配置
- 风险：攻击者可伪造客户端地址或协议，绕过限流、污染审计、影响 Origin/安全跳转判断，并隐藏真实连接来源
- 规则：转发 Header 必须由宿主统一使用 ASP.NET Core Forwarded Headers Middleware 处理，并位于日志、CORS、限流、认证、授权和 Endpoint 之前；默认必须关闭，只能信任显式最小代理 IP/CIDR 和精确层数，禁止全地址族、完整 IPv4-mapped 地址空间及其更宽超网。业务模块禁止直接解析转发 Header，只能读取规范化后的连接信息。双栈部署必须按 API 实际观察到的连接形式验证 IPv4、IPv4-mapped 与原生 IPv6 行为
- 验证：`TrustedProxyOptionsTests` 覆盖失败关闭和危险 CIDR，`TrustedProxyBoundaryTests` 锁定唯一解析边界与管道顺序，`TrustedProxyForwardingTests` 覆盖伪造、链路、限流和双栈，SQL Server/MySQL API 用例覆盖协议与审计消费
- 例外：明确不存在反向代理且保持默认关闭时，无需登记信任源；任何启用场景均无宽网段例外

## 5. Dapper、事务与双数据库

1. 业务数据访问默认使用 Dapper 与显式 SQL。未经明确架构决策，禁止引入 EF Core 作为并行 ORM 或业务数据访问捷径。
2. SQL 必须参数化；表名、排序字段等不能参数化的片段必须来自封闭白名单，禁止拼接用户输入。
3. 租户作用域内的 SQL 必须同时声明 `SqlDataScope.TenantRequired` 与 `SqlTenantBinding.CurrentTenantId`，由统一范围守卫校验并由执行器注入受信任的当前租户参数；`Global`/`HostOnly` 必须使用 `SqlTenantBinding.None`。查询和写入条件仍必须真实包含租户过滤，仅声明元数据或设置上下文变量不等于隔离。全模块 Scope/Binding 一致性必须由 Architecture Tests 自动检查。每条生产 `Global` Statement 还必须在 [`contracts/architecture/global-sql-statements.json`](../contracts/architecture/global-sql-statements.json) 以 Statement Name、声明成员和源码文件精确登记安全分类、中文理由与不可变 SQL 片段；禁止通配符、批量豁免、未登记新增项和过期目录项。
4. 命令事务必须明确开始、提交、回滚和释放；异常、取消与超时路径不得遗留连接或未完成事务。
5. 业务数据与 Outbox 必须在同一数据库事务内原子写入。事务内禁止调用不可回滚的外部 HTTP、gRPC 或消息服务。
   - `ICommandTransaction` 以异常而不是 `Result` 值判断回滚。首次写入前可以返回业务失败；发生写入后若整个用例必须失败，必须抛出受控异常或使用明确支持失败回滚语义的事务入口，禁止写入后只返回 `Result.Failure` 并假设已经回滚。
   - Query 默认不启动显式事务；同一模块确需一致快照时必须由用例显式声明事务和隔离要求。跨模块同步读取不构成一致快照，不能作为两个模块共同提交的正确性边界。
6. 数据库行为变更必须同时提供 SQL Server 与 MySQL 的迁移、SQL、索引和集成验证；不能以“语法相近”代替双库测试。
7. 迁移必须可重复部署并有确定顺序。仅验证 DbUp 已记账后的零脚本重跑不算可恢复；对非事务或会隐式提交的 DDL，每个结构探测、数据回填、约束收紧和默认值步骤必须在“迁移未记账且前序步骤已部分完成”时独立收敛，并用 SQL Server/MySQL 真实集成测试模拟旧结构与半完成状态。不可逆变更、长时间锁表和大数据回填必须提供风险说明与发布策略。
8. 数据库结构变更采用 `expand -> migrate/backfill -> contract`。`DROP TABLE/COLUMN`、`TRUNCATE`、直接重命名、缩窄类型、直接增加无默认值的非空列和未分批的大表回填默认禁止；确需执行时必须有机器可检查的限期豁免、备份/验证、前滚或回滚策略和独立数据审查者。
9. 应用 SQL 禁止 `SELECT *`，禁止无 `WHERE` 的 `UPDATE/DELETE`。迁移中确需全表修正时必须进入窄范围豁免并断言预期行数；注释或文件名不能单独作为放行证据。破坏性 DDL 与无 `WHERE` 写操作由 `pnpm test:sql-safety`（[`contracts/sql-safety/`](../contracts/sql-safety/README.md)）强制；命名类违规继续由 `pnpm test:naming` 负责，二者不得双写同一规则。
10. Provider 专有 SQL 必须按同一语义提供 SQL Server/MySQL 成对实现和真实测试。CTE、窗口、Upsert、锁、JSON 和日期函数不得以数据库分支散落在业务 Handler；JSON 聚合/变更默认在应用层完成。
11. 新查询必须评估索引、排序稳定性、分页复杂度和最坏数据量。性能结论必须来自基准或执行计划，不得只凭 ORM 偏好判断。
12. 新增或修改表、列、索引、约束、Statement 和生成模板必须遵守 [`naming-conventions.md`](naming-conventions.md)：表按冻结的 OwnerKey/ModuleKey/EntityKey 命名，列用 PascalCase 与 Dapper 投影直接映射；禁止 `sys` 项目 OwnerKey、运行时动态表前缀、全局 snake_case 映射和模板私有命名算法。

### R-20260726-migration-recovery-test-boundary：恢复用例必须冻结目标迁移上界

- 状态：强制
- 来源：UUID/Naming 恢复测试先后两次因新增后续迁移而跑穿目标边界，导致已删除旧表或旧 UUID 类型与新外键冲突
- 适用范围：模拟旧版本、部分 DDL、未记账脚本、Expand/Contract 中间态和发布候选升级的所有迁移测试 Runner
- 风险：用“排除当前已知后续脚本”的黑名单会在新增迁移后自动放入未来脚本，使测试失败原因偏离目标恢复语义，并掩盖真实迁移可恢复性
- 规则：测试 Runner 必须用明确的最后迁移编号或精确允许集合表达 `ThroughNNN` 上界；禁止仅排除当前后续文件名。新增迁移不得改变既有 `ThroughNNN` Runner 执行集合。需要执行后续脚本时必须新增或显式调整命名上界，并同步该阶段结构断言
- 验证：Runner 应有脚本集合/执行数量断言；全量 SQL Server/MySQL Integration 必须覆盖旧结构、部分提交与恢复重跑。评审新增迁移时必须搜索所有 `Through` Runner，确认上界不漂移
- 例外：无

### R-20260726-host-catalog-sql-scope：跨上下文 Host 目录 SQL 必须显式过滤

- 状态：强制
- 来源：租户上下文导航查询与 API Key 认证先后两次因 Host 目录 SQL 误标 `HostOnly` 失效
- 适用范围：认证主体或可信 Host 上下文建立前执行的 SQL，以及租户请求仍需读取的 Host 用户、菜单、凭据和其他 Host 目录
- 风险：`HostOnly` 依赖一个尚未建立或已切换为租户的上下文，可能让认证入口全部失败，或使合法租户请求抛出 `HostContextRequiredException`
- 规则：上述语句必须使用 `SqlDataScope.Global`，并在 SQL 自身通过不可变、可审查的行条件精确限制 Host 数据（例如 `TenantId IS NULL`、固定作用域键及必要关联过滤）。只有调用前已存在可信 Host 上下文、且语句不需要在租户或匿名认证路径执行时才可使用 `HostOnly`。禁止以 `Global` 代替行过滤，禁止依赖请求参数动态放宽 Host 目录范围
- 验证：每个此类 Statement 必须进入 Global SQL 精确目录，由 Architecture Tests 同时锁定 `Global`、声明身份和显式 Host 行过滤；认证入口与跨租户上下文消费者必须分别提供 SQL Server/MySQL 集成回归
- 例外：无

### R-20260718-dapper-tooling-boundary：Dapper 辅助包不能绕过统一数据路径

- 状态：强制
- 来源：项目所有者要求评估 Dapper 官方/社区扩展；审查确认现有 Executor、租户守卫和事务已构成安全边界
- 规则：业务模块不得直接引用 Dapper、ADO.NET 连接/事务、`GridReader`、`Dapper.ProviderTools`、`Dapper.Transaction`、Rainbow、Contrib、FluentMap、Dommel 或其他自动 CRUD/通用 Repository 包。原生 `QueryMultiple` 只能经 Full.NET 多结果集执行器顺序消费；`Dapper.SqlBuilder` 只有真实动态列表消费者命中门禁后才能由专用查询构建层封装，值必须参数化，列名、排序、运算符和 SQL 片段必须来自代码白名单。Provider 差异继续使用小型 `ISqlDialect` 与成对语义 Statement，事务继续使用 `ICommandTransaction + DbSession`。
- 验证：架构/依赖测试阻止被拒绝引用；QueryMultiple、动态列表和 Provider Statement 必须通过 SQL Server/MySQL 真实集成测试；包引入同步许可证 Notice
- 设计：[`../docs/superpowers/specs/2026-07-18-dapper-tooling-design.md`](../docs/superpowers/specs/2026-07-18-dapper-tooling-design.md)

### R-20260718-super-administrator-boundary：超级管理员是受保护角色，不是授权旁路

- 状态：强制
- 来源：项目所有者确认对标 Admin.NET 引入默认拥有全部权限的超级管理员账号
- 规则：超级管理员只能由持久化、受保护的 Host 系统角色表达，并从服务端授权目录动态投影当前可信作用域的全部适用权限；禁止用户名判断、用户表魔法字段、通配符权限和授权处理器无条件成功。每个 Endpoint 仍声明精确权限，超级管理员仍受租户隔离、账号/会话状态、安全戳、审计和高风险确认约束。授予、撤销、禁用或删除必须由专用领域服务处理，并在并发下至少保留一名有效超级管理员。
- 验证：Unit 覆盖未知权限和作用域拒绝；SQL Server/MySQL 覆盖未来权限、最后一名并发保护和会话撤销；Vue 覆盖相同高风险流程与真实后端 E2E
- 设计：[`../docs/superpowers/specs/2026-07-18-super-administrator-design.md`](../docs/superpowers/specs/2026-07-18-super-administrator-design.md)

## 6. 并发、重试、幂等与 Outbox

1. 所有后台处理器和远程调用必须接受 `CancellationToken`，设置超时，并保证停止过程可终止。
2. 重试必须有上限、退避和可观测记录；禁止对参数错误、权限错误或确定性业务失败无限重试。
3. 可重复提交的命令、Webhook、定时任务和消息处理必须定义幂等键、重复结果和并发冲突策略。
4. Outbox 领取必须有多实例竞争控制或租约；失败必须区分可重试与毒消息，并防止单条坏消息永久阻塞批次。
5. Outbox 处理时必须恢复租户上下文并验证事件类型映射。不得把“至少一次发布”描述成“恰好一次”。
6. 锁、租约、批大小和并行度必须有边界，且在崩溃、超时和时钟偏差下能恢复。
7. 乐观并发失败后若重读状态并重试，必须重新验证账号活动状态、安全戳、权限、租户边界和业务前置条件中受并发影响的部分；禁止只替换版本号后继续执行高权限操作。
8. 已发布 Outbox 事件必须保留稳定消息类型和正整数 `SchemaVersion`。出现第二个版本时必须提供并行旧版本 Handler 或显式相邻版本升级链，并记录兼容/退役窗口；未知版本、永久失败和超过最大重试的毒消息必须进入可查询、可审计重放的死信路径，不能永久阻塞批次。
9. 同进程模块内部事件使用类型化 Contract/Dispatcher，不得为未来吞吐假设默认进入外部 Broker。当前需要事务原子性和可靠重试的重要业务 Integration Event 只允许通过与业务数据同事务的 Outbox 发布；不得根据瞬时 QPS 动态切换到可靠性更弱的链路。缓存失效、日志、Trace、Metrics、普通 HTTP Operation Log 和 Audit 禁止使用 Outbox；Domain Audit 必须作为业务事实直接写入业务事务。
10. 项目已批准提前实施事务 Outbox 的 CDC/Kafka 交付演进，但批准范围仅覆盖事件契约 V2、追加式 Outbox、双库 CDC、Kafka Provider、消费 Inbox、影子流量、故障验证和受控切换能力；在生产切流门禁关闭前不得把该能力标记为 `Build-verified` 或 `Production-verified`。CDC/Kafka 端到端仍按至少一次与消费幂等设计，禁止宣称 Exactly-Once；轮询 Worker 与 CDC Relay 不得同时拥有同一事件流。
11. 直接 Broker 发布只能承载经事件目录批准的可丢失、可重算且不要求业务事务原子性的流量；禁止在 `finally`、无人观察的后台任务或无界缓冲中 fire-and-forget。详细边界见[总体架构 Spec §9.1](../docs/superpowers/specs/2026-07-17-fullnet-architecture-design.md#91-事件交付演进基线)。

### R-20260808-transactional-outbox-cdc-broker-boundary：提前实施不得降低事务与至少一次语义

- 状态：强制
- 来源：项目所有者于 2026-08-08 明确要求不引入 CAP，基于现有 Outbox 提前实现“.NET 业务事件 + 事务 Outbox + Broker 发布订阅”，并消除应用 Worker 对 Outbox 热表的高频轮询压力
- 适用范围：Integration Event 契约、Outbox、SQL Server CDC、MySQL Binlog、Debezium/Kafka Connect、Kafka Provider、Consumer Group、Inbox、重试、死信、重放、Worker、Migrator、部署配置、管理端运维能力和相关测试
- 风险：请求事务直接写 Broker 形成数据库/Broker 双写不一致；SQL Server CDC 被误报为零数据库读取；轮询 Worker 与 CDC Relay 重复发布；Broker Ack 与消费数据库提交之间发生重复副作用；分区键缺失导致业务乱序；CDC 位点或日志保留不足导致事件缺口；把 Kafka Producer 幂等误报为端到端 Exactly-Once
- 规则：业务事务只能原子写入本模块业务状态与追加式 Outbox，不得在事务内等待 Broker。SQL Server 使用受支持 CDC，MySQL 使用 ROW Binlog；禁止自行解析 SQL Server 内部事务日志。CDC Relay 只捕获批准的追加式 Outbox `INSERT`，以稳定 `EventId`、`MessageType`、`SchemaVersion`、`TenantId`、`PartitionKey`、`CorrelationId`、`CausationId`、`TraceParent`、`OccurredAtUtc` 和 `ContentType` 发布。Broker 与消费者端保持至少一次；每个持久化消费者必须以 `(ConsumerName, MessageId)` 唯一 Inbox 在本地事务内完成去重、业务写入、下游 Outbox 与完成标记，数据库提交后才允许提交 Offset/Ack。发布所有权必须按稳定事件流静态配置；切换时执行影子验证、停止目标旧发布者、排空、记录 CDC 位点、启用唯一 Relay 和可逆回退，禁止同一事件流双发布。SQL Server CDC 仍包含日志捕获和变更表读取，不得对外宣称“数据库零读取”；目标是移除应用对 Outbox 队列表的领取、租约、续租和状态更新压力
- 验证：Architecture Tests 阻止业务模块直接引用 Kafka/Debezium 客户端及事务内 Broker 发布；SQL Server/MySQL 真实集成测试覆盖业务+Outbox 原子提交、CDC 捕获、重复、乱序、Schema 兼容、Inbox 原子性和下游 Outbox；Kafka 集成测试覆盖不同 Consumer Group 扇出、同组竞争、分区顺序、重平衡、Broker 中断、DLQ 与受控重放；故障矩阵必须覆盖数据库提交后 Relay 前宕机、Broker 确认后位点提交前宕机、消费数据库提交后 Offset 前宕机以及切流/回退；生产切流前保存轮询基线、CDC 延迟、Consumer Lag、最老消息年龄、存储保留和恢复演练证据
- 例外：可丢失、可重算且无需与业务事务原子提交的遥测流可按事件目录使用直接 Broker，但必须使用独立接口、Topic 和可靠性分类，不得复用可靠业务 Integration Event API。任何生产双发布仅允许发送到无业务消费者的影子 Topic，且不得对外部系统产生副作用
- 设计：[`../docs/architecture/adr/ADR-0006-transactional-outbox-cdc-kafka-event-delivery.md`](../docs/architecture/adr/ADR-0006-transactional-outbox-cdc-kafka-event-delivery.md)、[`../docs/superpowers/specs/2026-08-08-transactional-outbox-cdc-kafka-design.md`](../docs/superpowers/specs/2026-08-08-transactional-outbox-cdc-kafka-design.md)

### R-20260717-seed-data-boundary：生产 Baseline、环境 Overlay 与场景测试数据必须分层

- 状态：强制
- 来源：项目所有者明确种子数据既用于生产初始化，也应供开发和测试复用；审查发现当前 `Host.Migrator --seed-local` 没有表达生产基线与环境叠加关系
- 适用范围：Host.Migrator、AppHost、所有业务模块 Seed Contributor、SQL Server/MySQL 迁移与 IntegrationTests
- 风险：生产环境误写演示数据、内置或泄露默认密码、API 多实例重复播种、测试依赖固定开发数据、重复执行覆盖用户修改或产生重复 Outbox
- 规则：必须遵循 [`../docs/superpowers/specs/2026-07-17-seed-data-module-design.md`](../docs/superpowers/specs/2026-07-17-seed-data-module-design.md)。Baseline 只包含生产必需/安全数据，首个管理员等敏感初始化必须使用显式 Secret；Development/Demo/Test 必须先执行 Baseline 再执行自己的 Overlay，Production 只允许 Baseline。Test 专用 Contributor 只能存在于测试/Sample 测试程序集，不进入发布物；具体场景数据由隔离 Test Factory 创建。API/Worker 禁止启动播种。Contributor 必须使用稳定自然键、真实领域服务、独立事务和幂等协调，默认不得删除数据、重置密码或覆盖用户修改；数据库审计历史不能代替真实状态检查
- 验证：SQL Server/MySQL 分别覆盖迁移、运行锁、Baseline 首次/重复、Overlay 继承、冲突、失败重跑、Outbox，以及 Production 允许 Baseline/拒绝其他 profile；Architecture Tests 断言 API/Worker 与业务模块不依赖 Seed 执行器且测试 Contributor 不进入发布物；日志和审计不得包含 Secret、连接串、Token 或异常堆栈
- 例外：无。生产初始化必须进入 Baseline；不能把开发/演示数据改名后放入 Baseline 绕过门禁

### R-20260816-identity-contracts-hub-boundary：Identity.Contracts 只允许跨模块稳定契约

- 状态：强制
- 来源：设计缺陷清单 P1 收尾；反向契约债务已清零，需防止 owner-domain 事件再次进入 hub
- 适用范围：`Full.NET.Modules.Identity.Contracts` 及引用它的模块 Contracts/Consumer Port
- 规则：`Identity.Contracts` **允许**：consumer-owned 只读 Port（如 `IIdentityOrganizationUnitProjectionSource`）、Identity 侧订阅所需的稳定 Integration Event 类型与常量（如 `IdentityOrganizationUnitChangedIntegrationEvent`）、Host 运维/Reconcile DTO 与权限码。**禁止**：Organization/Document/Files 等 owner 模块的领域事件定义、持久化实体、Repository 或写入 Port 放入 Identity hub；跨模块写入仍经 Outbox + 消费方本地 Handler/投影
- 验证：Architecture Tests 扫描 Contracts 依赖方向；`AllowedReverseContractDependencies` 保持空；新增 hub 类型必须附带 consumer-owned 或 Identity-owned 说明

## 7. API、错误与序列化契约

1. 对外 HTTP API 必须使用标准 HTTP 状态码与 ProblemDetails；领域失败不得默认返回 `200 OK`。
2. Admin.NET 包络只能由 Compatibility 适配层显式启用，核心业务与标准端点不得依赖兼容模型。
3. Minimal API/FastEndpoints 的每条端点必须显式声明 `RequireAuthorization(...)`/权限策略或 `AllowAnonymous()`，并声明输入验证、状态码和取消传播；禁止依赖默认行为表达安全意图。匿名端点必须有契约测试锁定最小返回字段；异常统一交给异常处理管道。
4. JSON 使用 System.Text.Json。高频或 Native AOT 路径应使用源生成上下文；新增多态或自定义转换器必须有往返和兼容测试。
5. 内部通信可按边界使用 gRPC；可靠 Integration Event 使用 ADR-0008 约束的 MemoryPack 受控二进制协议，并必须版本化契约、测试未知字段/旧版本/不可信载荷；禁止仅因性能偏好暴露内部二进制格式给公共 Web API。
6. 公共响应、事件和缓存对象的字段改名、类型改变或删除属于兼容性变更，必须迁移或版本化。
7. 错误响应不得包含实现堆栈、SQL、连接信息或内部类型名；日志中的关联标识必须能定位服务端详情。

### R-20260717-full-stack-localization-boundary：多语言必须覆盖协议、组件库和服务端生成文本

- 状态：强制
- 来源：项目所有者明确要求 Vue、Layui、uni-app、App/桌面端与后端统一支持多语言；审查确认当前只完成双管理端壳层 `zh-CN/en-US` 文案，尚未覆盖组件库、HTTP 协商、服务端资源和计划客户端
- 适用范围：ASP.NET Core API/Worker、Vue、冻结的 Layui 存量资源、uni-app、Flutter、可选 .NET MAUI，以及通知、报表、实时消息和 AI 自然语言输出
- 风险：语言清单、回退规则和术语在各端漂移；业务逻辑依赖翻译文本；后台任务串用请求 Culture；缓存跨语言污染；只完成一个壳层却把能力误报为全栈可用
- 规则：必须遵循 [`../docs/superpowers/specs/2026-07-17-full-stack-localization-design.md`](../docs/superpowers/specs/2026-07-17-full-stack-localization-design.md)，统一使用规范 BCP 47 语言标签、默认语言、回退规则与术语表。错误码、权限码、字段名、枚举值、事件类型和 Agent Tool Schema 等机器契约必须保持稳定且不可本地化；显示文本由各平台原生资源机制生成。后台、通知、实时与 AI 边界必须显式携带或解析语言，禁止依赖进程全局 Culture。只有对应语言资源、HTTP 协商、组件库、服务端生成文本和相关跨端测试均通过后，功能才可标记为多语言 `Verified`
- 验证：必须执行语言目录、资源完整性、占位符一致性、缺失键、回退、`Accept-Language`/`Content-Language`、Vue 组件库和相关业务客户端测试；冻结 Layui 不新增语言能力，仅在明确修改时运行聚焦检查。涉及本地化数据表时还必须执行 SQL Server/MySQL 双库验证
- 例外：平台 API 只能接受别名时可在适配层映射，例如 uni-app 内部 `zh-Hans` 映射到规范 `zh-CN`，但公共 API、持久化值和跨服务契约仍使用规范语言标签并记录映射；稳定机器契约没有本地化例外

## 8. 缓存、实时通信和基础设施

1. 缓存以 FusionCache 为唯一实现，并通过 `.AsHybridCache()` 暴露双抽象；禁止再引入独立缓存实现造成语义分叉。
2. 缓存键必须包含环境、模块、租户、版本和业务标识中的必要维度，禁止不同租户或不同契约版本共享同一键空间。
3. 缓存必须归入 C0 权威强一致、S0-L2 共享即时、S1 重要业务、S2 可降级展示或 N0 不缓存。权限、用户禁用/安全戳、租户启停/到期、API Key 和 Session 等安全关键数据的 Fail-Safe 必须关闭，授权决定不得只依赖可能陈旧的 L1；C0/S0-L2 必须禁用 L1，N0 直接读取权威源。Background Refresh 不能作为安全正确性证明。
4. 缓存失效禁止使用 Outbox。业务事务提交后，当前实例必须直接失效 L1/L2，再由 Redis Backplane 通知其他实例清理 L1，以 TTL、版本和权威源读取兜底；重复删除必须幂等且不得主动触发数据库回填。只有昂贵热点回填存在击穿证据时才允许增加带租约和超时的分布式锁。
5. 每项缓存必须定义类别、过期、最大陈旧、失效、空值、降级和源故障行为。多实例部署必须验证分布式缓存、失效通知延迟、通知丢失后的收敛和 Redis 故障，而非只测单机内存。
6. SignalR Hub 必须鉴权；组名和连接映射必须包含租户边界。横向扩展时必须采用受支持的背板并限制消息大小、频率和连接资源；除 WebSockets-only + `SkipNegotiation` 外，入口必须保持连接亲和。
7. 健康检查必须注册真实依赖并区分存活、就绪与启动完成；`ready`/`startup` 空检查集合不得返回可供编排器采用的成功信号。当前数据库、已配置的 Redis/Backplane 和必要初始化必须有短超时、无副作用的检查，并以依赖失败集成测试验证 HTTP 状态；健康响应不得泄露连接串、SQL 或异常堆栈。
8. 基础设施不可用时必须定义 fail-fast 或降级策略，禁止静默切换到可能造成数据不一致的本地实现。
9. 多实例 Data Protection 必须使用稳定 `ApplicationName`、共享持久化 Key Ring 和静态加密，历史 Key/证书必须可恢复；禁止使用 Pod 本地卷或可驱逐缓存 Redis。生产文件必须使用外部对象存储，不得依赖某个 API 实例本地磁盘。
10. 生产必须暴露独立 `Cache/Backplane` 与 `Realtime` Redis 连接边界；默认物理隔离，开发可共用，生产同机例外必须具备容量与故障域证据。

## 9. 日志、指标与高并发

1. 业务代码使用 `Microsoft.Extensions.Logging` 抽象和结构化模板，禁止字符串插值丢失字段语义。
2. 高并发路径禁止同步网络日志、无界队列和每请求刷新；日志管道必须有容量、背压、丢弃策略和自监控指标。
3. 必须统一关联 `TraceId`、租户标识、用户标识和业务标识，但敏感字段必须脱敏或哈希。
4. 错误日志应记录一次且位于最有处理上下文的边界，禁止每层重复记录同一异常。
5. Error/Critical 必须拥有独立容量、独立指标和可靠降级路径，不得与可丢弃的 Debug/Information 共用唯一过载命运；同时禁止为了日志可靠性默认阻塞请求线程同步写网络或磁盘。
6. OpenTelemetry 指标与追踪必须控制标签基数；禁止使用用户 ID、URL 原文或异常消息作为高基数标签。
7. 性能优化必须先定义指标、数据规模和基线；BenchmarkDotNet 结果必须记录运行环境，不能用 Debug 构建得出结论。
8. 日志必须按 `Level`、`LogClass`、`ReliabilityClass`、`DataClassification` 四维分类，并使用 `DiagnosticGroup`、`EventId/EventName` 和 `SourceContext` 做低基数逻辑分组；业务代码禁止指定日志文件、数据库表或具体 Sink。
9. 普通 HTTP Operation Log 属于 B2 可观测日志，每请求最多一条汇总，生产默认 `Summary`；请求/响应 Payload 只允许 Endpoint 白名单和字段投影，必须脱敏并限制长度、深度和集合数量。
10. Audit 不使用 Outbox。B0 Domain Audit 必须与业务状态在同一数据库事务直接写入并 fail-closed；B1 重要 HTTP Operation/Exception Audit 使用有界跨请求微批直接写审计库，请求等待写入尝试并默认 fail-open + 告警；B2 普通访问/诊断走有界日志管道并可采样。禁止每条日志或 Audit 单独开连接写库。
11. 生产动态诊断只能由受保护管理接口按命名空间、Endpoint、TraceId、租户或诊断组临时开启，必须具有 TTL、操作者、原因、速率/字节上限和 Audit；传播使用当前实例刷新 + Redis Backplane + 版本/TTL，不使用 Outbox，禁止无限期全局 Debug/Trace。
12. 日志过载状态只能收缩 B2/Best Effort；不得降低 B0/B1 可靠性语义或把同步网络/磁盘写入移到请求线程。

## 10. AI 集成与 Agentic Web

1. AI 能力必须通过独立抽象隔离模型供应商，配置模型、预算、超时、重试和降级策略。
2. Prompt、检索内容和工具输出均视为不可信输入，必须防范提示注入、越权工具调用和敏感数据外泄。
3. Agent 工具必须使用最小权限、参数白名单和可审计日志；付款、删除、发布、外发消息等高影响操作必须保留人工确认。
4. 必须限制单请求令牌、循环次数、工具次数和总时长，支持取消，并对费用与失败率建立指标。
5. 使用个人或业务数据前必须明确保留、脱敏、跨境和供应商训练策略；不得默认把生产数据发送给外部模型。
6. AI 输出必须被视为建议或不可信数据，进入 SQL、模板、代码执行或业务决策前必须验证。

## 11. 测试与验证

1. 新行为和缺陷修复必须先建立能失败的测试或可复现实验；文档和纯机械变更可用结构化检查代替行为测试。
2. 至少覆盖成功、验证失败、权限失败、取消、并发、重复请求和依赖故障中与变更相关的路径。
3. 数据层变更必须运行 SQL Server 与 MySQL 集成测试。Docker 或外部依赖不可用时，必须报告未验证项，禁止静默跳过后宣称通过。
4. Full.NET 使用 Microsoft Testing Platform；必须通过测试矩阵生成的稳定命令执行套件并保留最低发现数门槛，不能只看到构建成功就认为测试已执行。Integration 验证必须按变更风险分层：本地只运行受影响测试；SQL、事务、租户过滤和迁移变更必须覆盖 SQL Server 与 MySQL；共享基础设施运行对应 Smoke、能力过滤集或专项分片；完整集合只由 `main` CI 并行分片执行。聚焦结果只能表述为聚焦通过，被门槛拒绝、零发现或降低门槛的运行不得作为完成证据。
5. 测试套件、最低发现数、超时和 Integration 分片只维护在 [`eng/testing/test-matrix.json`](../eng/testing/test-matrix.json)；README、开发指南、CI 与 Skill 只能引用稳定命令或该清单，禁止复制易变数字。增删测试后必须更新清单并运行 `pnpm test:integration:partitions` 与 `pnpm test:governance`；普通门槛变化不得再要求人工追加 `test-threshold-audit` 长文档。
6. 架构、兼容性和序列化契约必须有专门测试；不能只依赖端到端测试偶然覆盖。
7. 完成前必须运行 Release 构建、相关测试和 `git diff --check`；报告测试总数、失败数和任何跳过项。
8. 验证命令必须在最终代码状态下重新运行，禁止复用变更前的结果作为完成证据。凡测试会扫描或执行构建产物，测试入口必须在同一命令链先生成当前源码的新产物；禁止依赖工作区遗留产物产生假通过。

### 11.1 Integration 变更风险分层

| 变更范围 | 最低 Integration 验证 |
| --- | --- |
| 文档、纯客户端或不接触服务端行为 | 不强制运行 Integration；执行直接相关的治理、客户端或契约测试 |
| 单模块且不改变 SQL、事务、租户、认证授权或共享宿主 | inner 只运行快速测试；纵向 slice 关闭时运行受影响 Endpoint/用例的聚焦测试 |
| SQL、Dapper 映射、事务、租户数据过滤或数据库行为 | 运行同一场景的 SQL Server 与 MySQL 聚焦测试 |
| 新增或修改迁移 | 运行受影响迁移阶段的双库恢复测试，以及受影响模块的双库聚焦测试 |
| 共享宿主、Composition 或未知服务端路径 | 运行双库 Smoke 影响集 |
| 认证授权、租户基础设施、Outbox、缓存或其他已登记共享能力 | 运行对应能力的双库聚焦影响集 |
| 迁移 Runner 或迁移测试基础设施 | 运行 migrations 分片 |
| Integration 测试工具链 | 运行 Integration tooling 与治理契约 |

验证按开发阶段收敛：

| 阶段 | 触发时机 | 默认门禁 |
| --- | --- | --- |
| `inner` | 每次代码迭代 | 编译、Unit/Contract/类型检查；Identity、Tenancy、Outbox、缓存、迁移和共享宿主等高风险变更立即运行登记的聚焦 Integration |
| `slice` | 一个 API＋数据库＋客户端纵向功能切片关闭，最长不超过两个工作日 | 运行该切片全部 affected 双库 Integration 与受影响客户端测试 |
| `merge` | PR、合并候选或每日功能列车 | 运行 slice 影响集并追加双库 Smoke；默认排除 `messaging-heavy` 分片（Kafka/CDC/Capacity Docker 重测），Messaging 变更在 slice 验证，完整重测由 `main` CI 第五分片承担；需要本地复核重测时使用 `--include-heavy` |
| `main` | 受保护分支 CI | 运行测试矩阵中的完整互斥分片和汇总门禁 |

本地任务禁止运行 `test:integration:full`，只运行从任务边界计算出的受影响测试；共享路径不得自动升级为完整集合。完整集合只保留给 `main` CI。准备发布时以最近一次目标 `main` CI 全量门禁为完整 Integration 证据，本地仍只补跑发布变更的影响集。

本地标准入口为 `pnpm test:inner`、`pnpm test:slice`、`pnpm test:integration:affected:plan` 和 `pnpm test:integration:affected`。`inner` 阶段必须使用 `pnpm test:inner`（或等价的 `test:integration:affected --phase inner`），禁止用 `pnpm test:e2e:real`、完整 `pnpm test:e2e:admin`、`pnpm test:integration:full` 或 `messaging-heavy` 代替内循环。`test:e2e:real` 只用于 `Verified` 关闭或真实 CORS/Cookie/Session 缺陷；完整 `test:e2e:admin` 属于 slice/客户端契约关闭，不进入每次代码迭代。`test:integration:full` 只保留为 CI 维护诊断入口，普通本地任务禁止调用。完成耗时基线或排查慢测时必须对受影响 TRX 运行 `pnpm test:integration:durations`，不得只凭单次墙钟时间修改并行度，也不得让多个用例共享可变业务数据库。只读 schema 模板克隆到独立数据库、本地 Testcontainers 复用，以及 inner 缩小浏览器套件，属于已批准的加速手段，不在此禁令内。

代码、SQL、配置或脚本任务开始时必须记录 `git rev-parse HEAD`。工作区已脏或任务跨窗口时必须运行 `pnpm test:task:start -- <task-id>` 创建任务快照；后续通过 `--snapshot <task-id>` 只选择任务开始后真正改变的文件。干净且单窗口任务可继续使用 `--base <任务基线>`。先运行 `pnpm test:integration:affected:plan -- --snapshot <task-id> --phase <inner|slice|merge>` 审查影响集，再运行对应 affected 命令。`inner` 阶段聚焦测试与 Smoke 只强制 MySQL Provider，选择器不得用宽子串把迁移恢复或 `messaging-heavy` 卷进 inner；`slice` 与 `merge` 仍要求同场景 SQL Server 与 MySQL。`merge` 默认跳过 `messaging-heavy`；Messaging/Kafka/CDC/Capacity 变更先在 `slice` 验证，必要时追加 `--include-heavy`。选择器排除 `App_Data`、纯 `benchmarks/` 文档式变更等运行时或基准工件，合并多个过滤目标并按 UID 去重；已在测试矩阵登记恢复集的迁移运行对应双库恢复测试和受影响模块测试，未登记迁移安全降级到 migrations 分片并追加可识别的受影响模块，迁移 Runner 或共享夹具也运行 migrations 分片。不得通过遗漏路径、改写边界或手工缩小 `--filter` 规避受影响测试。

### 11.2 新增 Integration 测试门禁

1. 新增行为默认先在 Unit 或 Architecture 测试覆盖；只有 Unit 无法证明真实 DB、Broker、Connect、租户隔离或双 Provider 差异时，才允许新增 Integration 测试。
2. 新增 Integration 测试前必须说明：为何 Unit 不足、是否必须双库 `[DataRow]`、能否并入现有 `[TestClass]`/fixture，以及是否属于 `messaging-heavy` 重测。
3. Kafka/CDC/Capacity/Debezium 全链路或 `[RequiresDocker]` 长时测试只能进入 `messaging-heavy` 分片或专项 workflow，禁止加入 Smoke 或普通模块聚焦集。
4. 增删 Integration 后必须更新 [`eng/testing/test-matrix.json`](../eng/testing/test-matrix.json) 并运行 `pnpm test:integration:partitions`；慢测排查使用 `pnpm test:integration:durations`，不得凭单次墙钟时间让多个用例共享可变业务数据库。

### R-20260816-local-test-inner-budget：本地内循环必须走分层漏斗，禁止用全量套件冒充 inner

- 状态：强制
- 来源：项目所有者明确要求加快测试与开发速度，并授权修改测试规则；代理在 Document 等切片中把 `test:e2e:real`、完整 Playwright 和双库 Integration 当作每次迭代门禁，导致内循环数十分钟
- 适用范围：本地开发、修复、重构和代理自动验证；不降低 `main` CI 全量分片或 `Verified` 真实栈门槛
- 风险：每次改几行代码都启动完整浏览器、真实 Migrator/API/Worker 或 585 项 Integration，开发反馈被拖垮，同时把 inner 通过误报为 slice/`Verified`
- 规则：`inner` 必须使用 `pnpm test:inner`（审查影响集时用 `pnpm test:integration:affected:plan -- --phase inner`）。禁止在 inner 运行 `pnpm test:e2e:real`、`pnpm test:e2e:real:mysql`、完整 `pnpm test:e2e:admin`、`pnpm test:integration:full` 或 `messaging-heavy`。inner 的 Smoke 与聚焦 Integration 必须附加 `FullyQualifiedName~MySql`，禁止再跑同场景 SQL Server。Identity/Tenancy/Outbox/CodeGeneration 过滤器必须限定到对应 API/模块测试命名空间，禁止用 `~Identity`、`~Outbox` 这类会命中迁移恢复或 CDC 重测的宽子串。`slice` 使用 `pnpm test:slice` 或 `test:integration:affected --phase slice`，覆盖该纵向切片的双库 Integration 与受影响客户端测试。`test:e2e:real` 只用于功能 `Verified` 关闭，或修复真实 CORS、Cookie、CSRF、Session 与跨 Origin 凭据问题。每个 API Integration 用例仍必须使用独立数据库；允许把只读、已迁移的 schema 模板（不含租户/管理员/导航业务行）克隆到这些独立库，每个用例仍必须自行执行供给与引导。禁止多个用例共享同一可变业务库。本地默认复用 Testcontainers 容器；CI 必须销毁。设置 `FULLNET_TESTCONTAINERS_REUSE=0` 或 `FULLNET_API_SCHEMA_TEMPLATE=0` 可关闭对应加速
- 验证：`tests/governance/integration-test-feedback.test.mjs` 锁定 `test:inner`/`test:slice`、inner 禁令和模板克隆/复用入口；`pnpm test:governance` 与 `pnpm test:integration:tooling` 必须通过
- 例外：用户在当前任务中明确要求运行真实栈或完整浏览器套件时可以执行，但不得把该结果写成 inner 完成证据

## 12. 文档、依赖与发布许可

### 12.1 文档产物分层

架构、设计和实施文档必须按职责分层，禁止用文件标题、目录习惯或完成勾选隐式提升决策状态：

永久文档实行产物预算：

- 普通功能、局部缺陷和低风险重构只需要代码、测试与 PR/提交说明，不创建独立 Spec、Plan 或 Verification。
- 跨模块或预计超过一个工作日的工作才创建实施计划；同一主题只保留一份活动计划。
- 公共契约、数据迁移、安全边界或长期架构决策才创建或修改 Spec/ADR。
- 性能基准、安全审计、恢复演练或发布才创建独立 Verification；普通测试结果保留在 CI/TRX 与交付说明。
- 路线图和能力矩阵在纵向切片或里程碑关闭时集中更新，不随每次内部提交重复同步。

| 目录 | 职责 | 允许状态与约束 |
| --- | --- | --- |
| `docs/verification/` | 保存基于特定代码基线的评估、审查、实验和验证事实 | 必须记录日期、范围、输入或代码基线、方法、结论与未验证项；评估建议不自动覆盖已批准规格，也不能单独证明功能已实现 |
| `docs/superpowers/specs/` | 保存经项目所有者或当前授权用户确认的长期设计、架构边界和验收条件 | 必须明确批准状态、适用范围和被替代关系；同一主题优先更新现有规格，禁止创建相互竞争的事实源 |
| `docs/architecture/adr/` | 保存单项重大架构决策的上下文、候选方案、取舍、后果和替代关系 | 仅在改变长期基线、引入高迁移成本约束或多个可行方案需要保留决策理由时创建；首个 ADR 创建目录，文件使用 `ADR-NNNN-kebab-case-title.md`；ADR 与总体规格冲突时必须在同一任务同步规格摘要 |
| `docs/superpowers/plans/` | 将已批准的 Spec 或 ADR 分解为可执行、可验证的实施步骤 | 必须引用批准依据并列出精确文件、验证和停止条件；计划勾选、提交存在或文档声称完成均不能替代新鲜构建、测试和 Verification 证据 |

文档状态按以下顺序流转：

1. 分析、审查或实验先进入 `docs/verification/`，保持“建议稿”“复核记录”或真实验证状态；只读任务未经明确授权必须在此停止。
2. 建议被项目所有者或当前授权用户确认后，更新对应 `docs/superpowers/specs/`；若命中重大单项决策门槛，同时新增 ADR 并同步规格摘要。
3. 只有批准后的 Spec 或 ADR 才能产生 `docs/superpowers/plans/` 实施计划；探索性计划必须显式标为未批准且不得执行。
4. 实施完成后，以新鲜自动化或人工验证更新 `docs/verification/` 和能力状态矩阵；验证失败、跳过或环境缺失必须如实保留。
5. 后续证据推翻旧决策时，必须显式标记替代、退役或重新评估，禁止只新增一份更新日期更晚但关系不明的文档。

### 12.2 一般文档、依赖与发布要求

1. README、路线图和示例必须明确区分“已实现”“实验性”“计划中”，禁止把未来能力写成当前可用功能。
2. 新增功能必须同步使用说明、配置、迁移、故障排查和限制；删除或重命名能力必须更新全部链接和示例。
3. NuGet 版本必须集中管理，禁止项目文件中散落版本。新增依赖前必须检查维护状态、传递依赖、漏洞、体积和 Native AOT 影响。npm 与 NuGet 的 Critical 漏洞必须阻断，High 默认阻断；例外必须精确到 advisory、包、实际依赖路径、缓解措施、责任人和有限到期日，禁止通配、永久忽略或只输出列表。扫描失败、输出不可解析或缺少可靠 finding 路径时必须失败关闭。
4. Full.NET 最终发布框架采用 MIT 许可。任何进入发布产物的代码、资源和依赖必须允许该分发方式，或在发布前替换、隔离或排除，并更新 `THIRD-PARTY-NOTICES`。
5. Admin.NET.Pro 已获得二开和商用授权，但该授权不自动等于可按 MIT 再许可；复制其代码或资源进入 MIT 发布物前必须留存明确授权范围。`dotnet/eShop` 作为架构参考时仍须保留适用通知。
6. 禁止提交真实密钥、内部地址和生产连接串。示例配置必须使用占位值，并说明安全注入方式。

## 13. Git、编码与跨平台

1. 提交前必须确认工作区差异只属于当前任务；不得覆盖或提交用户无关变更。
2. 提交应小而聚焦，消息说明意图。合并前必须验证目标分支；合并成功后按用户要求删除已合并分支并再次检查分支列表。
3. 禁止使用 `git reset --hard`、强制推送或批量删除来解决普通冲突，除非用户明确授权且目标已核验。
4. 手写文本必须使用 UTF-8，禁止 UTF-16、无效 UTF-8、Unicode Replacement Character `U+FFFD`，以及中文正文被连续 ASCII `?` 替换的乱码进入权威 Markdown、规则、Skill、契约或源码；治理扫描必须排除代码围栏与行内代码后识别可疑问号串，报告首个损坏文件与位置并失败关闭，不得静默重编码。遵守仓库换行策略，`git diff --check` 必须无空白错误。
5. 路径、文件名大小写、脚本和容器配置必须考虑 Windows 与 Linux；禁止依赖本机绝对路径进入正式代码。
6. 生成物、密钥、本地 IDE 配置和临时工作树不得进入版本控制。

## 14. 交付前遗漏清单

完成声明前必须回答：

- 每项用户需求是否都能指向代码、测试或文档证据？
- DI 注册、生命周期、启动顺序和健康检查是否真实可运行？
- 租户、权限、事务、并发、幂等和失败恢复是否覆盖？
- Baseline、Development/Demo/Test Overlay 与场景 Test Fixture 是否正确分层，且没有默认密码、生产误播种或重复覆盖？
- SQL Server 与 MySQL 是否同步实现并实际验证？
- 标准 API、Admin.NET 兼容层和序列化契约是否保持边界？
- 多语言是否覆盖受影响客户端、组件库、HTTP 协商和服务端生成文本，并保持稳定机器契约不被翻译？
- FusionCache、多实例、Outbox、SignalR 或 AI 能力是否考虑生产拓扑？
- 构建是否为 Release，测试是否真的执行且数量达到门槛？
- 文档是否准确描述当前状态，许可证与第三方通知是否完整？
- 新增或修改的源代码注释是否为清晰中文且没有过期内容？
- Git 工作区、分支、编码和跨平台状态是否干净可复现？
- 本次是否出现值得按 [`rule-evolution.md`](rule-evolution.md) 升级的新经验？
