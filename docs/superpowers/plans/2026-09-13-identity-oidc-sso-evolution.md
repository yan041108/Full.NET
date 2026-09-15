# Full.NET Identity OIDC 与 SSO 执行计划

> 执行方式：按仓库 AGENTS.md 在当前工作流逐项推进；不自动创建工作树、派发实现代理或切换独立计划执行流程。安全行为先建立可失败验证，完成前核对新鲜证据，重大安全切片交付前独立审查。

**目标：** 复用现有 Identity 账号、权限与权威会话，验证并分阶段交付标准 OIDC 认证中心、两个应用 SSO 和 Vue 接入。

**架构：** Identity 单一数据所有者，OpenIddict 为优先验证的协议实现，四类 Store 使用自有 Dapper 执行与事务边界。Composition 装配，API/Worker/Migrator 分离，Host.Api 维持 Native AOT；中心登录、应用会话和刷新令牌族分别建模。

**技术栈：** .NET 10、OpenIddict（T00 锁定具体受支持版本）、Dapper、SQL Server/MySQL、ASP.NET Core 认证、现有 Vue 与真实栈测试工具。

## 1. 批准、状态与范围

- 日期：2026-09-13。
- 计划修订：2026-09-15，按审查补齐既有会话消费者、切租户签发和强制下线入口的适配与验收；本次不依据其他工作区改动调整实施状态。
- 批准依据：用户已确认研究建议并要求更新项目文档及执行计划。
- 设计依据：[ADR-0011](../../architecture/adr/ADR-0011-identity-oidc-sso-evolution.md)、[Identity 规格 §14](../specs/2026-07-17-identity-session-foundation-design.md#14-oidc-认证中心与-sso-演进2026-09-13-已确认)。
- 证据与稳定场景编号：[eShop 对照研究 V01—V24](../../verification/2026-09-13-identity-oidc-sso-research-validation.md)。
- 当前状态：文档已同步；T00—T08 均未开始，P0—P3 均无运行验收证据。本次文档任务不安装依赖、不修改生产代码或数据库。
- 当前记录基线：`main`，HEAD `f74515d1b9e4544e4c75eeb623892970790b4d0c`，工作区有其他任务改动。进入代码任务时重新记录当时 HEAD 并按规则创建任务快照，不将这里的旧 HEAD 作为未来变更的无条件基线。
- 本文件是本主题唯一活动执行计划。P0 通过后按已确认阶段继续推进，正常实现选择无需重复审批；改变 ADR 的数据、运行或安全边界时先形成证据与决策修订。

## 2. 全局约束

1. 单一 Identity 账号权威源；不复制用户库、不跨模块直查表、不把第三方协议 DTO 泄漏到业务 Contracts。
2. Host.Api 保持 Native AOT；不能靠关闭模块、放松会话校验或无依据 suppression 通过。
3. Dapper 与显式 SQL，经 Full.NET 自有执行／事务边界访问；SQL Server/MySQL 同场景成对验证，Migrator 负责迁移与受控播种。
4. 官方实体主键应用端 UUID v7；表命名和全局 SQL 登记按现有规则。协议字符串标识和本地 Guid 主键分别处理，不能假定任意第三方 sub 都是 Guid。
5. 交互式授权码 + PKCE S256；精确客户端与回调范围；scope、aud、业务权限、账号／会话和租户独立校验。
6. 旧认证保持兼容，未知令牌失败关闭；新增 OIDC 会话与旧刷新会话分开适配，不删除 `AccessSessionValidator` 以求兼容。
7. 协议端点严格遵循 ADR-0011；管理 API 保持 ProblemDetails、精确权限和源生成契约，认证限流和秘密保护没有通配例外。
8. Vue 是唯一后台交付线，Layui 冻结；第二应用是受控 SSO 验证夹具，不扩为产品线。
9. 本计划不批准生产发布、推送、全局信任扩大、动态客户端注册或迁移现有全部客户端。生产启用需满足对应发布流程，旧入口退役需有回退证据。
10. 代码／SQL 注释中文；令牌、Cookie、凭据和敏感 Claim 不进入日志、Trace、HAR 或文档。

## 3. 文件与职责地图

以下“新增”文件是后续任务的目标，并非当前已存在。T00 验证组件 API 后，如需调整内部类名，在本计划同步精确路径；不得静默扩大为新生产项目。

| 类型 | 文件／目录 | 职责与阶段 |
| --- | --- | --- |
| 修改 | `Directory.Packages.props`、`src/Modules/Full.NET.Modules.Identity/Full.NET.Modules.Identity.csproj`、`THIRD-PARTY-NOTICES` | T00 锁定协议组件与许可，真实引入时登记 |
| 新增 | `src/Modules/Full.NET.Modules.Identity/Configuration/IdentityOidcOptions.cs` | T00/T03 显式启用、Issuer、固定客户端与回调配置 |
| 新增 | `src/Modules/Full.NET.Modules.Identity/DependencyInjection/IdentityOidcServiceCollectionExtensions.cs` | T00/T03 静态注册与协议边界 |
| 新增 | `src/Modules/Full.NET.Modules.Identity/Oidc/Stores/IdentityOidcApplicationStore.cs`、`IdentityOidcAuthorizationStore.cs`、`IdentityOidcScopeStore.cs`、`IdentityOidcTokenStore.cs`（后 3 个与首文件同目录） | T01 组件 Store 适配，不拥有独立 DbConnection |
| 新增 | `src/Modules/Full.NET.Modules.Identity/Persistence/IdentityOidcSql.cs`、`IdentityOidcRecords.cs`（同目录） | T01 双库 SQL 与本地持久化记录 |
| 新增 | `src/Modules/Full.NET.Modules.Identity/Oidc/IdentityOidcSessionService.cs`、`IdentityOidcPrincipalFactory.cs`、`IdentityOidcAccessSessionValidator.cs`（同目录） | T02 中心／客户端会话、最小 Claim 投影、资源端权威验证 |
| 修改 | `src/Modules/Full.NET.Modules.Identity/DependencyInjection/IdentityAuthenticationServiceCollectionExtensions.cs`、`src/Modules/Full.NET.Modules.Identity/IdentityModule.cs` | T02/T03 新旧 Scheme 路由与装配，不改变旧登录默认行为 |
| 修改 | `src/Modules/Full.NET.Modules.Identity/Features/ManageHostOnlineSessions/HostOnlineSessionManagementService.cs`、`src/Modules/Full.NET.Modules.Identity/Persistence/OnlineSessionSql.cs`、`IdentitySql.cs`（后两者同目录） | T02 既有在线会话查询、单会话／全部会话撤销接入 OIDC 会话权威源；T07 扩展跨应用通知 |
| 修改 | `src/Modules/Full.NET.Modules.Identity/Features/ChangeSessionContext/IdentitySessionContextService.cs` | T02 按可信会话来源选择上下文更新和令牌签发路径，保留客户端授权边界 |
| 修改 | `src/Modules/Full.NET.Modules.Identity/Security/CurrentSessionAuthorization.cs`、`BackgroundSessionBindingValidator.cs`、`BackgroundSessionAuthorization.cs`（同目录） | T02 交互工具、审批与后台任务接入新旧权威会话适配，保持逐次授权 |
| 核对并按适配需要修改 | `src/Modules/Full.NET.Modules.Identity.Contracts/SessionBindingSnapshot.cs`、`ICurrentSessionAuthorization.cs`、`IBackgroundSessionBindingValidator.cs`、`IBackgroundSessionAuthorization.cs`（同目录）；`src/Modules/Full.NET.Modules.Ai/Features/ManageAgentRuns/AgentRunHttpBinding.cs`、`src/Modules/Full.NET.Modules.Ai/Runtime/AiAgentRunCoordinator.cs` | T02 冻结绑定的可信会话来源与稳定标识，兼容既有持久化任务；仅通过稳定 Contract Port 访问 Identity |
| 新增 | `src/Modules/Full.NET.Modules.Identity/Features/OidcAuthorization/Endpoint.cs`、`src/Modules/Full.NET.Modules.Identity/Features/OidcSession/Endpoint.cs` | T03 协议交互和中心登录／退出衔接；协议处理交给组件 |
| 修改 | `src/Modules/Full.NET.Modules.Identity/Serialization/IdentityJsonSerializerContext.cs` | T01—T03 新增自有 DTO 的静态 JSON 闭包；先核对真实类型名 |
| 修改 | `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer/`、`src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/MySql/` | T01 同编号新增协议状态／会话结构，编号按实施时库存分配 |
| 修改 | `eng/testing/test-matrix.json`、`contracts/architecture/global-sql-statements.json`、`contracts/database/object-comments.json` | 随实际测试／SQL／DDL 更新唯一机器登记源 |
| 新增 | `tests/Full.NET.UnitTests/Identity/IdentityOidcOptionsTests.cs`、`IdentityOidcSessionTests.cs`、`IdentityOidcPrincipalTests.cs`（同目录） | T00/T02 纯规则与注册负向用例 |
| 新增 | `tests/Full.NET.ArchitectureTests/IdentityOidcBoundaryTests.cs` | T00/T03 依赖方向、协议响应例外范围与 AOT 闭包边界 |
| 新增 | `tests/Full.NET.IntegrationTests/Identity/IdentityOidcStoreAssertions.cs`、`IdentityOidcProtocolAssertions.cs`、`IdentityOidcSessionAssertions.cs`（同目录） | T01—T03 同一断言供双库入口调用 |
| 修改 | `tests/Full.NET.IntegrationTests/Api/IdentityApiSqlServerTests.cs`、`IdentityApiMySqlTests.cs`（同目录） | 挂接双库协议／会话用例，避免只有辅助方法没有测试入口 |
| 修改 | `tests/Full.NET.IntegrationTests/NativeAot/NativeApiE2EAssertions.cs` 及对应 MySQL／SQL Server 入口 | T04 在原生产物验证完整最小流程 |
| 新增 | `tests/Full.NET.IntegrationTests/Identity/IdentityOidcRelyingPartyFixture.cs` | T03/T05 同一受控夹具建立两个独立 Origin／client_id；不作为生产宿主 |
| 新增 | `tests/e2e/admin-real-stack/tests/identity-oidc-sso.spec.mjs` | T05 双客户端真实浏览器验收，沿用现有工具和 CI 生命周期 |
| 修改 | `ui/admin/src/auth/session.ts`、`ui/admin/src/router/index.ts`、`packages/client-contracts/` | 仅 T08 迁移 Vue 与共享客户端，不提前改变现有登录 |

迁移清单、命名规则和其他受影响文件随任务影响集补齐；不在此预占迁移号，也不为文件地图提前创建空文件。

## 4. 执行顺序与任务

### T00（P0）：冻结最小实验输入，验证依赖和静态注册

**输入：** 已接受 ADR、当前工作区、现有 Identity 项目与 AOT 注册方式。

**产出：** 组件精确版本与依赖清单、被验证的静态 DI 方式、协议／会话接口清单；证据写回研究报告的 P0 执行记录，不另建竞争设计文档。

- [ ] 记录当前分支、HEAD、Identity 相关既有改动；代码工作开始前创建 `identity-oidc-sso-p0` 快照，保护其他任务文件。
- [ ] 阅读 `fullnet-module-delivery`、安全／数据／命名／Native AOT 规则；核对实际项目、测试入口和迁移最新编号。
- [ ] 从官方资料确定当前受支持的 OpenIddict 版本、许可证、Store 接口与传递依赖，检查漏洞和目标框架。记录真实版本，不从研究日期推断最新版本。
- [ ] 在 `IdentityOidcOptionsTests` 首先表达：生产无明确 Issuer／持久化密钥拒绝启动；协议默认未启用；回调不允许任意来源。运行并保留因缺少能力而失败的证据。
- [ ] 最小引入选定包并静态注册协议处理器，保持生产新入口未启用；增加 Architecture 断言，禁止 EF、跨模块实现引用及通配协议授权例外。
- [ ] 在测试矩阵登记 `identity-oidc` Unit／Architecture 聚焦集与实际最低发现数；使用现有套件命令验证，不在文档复制动态门槛。
- [ ] 执行 AOT analyzer；无法解释的反射／动态泛型或传递依赖要求先记录为失败，不能跳过分析器。

**通过条件：** 选定版本可进入现有闭包，负向配置与边界测试发现非零且通过；这只证明静态可行，不证明原生发布或 SSO 可运行。

### T01（P0）：协议持久化与一次性状态

**消费：** T00 确认版本对应的 `IOpenIddictApplicationStore<T>`、`IOpenIddictAuthorizationStore<T>`、`IOpenIddictScopeStore<T>`、`IOpenIddictTokenStore<T>` 完整接口清单。

**提供：** 四类 Store 的静态注册、客户端／授权／Scope／Token 数据查询和状态改变；记录类型与 SQL 只在 Identity 内可见。

- [x] 在 `IdentityOidcStoreAssertions` 建立 V03/V15/V19 的 RED：跨实例并发消费同一授权码最多一个成功；撤销后不能复活；中断迁移可恢复。
- [x] 依据当前数据库库存确定同一迁移编号（**216**），为双库创建协议记录、约束和索引，登记命名、对象注释与认证前 Global SQL 精确边界。
- [x] 通过自有执行器实现 Store；列出每个组件查询接口的受支持语义，不以空结果、客户端全表过滤或运行时动态 SQL 假装支持。
- [x] 实现原子条件更新、并发版本、撤销与过期清理；同一事务失败时不得遗留有效授权或部分成功状态。
- [x] 登记 Dapper AOT 参数／物化器及自有 JSON 元数据；明确协议序列化由组件提供，不把普通业务 camelCase 应用于协议字段。
- [x] 挂接 SQL Server/MySQL 测试入口；执行实际受影响双库 CI，记录同一场景结果、失败与跳过。

**通过条件：** V03/V15/V19 的 Store 层条件在双库成立，原生运行仍待 T04；不把内存 Store 通过作为此任务通过。

### T02（P0）：中心会话、应用会话与旧会话衔接

**消费：** 现有账号／安全戳／权限读取与 T01 协议持久化。

**提供：** `IdentityOidcSessionService` 建立和撤销中心／应用会话；`IdentityOidcPrincipalFactory` 按身份或资源用途投影最小 Claim；`IdentityOidcAccessSessionValidator` 在资源请求中核验关联会话与账号权威状态。精确内部方法签名在开始编码前根据 T00/T01 实际类型登记到本任务。

**消费方适配交付：** 覆盖文件地图中的现有管理入口、上下文切换、交互工具及后台任务，不能以新增 JwtBearer 验证器替代这些消费者的适配。会话来源由已验证的认证 Scheme／Issuer 和服务端映射确定；后台绑定保留可权威解析的来源、稳定会话标识与客户端关联，不接受调用方自报。需要演进 Contracts 或持久化绑定时，先登记最小契约及旧记录读取策略，不引入第三方协议 DTO 或跨模块 SQL。

- [ ] 在 Unit 与 `IdentityOidcSessionAssertions` 首先建立 V10/V12/V13/V21/V23 的 RED：错误令牌类型、伪造租户、停用账号、旧刷新记录轮换和状态库故障不得放行。
- [ ] 为稳定中心会话、客户端会话和令牌族建立显式关联；不改变旧 `sid` 的解析规则，不让新旧 Scheme 互相兜底。
- [ ] 复用账号密码、锁定、强制改密与安全戳判断；新中心登录成功后仍需按同一安全策略创建会话，禁止中心 Cookie 绕过停用／强制下线。
- [ ] 按 client_id、aud 与用途控制 Claim；标准 scope 与 `fullnet_scope` 分开，不向一般外部应用发安全戳和超管快照。
- [ ] 固定主体到业务上下文的适配规则：受信 Issuer、已登记客户端、有效应用会话和本地账号映射全部成立后才构造租户／权限上下文。
- [ ] 在接通现有强制下线入口前，冻结单会话与全部会话操作对应的中心／应用会话、令牌族及再授权阻断集合，保留原操作权限和 Host／租户边界。在线会话查询与撤销必须使用一致的目标集合；用户没有旧刷新记录但仍有 OIDC 会话时，不得提前返回“撤销零条”的成功结果。通过 Identity 自有事务边界完成权威撤销与审计，失败不能留下部分有效状态或报告成功；跨应用 Cookie 通知与传播窗口在 T07 扩展。
- [ ] 改造 `IdentitySessionContextService`：按可信会话来源更新对应应用的上下文并选择原认证体系的签发路径。OIDC 切租户不得通过旧 `IAccessTokenIssuer` 换成旧令牌；组件签发的新令牌仍受原 Issuer、client_id、aud 和已批准 scope 约束，不扩大授权。定义切换后的旧令牌／后台绑定如何按上下文版本失效，并覆盖并发切换与撤销竞争；B 应用的上下文不随 A 改变。
- [ ] 接通 `CurrentSessionAuthorization`、`BackgroundSessionBindingValidator` 与后台授权 Port；新旧会话均重新检查账号、会话、租户和当前权限。内部安全戳从权威映射获得，不通过向一般外部应用发安全戳来修补消费者。既有后台绑定仅按明确的旧格式解析，未知来源或无法映射的绑定拒绝，不在两套会话之间试探兜底。
- [ ] 将 §6 的三组入口验收拆为可执行断言：T02 验证服务／Port 及双库状态转换；T03 使用真实签发的 OIDC 令牌经 HTTP 入口和工具调用链重跑。保留旧体系对应的成功与拒绝场景，不以仅调用新增 OIDC 服务代替现有入口验收。
- [ ] 对旧登录、刷新、退出、改密、上下文切换与最后一名保护执行回归；双库测试验证状态改变后的后续请求拒绝。

**通过条件：** 三组消费方适配及双库服务／Port 断言成立，中心不能凭旧认证重建已撤销权限；旧体系回归与新会话正常业务路径同时通过。完整协议入口证据由 T03 补齐，未完成时不能关闭 P0。既有请求处理中不宣称回溯撤销，后台任务在下一次执行／副作用授权检查时拒绝。

### T03（P0）：最小标准授权闭环与两个客户端夹具

**消费：** T00 配置／静态注册、T01 Store、T02 会话与主体构造。

**提供：** 实际 Discovery/JWKS、Authorize/Token/UserInfo、中心登录交互和两个固定客户端夹具；P0 客户端 UI 保持最小，不构建完整管理后台。

- [ ] 在 `IdentityOidcProtocolAssertions` 表达 V01/V02/V04/V08/V10/V18：先证明当前缺少协议能力导致失败，再实现协议接入，不能以编译失败或环境缺失代替 RED。
- [ ] 明确 URI 与端点启用集合，委托组件处理协议；由 Full.NET 处理账号交互和权威授权，不手写另一套授权码／JWT 协议引擎。
- [ ] 配置 A、B 独立 client_id／Origin／回调、PKCE S256 与机密客户端认证；夹具秘密从临时测试环境提供。
- [ ] 建立 state／correlation／nonce 验证与 UserInfo sub 一致性；以去敏后的字段存在性和断言记录流程，不保存令牌正文。
- [ ] 验证未请求 offline_access 时不误报已获得 Refresh Token；保存票据与刷新执行分别实现和验证。
- [ ] 证明业务 API 仍返回 ProblemDetails，协议端点返回标准错误且不受通用包络改写；边界测试拒绝未登记协议例外。
- [ ] 执行 JWT 格式适配实验，确定签名 JWT／JWE／其他受控验证模式；资源 API 不能通过持有中心私钥获得兼容。
- [ ] 使用 A／B 实际签发的令牌执行 §6 三组入口验收，覆盖仅 OIDC 会话与新旧混合会话；真实调用现有强制下线、切租户以及工具／审批／后台任务入口，证明认证中间件与消费方适配一起生效。

**通过条件：** 两个客户端各自完成真实 HTTP 授权与令牌验证，授权码绑定、三组入口验收和负向用例双库通过；跨浏览器完整体验留给 T05。

### T04（P0）：Linux Native AOT 与双实例可行性结论

**消费：** T00—T03 可运行最小链路。

**提供：** P0 Go／No-go 结论及可定位的 CI 证据；只在全部门禁通过后进入 P1。

- [ ] 将 V01/V03/V10/V13/V20 挂接到现有 Native API 外部进程夹具和双库入口，不只在 JIT 测试里调用内部类。
- [ ] 实测原生产物启动、依赖解析、授权／换码、数据库并发消费、验签和会话撤销。
- [ ] 将 §6 三组入口验收的最小成功／拒绝路径挂入原生产物双库测试，覆盖切租户后的令牌验证、现有管理入口强制下线及工具／后台授权适配的 DI 与序列化闭包，补齐 V12/V21，不能只验证新 OIDC 服务可解析。
- [ ] 执行 V16/V17 最小探针：双实例交错换码、中心重启、共享必要 key ring、公钥轮换；无进程级临时密钥或状态独占依赖。
- [ ] 记录组件／数据库／RID／提交、工作流链接、通过与失败步骤、未验证项；将研究报告 V01—V24 中确实执行的具体层次分别标记，不能整表勾选。
- [ ] 审查新旧认证与数据边界、依赖许可和风险。P0 失败时保持原登录，不继续生产接入；通过后按已批准方向推进。

**通过条件：** fresh Linux 原生双库证据与旧安全语义均成立；Windows discovery、普通 build 或 analyzer 不能单独作 Go 依据。

### T05（P1）：真实浏览器最小 SSO

**主要文件：** T03 客户端夹具、中心登录／退出 Endpoint、`tests/e2e/admin-real-stack/tests/identity-oidc-sso.spec.mjs` 及现有测试启停配置。

- [ ] 先建立 V05/V06/V07/V22 的失败浏览器场景：A 登录后 B 免密；仅辅助 Cookie 不通过；prompt/max_age 有效；受限第三方 Cookie 环境可正常顶层跳转。
- [ ] 完成中心登录、必要交互、回调错误展示和本地退出；每个客户端保持独立会话，第二客户端不复制 A 的令牌。
- [ ] 验证 Cookie Scheme、Secure/HttpOnly/SameSite、回调模式和 CSRF；不降低旧 Strict Refresh Cookie 属性来完成跨站登录。
- [ ] 补齐错误 audience、缺 scope、无用户权限和数据归属的直接 API 拒绝，覆盖 V10/V11/V18/V24。
- [ ] 双库真实栈通过后记录 P1 结果；登录 UI 与错误需遵循现有语言、可访问性和秘密保护规则。

**通过条件：** 两个应用的实际浏览器 SSO 与负向流程成立；“未再次输密码”必须来自中心有效会话，不是共享应用 Cookie。

### T06（P2）：客户端、Scope 与授权治理

**目标文件：** 新增 `src/Modules/Full.NET.Modules.Identity/Features/ManageOidcClients/`、`Features/ManageOidcAuthorizations/`（均在 Identity 项目内），对应 Unit／双库断言及 `ui/admin/src/views/OidcClientsView.vue`、`OidcAuthorizationsView.vue`（同目录），共享客户端契约按既有生成流程产出。

- [ ] 建立 V08/V09/V11/V15/V19 的失败场景，特别是另一实例命中旧客户端缓存后的禁用／撤销行为。
- [ ] 提供精确权限的客户端注册管理、secret 轮换、回调／scope 配置、授权查看与撤销；公开客户端不强行分配保密 secret。
- [ ] 用逐操作权限保护管理 API 与 Vue，规范化 OpenAPI 与共享契约；业务管理错误保持 ProblemDetails。
- [ ] 实现刷新令牌族重用检测、撤销、过期清理和幂等管理；证明双库及多实例缓存不削弱权威状态。
- [ ] 完成密钥材料管理和轮换流程，覆盖 V17/V24；不加入自动动态客户端注册。

**通过条件：** 客户端／授权生命周期可管理且跨实例生效；协议与管理权限保持独立。

### T07（P2）：全局退出、撤销传播与故障治理

**目标文件：** T02 会话服务、T06 授权管理、Identity Worker 最小清理／投递入口及相应双库／多实例测试。只有确认需要可靠跨应用投递时新增 Identity 所有的退出通知状态，按既有 Outbox 边界注册。

- [ ] 以 T02 已冻结的强制下线权威撤销集合和 T03 现有入口验收为前置，在实施前补齐当前应用退出、全局退出的目标集合、外部 API 令牌窗口、通知重试与可接受传播时限并写入规格；随后按固定时限实测，不根据测试结果反向放宽门槛。不得把 Full.NET 本地权威撤销延迟到通知成功后，也不预先宣称跨应用即时完成。
- [ ] 建立 V13/V14/V15/V16/V17/V23 的 RED：客户端离线、重复／伪造通知、状态库故障、应用重启和 key ring 轮换。
- [ ] 根据选定组件实测支持选择标准退出通知；接收方验证 Issuer／Audience／会话关联与重放保护，不能只信任 sid 或回调来源 IP。
- [ ] 实现幂等撤销、必要的可靠通知和有界重试；区分清 Cookie、撤销刷新与拒绝既有 Access Token。
- [ ] 用真实时钟测量传播上界和故障窗口，记录何时中心已撤销、何时各应用／资源拒绝；未达标时保持门禁未通过。

**通过条件：** 定义的失效语义有实测证据，通知失败不误报全局成功，Full.NET 后续管理请求仍执行权威检查。

### T08（P3）：Vue 迁移、兼容与发布准备

**目标文件：** `ui/admin/src/auth/session.ts`、路由、登录／退出 UI，`packages/client-contracts/`，T05 的真实栈用例与正式部署配置。

- [ ] 基于 P0/P1 结果冻结 Vue 服务端回调或 BFF、票据存储和更新策略，以及旧令牌的受信窗口；不自动引入新的生产宿主。
- [ ] 先为 V09/V12/V14/V18/V21/V22/V24 建立失败回归：并发刷新、切租户、退出、CSRF、旧入口和未知令牌拒绝。
- [ ] 迁移登录／回调／退出与错误展示，保留共享权限与租户运行时；浏览器不自行解析未验证 JWT 建立可信授权。
- [ ] 复用 T02/T03 已验证的上下文签发和会话消费者适配，Vue 验收同时覆盖切租户后的业务请求、工具／审批、后台任务和现有强制下线操作；不得到 P3 才补接服务端会话消费者。
- [ ] 执行旧登录并行和回退演练：切回入口时同时验证 Issuer／Scheme／会话信任，不能仅切页面；已轮换、过期或已撤销的 Refresh Token、已撤销会话和未知 Issuer 继续拒绝；受信窗口内仍有效的旧体系令牌按兼容策略处理。
- [ ] 执行全部适用 V01—V24 与 Vue 可访问性／多语言／双库真实栈回归，保留发布、监控和回退记录。
- [ ] 仅在实际通过相应门禁后更新能力状态；旧入口退役另记录版本、存活窗口与恢复方式，生产发布遵循授权和现有发布流程。

**通过条件：** Vue 真实业务与安全语义不退化，生产启用和回退条件完整；Layui 无新增实现要求。

## 5. 验证入口与运行位置

本节是执行时的命令索引，不替代[开发质量 §11](../../../rules/development-quality.md#11-测试与验证)。本次文档任务未运行下面的代码命令。

代码任务开始时：

```powershell
git branch --show-current
git status --short
git rev-parse HEAD
pnpm test:task:start -- identity-oidc-sso-p0
pnpm test:integration:affected:plan -- --snapshot identity-oidc-sso-p0 --phase inner
```

T00 将聚焦集登记到测试矩阵后，使用以下入口；目前 `identity-oidc` 选择器尚未创建，不应直接运行或报告通过：

```powershell
pnpm test:dotnet:unit -- --selection identity-oidc
pnpm test:dotnet:architecture -- --selection identity-oidc
pnpm test:aot:analyzers
pnpm test:dotnet:architecture -- --selection api-native-aot
```

数据与测试登记改变时运行现有 `pnpm test:naming`、`pnpm test:sql-safety`、`pnpm test:integration:partitions` 和 `pnpm test:governance`；接口／Vue 改变时按影响集加入现有 OpenAPI、客户端和页面测试。每条命令必须有真实发现数、退出码、失败与跳过信息，预期结果是受影响断言通过而非零发现。

切片关闭按当前任务快照审查 affected `slice`／`merge`，在 GitHub Actions 执行双库和真实栈。T04 的发布与原生运行命令为 `pnpm test:aot:publish:linux`、`pnpm test:aot:native:e2e`，只在适当 Linux 执行环境运行并保留实际产物证据；脚本发现测试而跳过不算成功。

未授权推送时按 §11 记录待 CI 边界；不为触发 CI 擅自推送，也不改为启动无关重型本地套件。重大认证切片完成后使用 `requesting-code-review` 审查，修复重要问题后再交付。

## 6. 场景覆盖与退出审计

| 验证编号 | 责任任务 |
| --- | --- |
| V01、V02、V04 | T03、T04、T05 |
| V03 | T01、T03、T04 |
| V05、V06、V07 | T05 |
| V08、V09 | T03、T06、T08 |
| V10 | T02、T03、T04、T05 |
| V11 | T05、T06 |
| V12、V21 | T02、T03、T04、T08 |
| V13 | T02、T03、T04、T07 |
| V14 | T07、T08 |
| V15、V19 | T01、T06、T07 |
| V16、V17 | T04、T06、T07 |
| V18 | T03、T05、T08 |
| V20 | T04 |
| V22 | T05、T08 |
| V23 | T02、T03、T07 |
| V24 | T05、T06、T08 |

以下三组为既有 V 编号的必验子场景，不另设竞争编号或仅凭文档勾选通过。T02 交付服务／Port 断言，T03 接真实协议和现有入口，T04 验证最小原生闭环，T08 验收 Vue 消费；涉及持久化的场景均需 SQL Server/MySQL 同场景证据。

| 入口与验证编号 | 必验成功／拒绝场景 | 结果要求 |
| --- | --- | --- |
| 现有在线会话管理 API；V13/V21/V23 | 仅旧会话、仅 OIDC 会话、新旧混合；按已冻结集合分别执行单会话／全部强制下线；重复撤销、无权限、跨租户及状态写入故障 | 查询／撤销集合一致；仅 OIDC 用户不误报零撤销；目标会话后续管理请求、刷新和旧中心 Cookie 再授权拒绝，非目标会话不误伤；失败不报告成功，审计与权威撤销一致 |
| 现有上下文切换入口；V12/V21 | A／B 独立应用会话，A 切租户后访问真实业务 API；旧体系对应回归；并发切换、与撤销竞争、缺权限及伪造租户 | 返回令牌保持原认证体系、客户端、受众和授权范围约束；A 的旧令牌／后台绑定按冻结版本策略处理，B 不变；新会话不回落到旧签发器，未授权租户拒绝 |
| 工具、审批、后台任务入口及执行 Port；V13/V21/V23 | 新旧登录均完成一次有权限操作；任务排队后撤销会话、停用账号、撤权或切换上下文，再恢复执行；旧持久化绑定、未知来源绑定和状态库故障 | 正常新会话不会因查询旧刷新表而被拒绝；后续执行／副作用前权威授权拒绝失效绑定；旧记录兼容不依赖外发安全戳，不跨模块直查 Identity 表，不在失败时退回旧验证器 |

通过 P0 后在本计划记录 Go／No-go 和证据，不另建平行计划；任务完成复核输入、文件、断言与未验证项后才勾选。失败／跳过不得借文档批准、历史 JWT 验证或组件官方 AOT 声明转为通过。
