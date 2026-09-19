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
- 当前状态（2026-09-16 接手复核）：已有 OpenIddict、Dapper Store、中心登录、令牌、会话适配和治理实现及测试入口；本轮继续安全修复。T02/T03 尚未满足全部退出条件，T04 缺本次提交的 Linux 原生证据，P0 未关闭；不能再沿用“均未开始”，也不能将已有勾选等同生产验收。
- 本轮接手基线：`main`，HEAD `0ccfbe1cecae2a4f30698df8a279cb91c3e8cc8d`；任务快照 `identity-oidc-takeover-20260916`。保留其他任务文档与本地产物；未提交、推送或部署。
- 本文件是本主题唯一活动执行计划。P0 通过后按已确认阶段继续推进，正常实现选择无需重复审批；改变 ADR 的数据、运行或安全边界时先形成证据与决策修订。

### 2026-09-16 接手修复与剩余顺序

本轮是 T02/T03 的安全修复切片；历史执行记录保留，当前可接受证据见[研究验证记录 §12](../../verification/2026-09-13-identity-oidc-sso-research-validation.md#12-2026-09-16-接手审查与安全修复)。

| 顺序 | 工作及退出条件 | 当前状态 |
| --- | --- | --- |
| 1 | 恢复权威查询前的 Host／租户／未解析上下文；异常和取消同样恢复；仅接受精确 access 用途 | 已实现，新增可失败 Unit 回归 |
| 2 | 中心凭据 POST 防伪校验先于 Cookie／凭据副作用；max_age 强制重新认证且保留 auth_time | 已实现，Unit 回归；真实防伪表单已接入双库／原生共享夹具，运行待 CI |
| 3 | profile scope 控制公开 Claim；加密授权码／刷新令牌保留客户端分类；第一方权限保持多值 | 已实现，覆盖两次签发处理；JWT 实际输出待双库 CI |
| 4 | 继续核对 UserInfo、授权码兑换及刷新后的账号／应用／中心会话权威检查，覆盖撤销、账号停用和状态库故障 | 已补齐 authorize fail-closed、must-change@authorize、refresh 滑动延长应用会话与协议端点 outage 矩阵 |
| 5 | 完成 OIDC 原体系切租户签发、A/B 应用隔离与旧令牌／后台绑定失效 | 已实现 `ChangeOidcAsync` 与双库回归；切租户后旧 refresh 失效，新 access 在窗口内有效 |
| 6 | 同一候选提交执行双库、真实浏览器、防伪攻击场景、Linux Native AOT 与多实例验证，再决定 P0 Go/No-go | 待 CI；沿用现有入口，不降低门禁；T05—T08 不据此升级 Verified |

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

- [x] 在 Unit 与 `IdentityOidcSessionAssertions` 首先建立 V10/V12/V13/V21/V23 的 RED：错误令牌类型、伪造租户、停用账号、旧刷新记录轮换和状态库故障不得放行。
- [x] 为稳定中心会话、客户端会话和令牌族建立显式关联；不改变旧 `sid` 的解析规则，不让新旧 Scheme 互相兜底。
- [x] 复用账号密码、锁定、强制改密与安全戳判断；新中心登录成功后仍需按同一安全策略创建会话，禁止中心 Cookie 绕过停用／强制下线。
- [x] 按 client_id、aud 与用途控制 Claim；标准 scope 与 `fullnet_scope` 分开，不向一般外部应用发安全戳和超管快照。
- [x] 固定主体到业务上下文的适配规则：受信 Issuer、已登记客户端、有效应用会话和本地账号映射全部成立后才构造租户／权限上下文。
- [x] 在接通现有强制下线入口前，冻结单会话与全部会话操作对应的中心／应用会话、令牌族及再授权阻断集合，保留原操作权限和 Host／租户边界。在线会话查询与撤销必须使用一致的目标集合；用户没有旧刷新记录但仍有 OIDC 会话时，不得提前返回“撤销零条”的成功结果。通过 Identity 自有事务边界完成权威撤销与审计，失败不能留下部分有效状态或报告成功；跨应用 Cookie 通知与传播窗口在 T07 扩展。
- [ ] 改造 `IdentitySessionContextService`：按可信会话来源更新对应应用的上下文并选择原认证体系的签发路径。OIDC 切租户不得通过旧 `IAccessTokenIssuer` 换成旧令牌；组件签发的新令牌仍受原 Issuer、client_id、aud 和已批准 scope 约束，不扩大授权。定义切换后的旧令牌／后台绑定如何按上下文版本失效，并覆盖并发切换与撤销竞争；B 应用的上下文不随 A 改变。
- [x] 接通 `CurrentSessionAuthorization`、`BackgroundSessionBindingValidator` 与后台授权 Port；新旧会话均重新检查账号、会话、租户和当前权限。内部安全戳从权威映射获得，不通过向一般外部应用发安全戳来修补消费者。既有后台绑定仅按明确的旧格式解析，未知来源或无法映射的绑定拒绝，不在两套会话之间试探兜底。
- [ ] 将 §6 的三组入口验收拆为可执行断言：T02 验证服务／Port 及双库状态转换；T03 使用真实签发的 OIDC 令牌经 HTTP 入口和工具调用链重跑。保留旧体系对应的成功与拒绝场景，不以仅调用新增 OIDC 服务代替现有入口验收。
- [ ] 对旧登录、刷新、退出、改密、上下文切换与最后一名保护执行回归；双库测试验证状态改变后的后续请求拒绝。

**通过条件：** 三组消费方适配及双库服务／Port 断言成立，中心不能凭旧认证重建已撤销权限；旧体系回归与新会话正常业务路径同时通过。完整协议入口证据由 T03 补齐，未完成时不能关闭 P0。既有请求处理中不宣称回溯撤销，后台任务在下一次执行／副作用授权检查时拒绝。

### T03（P0）：最小标准授权闭环与两个客户端夹具

**消费：** T00 配置／静态注册、T01 Store、T02 会话与主体构造。

**提供：** 实际 Discovery/JWKS、Authorize/Token/UserInfo、中心登录交互和两个固定客户端夹具；P0 客户端 UI 保持最小，不构建完整管理后台。

- [x] 在 `IdentityOidcProtocolAssertions` 表达 V01/V02/V04/V08/V10/V18：先证明当前缺少协议能力导致失败，再实现协议接入，不能以编译失败或环境缺失代替 RED。
- [x] 明确 URI 与端点启用集合，委托组件处理协议；由 Full.NET 处理账号交互和权威授权，不手写另一套授权码／JWT 协议引擎。
- [x] 配置 A、B 独立 client_id／Origin／回调、PKCE S256 与机密客户端认证；夹具秘密从临时测试环境提供。
- [x] 建立 state／correlation／nonce 验证与 UserInfo sub 一致性；以去敏后的字段存在性和断言记录流程，不保存令牌正文。
- [x] 验证未请求 offline_access 时不误报已获得 Refresh Token；保存票据与刷新执行分别实现和验证。
- [x] 证明业务 API 仍返回 ProblemDetails，协议端点返回标准错误且不受通用包络改写；边界测试拒绝未登记协议例外。
- [x] 执行 JWT 格式适配实验，确定签名 JWT／JWE／其他受控验证模式；资源 API 不能通过持有中心私钥获得兼容。（P0 选定签名 JWT + `token_use=access`，禁用 Access Token 加密）
- [ ] 使用 A／B 实际签发的令牌执行 §6 三组入口验收，覆盖仅 OIDC 会话与新旧混合会话；真实调用现有强制下线、切租户以及工具／审批／后台任务入口，证明认证中间件与消费方适配一起生效。

**通过条件：** 两个客户端各自完成真实 HTTP 授权与令牌验证，授权码绑定、三组入口验收和负向用例双库通过；跨浏览器完整体验留给 T05。

### T04（P0）：Linux Native AOT 与双实例可行性结论

**消费：** T00—T03 可运行最小链路。

**提供：** P0 Go／No-go 结论及可定位的 CI 证据；只在全部门禁通过后进入 P1。

- [x] 将 V01/V03/V10/V13/V20 挂接到现有 Native API 外部进程夹具和双库入口，不只在 JIT 测试里调用内部类。
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

### T08 冻结：oidc-center 会话策略（2026-09-17）

- **回调 URL**：`#/identity/oidc/callback`（hash 路由，匿名守卫白名单）。
- **Refresh 存储**：仅 `sessionStorage` 键 `fullnet.admin.oidc.refresh`；access token 仅内存，不落 localStorage。
- **切租户**：`onTenantContextTokenResponse` 写回轮换后的 refresh；`identity.session_context_conflict` 时刷新凭据并最多重试一次，成功后重载权限快照；刷新或重试失败时不继续使用旧授权快照。
- **并发刷新**：`sessionRefreshCoordinator` 单飞，禁止并行 `/connect/token` refresh。
- **多标签强撤**：`handleRemoteSessionRevoke` 清本地凭据并导航登录；重复通知幂等。
- **legacy 并行**：默认 `legacy` 密码登录；`VITE_IDENTITY_AUTH_MODE=oidc-center` 为构建时注入，Helm 未内置该变量。

### T08 部署与监控（2026-09-17）

| 项 | 说明 |
| --- | --- |
| 构建变量 | `VITE_IDENTITY_AUTH_MODE=oidc-center`、`VITE_IDENTITY_OIDC_CLIENT_ID`（默认 `admin-spa`）；见 `ui/admin/.env.example` |
| 签名密钥 | `IdentityOidcOptions.SigningKeys` ConfigMap/Secret；多实例必须共享 `EncryptionKeyBase64`（32 字节）与 RSA 私钥；通过治理 API 激活 kid，见 `OidcSigningKeysView` |
| 监控指标 | `authorize`/`token` 4xx/5xx 比率、`refresh_token` reuse 拒绝计数、会话 revoke 到 access 401 滞后（应 ≤1 请求）、realtime 投递失败率 |
| 回退 | 移除 `VITE_IDENTITY_AUTH_MODE` 重建 legacy 前端；已撤销 OIDC 会话不能通过遗留 refresh 复活（`admin-oidc-center` E2E 探针） |

**T08 执行记录（2026-09-16，进行中）：** 已交付可选 `oidc-center` 消费路径（PKCE 登录、回调、refresh、双端 logout、实时强撤、切租户与业务页探针）并保持 `legacy` 默认并行；单元与 E2E 用例见[验证记录 §13](../../verification/2026-09-13-identity-oidc-sso-research-validation.md#13-t08-vue-消费与并行入口2026-09-16)。`admin-oidc-center.spec.mjs` 共 **130** 项串行探针，覆盖 §6 工具／审批／后台任务最小 UI 与操作路径、三类 Host API 正／负探针、在线会话撤销后 access/refresh 拒绝，以及 OIDC 创建排队 Agent Run 正探针（三上下文 clientRequestId 幂等后读取仍为排队并可取消，含幂等取消后重复取消负探针与三上下文 UI 加载并取消）、切租户／切租户返回 Host 后后台任务触发与执行历史 API／UI 正探针及退出／强撤后绑定失效、退出／强制下线后后台任务触发与定义列举、已排队 Agent Run 读取／取消／恢复／新建绑定失效（`expectRevokedOidcCenterAgentRunAccessRejected`，含切租户并返回 Host 往返后创建）；单元层补充并行 `restore` 不重叠 token 交换（V09/V18）；`auth-smoke` 与 `spec-contracts` 治理测试登记 legacy 回退与 oidc-center 项目入口。聚焦真实栈入口：`pnpm test:e2e:real:oidc-center`。上述清单项整体仍未勾选通过——缺 CI `real-stack-e2e` fresh TRX 证据（门禁已登记：`pnpm test:e2e:real` 含 `vue-admin-oidc-center`）、旧入口回退演练完整执行（`auth-smoke` 已覆盖遗留 OIDC 凭据不阻断 legacy 登录的自动化探针）、§6 **全量**矩阵与能力状态门禁；§6 **最小**矩阵 E2E 探针已编写完毕（见验证记录 §13 对照表），`captureOidcAccessTokenFromOverviewProbe`、`buildOidcCenterApiHeaders`、`expectOidcApiGetStatus`、`expectOidcApiPostStatus`、`createE2eHostPingJobDefinition` 与 `revokeCurrentOidcCenterSession` 统一探针 token 捕获、API 请求头、GET/POST 状态断言、任务夹具与强撤流程（治理禁止 spec 内联 `/api/v1/me` 拦截）；退出与强撤 API 负探针、受保护路由（Agent 工具／Agent 运行／工作流待办／任务定义）与凭据清理对称覆盖（见验证记录 §13 对称性对照表）；`expectOidcCenterLocalCredentialsCleared` 与 `expectOidcCenterTokensRejected` 统一 token 拒绝断言。

## 5. 验证入口与运行位置

### 2026-09-18 接手复核：已修复项与下一项阻断

Cursor 最近一轮实现复核发现并已修复三处协议边界问题：

- OIDC 中心登录输错密码原先没有推进 `FailedLoginCount`，现已复用 Identity 锁定阈值写入失败记录；`/connect/authorize` 与中心登录 API 均接入 `identity-login` 限流。
- OIDC 中心登录成功后现会清除连续失败计数，保持与旧登录入口相同的锁定语义。
- 切租户专用 Refresh Token 由自定义 OpenIddict dispatcher 签发时原先缺少创建／过期元数据，现已显式写入有限生命周期，避免产生无过期刷新令牌。

多标签 P1 已补上代码与单元回归：Vue 在刷新协调锁内刷新 access，再发起单次中心退出；服务端以经过验证的 OIDC bearer 用户确定撤销目标，中心 Cookie 属于另一用户时保留该 Cookie。无效 Authorization 不回落 Cookie，应用退出额外校验令牌 client 与请求 client 一致。无 Authorization 的既有 Cookie 入口保留兼容行为。下一步仍需 SQL Server/MySQL、真实 A/B 多标签与过期 access 浏览器验收；尚不提升生产 Verified。网络或刷新失败时仅保证本地清理，不宣称服务端已退出。

增量验证：`identity-oidc` 单元选择器 51 项通过（其中退出回归 4 项）；Vue 退出／中心登录 8 项通过，共享 OIDC 客户端 10 项通过。Vue 全量 typecheck 未通过，仍有既有 OIDC 用例／页面类型错误及工作区企业申请、租户订阅契约和翻译错误；完整构建门禁保持未通过。

本轮聚焦验证：`pnpm test:dotnet:unit -- --filter "FullyQualifiedName~IdentityOidcCenterLoginServiceTests|FullyQualifiedName~IdentitySessionContextServiceTests" --minimum-expected-tests 2`，7 项通过；Identity 模块构建 0 警告、0 错误。完整双库、真实浏览器、多实例和 CI 门禁仍按本计划执行。

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

### 2026-09-18 后续复核：类型与并发登录

- 已修正 OIDC 回调查询数组的 nullable 类型、Element Plus 提示 mock 返回契约、授权列表列泛型、强撤／切租户测试的 Bearer 字面量类型；全量 typecheck 输出已无 OIDC 文件错误，仍因企业申请契约及租户订阅翻译错误失败。
- 切租户冲突旧测试与文档错误要求保留旧 Host 令牌；现按已有共享会话层行为验证刷新后重试一次、更新 token／refresh 并重载权限，没有放宽失败关闭逻辑。
- 中心登录记录失败／成功时增加最多 32 次乐观重试，冲突后重读启用／锁定状态并重新验密；只有成功更新 1 行才能创建登录身份，持续争用拒绝登录。密码哈希需升级时同步更新。
- 登录回归扩至 7 项，覆盖错误密码锁定、失败和成功写入冲突、并发停用／锁定／改密、持续冲突。重试修复前 3 项中 2 项失败；修复后 `pnpm test:dotnet:unit -- --selection identity-oidc` 为 57 通过、0 失败、0 跳过，构建 0 警告／错误。
- Vue 聚焦命令覆盖 `oidc-center-login`、`session-oidc-center`、`session-oidc-center-revoke`、`session-oidc-center-switch-tenant`、`OidcCallbackView`，5 文件共 15 项通过。完整 typecheck 仍未通过，不升级生产 Verified；双库、真实浏览器、多实例与原生发布运行继续待 CI。
- 本轮 pnpm test:aot:analyzers 未通过：Webhooks 反射 JSON 序列化 2 项、Tenancy DbDataReader 日期读取 4 项、企业申请生成代码反射 1 项，共 7 个错误。此结果属于当前完整工作区门禁，不宣称 AOT 通过。中心登录并发修复经独立静态复核未发现明确阻断问题（该复核不替代测试）。

### 2026-09-18 后续复核：AOT 与客户端构建阻塞

- 修复 Webhook JSON 源生成、Tenancy 跨 Provider 日期读取及企业申请生成代码的反射参数复制；Webhook 保持原 PascalCase 负载及签名，生成器使用静态字典参数。`pnpm test:aot:analyzers` 通过（0 警告／错误），`pnpm test:dotnet:architecture -- --selection api-native-aot` 73 项通过；生成器与 Webhook 聚焦回归共 41 项通过。
- 统一后端、OpenAPI 与 Vue 生成器对下划线模块名的 operationId 转换；企业申请的 5 个真实端点加入客户端生成 manifest。补齐租户订阅相关中英文翻译，修复页面误把翻译函数第二参数作为默认文案的问题；admin-i18n 8 项通过。
- 真实导出揭示 SQL Server 222／223 迁移在同一批次引用新增列导致首次安装失败，改用 GO 分隔 DDL 与回填。修复后 SQL Server OpenAPI 集成测试 1 项通过、0 跳过；SQL 安全检查 5 项通过。
- 双库导出尚未通过：MySQL 容器就绪检查返回 Docker API 500，未进入数据库迁移；不能将 SQL Server 结果扩展为双库通过。新迁移的半完成恢复、真实浏览器、多实例和 Linux Native AOT 发布仍待 CI。
- 当前工作区命名检查失败，包含新增迁移注释缺失、索引名超长及未登记动态 SQL；不以新增豁免隐藏问题。影响集规划也因调用 git hash-object 失败而未完成。上述项不计为通过，不升级生产 Verified。
- SQL Server 单库导出重跑通过（1 项、0 跳过），已更新真实 OpenAPI 快照并重新生成客户端产物；离线快照检查与生成器 `--check` 均通过，产物零漂移。操作登记由 544 增至 549，同步对应归一化断言。
- 重生成暴露此前产物与当前契约的积累差异：管理端 typecheck 仍有 41 处错误，主要为请求字段可选性、枚举／数值表示、工作流新增业务标题测试夹具，以及企业申请表格只读数组／行类型。不得仅修改 generated 文件掩盖问题。
- `pnpm test:openapi` 初次为 160 通过、6 失败（166 项、0 跳过）；其中操作计数断言已更新，其他失败涉及 OIDC Schema 引用、未消费夹具和 7 个 Vue API 模块的覆盖登记。全套未重新通过。
- 下一步顺序：① 修正真实契约与调用方／测试夹具并清零 typecheck；② 补齐 OIDC 及新增模块契约覆盖，跑通 OpenAPI 门禁；③ 修复新增迁移命名／注释并补半完成恢复证据；④ 在 CI 验证 MySQL 与双库一致性、SSO 真实浏览器及原生运行。保持 Implemented／局部 Build-verified，不作完整验收结论。
- 客户端归一化契约聚焦复跑：node --test tests/openapi/client-openapi-normalization-contract.test.mjs，5 项全部通过；已同步新增操作数和生成分组。

### 2026-09-18 后续复核：管理端类型检查通过与 OIDC 契约收敛

- 完整 `pnpm --dir ui/admin typecheck` 已通过，上轮 41 处错误清零：请求适配显式补齐 nullable 字段，文件修订号按服务端 int 契约传递，在线会话策略按服务端数值枚举消费；工作流测试夹具补业务标题字段，企业申请表格显式处理只读数组与行类型。
- 修复批量上传真实契约：Endpoint 使用 IFormFileCollection 绑定并移除错误的单文件 Accepts 元数据。SQL Server 真实 OpenAPI 导出 1 项通过、0 跳过；生成客户端正确追加多个文件。新增上传请求回归验证 FormData 中两个独立 files 字段；文件与模块选择客户端 8 项通过，之前其余工作流相关 29 项通过。生成器 8 项通过，生成产物与离线快照检查均零漂移。
- OIDC 客户端／授权管理夹具补齐缺失 Schema，新增签名密钥管理夹具，并登记三个 Vue API 模块与页面。字段对照 C# 契约 3 项通过。批量上传兼容修正只接纳精确路径的已确认元数据变化，认证／响应／必填字段变更负例仍拒绝；兼容门禁 18 项通过。
- 本轮 `pnpm test:aot:analyzers` 通过，0 警告／错误。初次 SQL Server 导出因 Docker 未启动失败，启动后聚焦重跑通过；不代表 MySQL 或原生发布已验证。
- 最新 `pnpm test:openapi` 为 170 项中 167 通过、3 失败、0 跳过：企业申请样例夹具未消费、样例 API 调用层位置不合规，以及 public-auth／tenancy-entitlements／tenant-members／tenant-subscriptions 四个 Cursor 新增模块尚未完成共享契约及覆盖登记。下一步先完成这三组缺口，再回到迁移命名／注释、双库恢复和 SSO 真实栈验收。保持未完成整体验收状态。
- 2026-09-19：基础 API 新增响应形状回归 6 项通过；tenant-members、tenant-subscriptions、tenancy-entitlements 和 public-auth 仍需纳入正式共享契约与覆盖清单，暂不将其标记为完成。
- 2026-09-19：企业申请页面已改为 API 适配层；四个基础 API 和企业申请均登记共享覆盖清单与契约夹具。管理端 typecheck 通过；`pnpm test:openapi` 172/172 通过、0 失败、0 跳过；客户端生成 `--check` 与离线快照检查通过。MySQL 双库、真实浏览器、原生发布及迁移命名债务仍待 CI。
- 2026-09-19：OpenAPI 与客户端覆盖继续保持 172/172；新增基础 API 共享契约、企业申请适配层和运行时校验已完成，管理端 typecheck 通过。命名门禁当前 29/31 通过，剩余为存量 OIDC 迁移注释目录与新增迁移的精确索引名／动态 SQL 债务登记，未通过豁免隐藏。

### 2026-09-19 接管审查增量

- 修复 `generate-object-comments.mjs` 对 MySQL `IF NOT EXISTS`、`ENGINE` 建表语法的解析缺口，并新增回归测试，避免双库迁移表/列从注释目录漏登记。
- 为 Identity OIDC 协议状态与会话迁移补齐 SQL Server `MS_Description` 幂等注释；为新增双库迁移应用目录注释。
- 将账号挑战、注册邀请的超长索引名按确定性压缩规则统一到 SQL Server/MySQL；对仍需兼容旧 MySQL 的固定动态 DDL 登记精确 M1.0 债务。
- 新鲜门禁证据：`pnpm test:naming` 32/32、`pnpm test:sql-safety` 5/5、`pnpm test:openapi` 172/172、`pnpm --dir ui/admin typecheck` 通过；`git diff --check` 无错误（仅存在换行符提示）。
- 未完成的环境级验证仍是 MySQL/SQL Server 容器迁移恢复、真实浏览器 OIDC 全链路和 Native AOT 发布；本轮未提交或推送。
- 回归测试复核：迁移注释解析改为独立 SQL 夹具，覆盖 SQL Server 普通建表、MySQL IF NOT EXISTS + ENGINE、表 COMMENT + ENGINE 及生成列；临时恢复旧解析器时准确失败（漏掉两个 MySQL 表），恢复修复后 `pnpm test:naming` 32/32 通过。
- AOT 日志复核：`.fullnet/oidc-20260919-aot-final.log` 包含完整构建成功摘要，0 警告、0 错误，随后恢复默认 JIT 依赖图；仅为分析器构建证据，不代表 Linux 原生发布通过。客户端生成零漂移、离线快照与生成就绪 7/7 的上一轮证据已核对。
- 环境验收执行位置：遵循 R-20260903-github-actions-first-verification，双库集成/迁移恢复及 OIDC 真实浏览器交由 `.github/workflows/ci.yml`，原生发布/运行交由 `.github/workflows/api-native-aot-linux.yml`；当前没有新的远端运行证据。Docker 本地 Linux Engine 管道不可用，不启动本地重型全套，也不将这些项升级为 Verified。

### 2026-09-19 SSO 验收探针复核

- 修正 `identity-oidc-sso.spec.mjs` 的 max_age=0 用例：真实发送 max_age=0，不再用 prompt=login 替代，并完成重新认证与换票。
- 将授权码领取与兑换分离；错误 PKCE verifier 用例现在使用未消费授权码，要求 HTTP 400 + invalid_grant，避免授权码重放或服务端 500 造成假阳性。
- 新增辅助函数行为回归，验证未提前兑换、完整登录流只兑换一次、机密客户端凭据保留及服务故障不能充当协议拒绝。新增领取测试先失败，修复后通过；已加入 test:provisioner。
- 修复 oidc-governance.spec.mjs 的 UTF-16 编码导致 Playwright 全套发现失败；只转换为 UTF-8，保留原用例内容。同步既有 admin-oidc-center 探针的实际数量登记。
- 本轮证据：pnpm test:e2e:provisioner 27/27，通过且无跳过；在 tests/e2e/admin-real-stack 执行 pnpm exec playwright test --list，发现 59 个文件、286 个用例，退出码 0。发现不执行浏览器与数据库，不代表这些 E2E 已通过；真实双库运行继续待 CI。

### 2026-09-19 SSO state 探针复核

- 修复浏览器 state 篡改用例：保留真实 callback code，仅修改 state，并清除旧授权码后断言拒绝时未写入新授权码。此前跳转缺少 code，只会进入 ready 分支。
- 请求辅助函数在复用中心 Cookie 的直接 302/303 路径也验证 state，缺失或篡改均拒绝；两条新增回归修复前失败、修复后通过。
- 通过 Node VM 执行 A/B RP 实际夹具回调，覆盖匹配、篡改、缺失回调 state 和没有发起授权的情况。发现并修复夹具将两侧空 state 视为匹配的问题，两条负例修复前失败。
- 本轮 pnpm test:e2e:provisioner 37/37 通过，无失败或跳过；Playwright --list 发现 286 项/59 文件，退出码 0；git diff --check 通过。VM 验证只覆盖脚本回调逻辑，用例发现不等同于浏览器、Cookie 或双库协议运行，真实栈仍待 CI。

### 2026-09-19 全量代码提交检查

- 提交前发现 8 个新增文件为 UTF-16，已按文件字节证据转换为 UTF-8：两份 Vue 注册/找回密码页面、一份基线文档及五份迁移脚本。另清理新增文件尾部空白。
- 修正编码后 `pnpm --dir ui/admin typecheck` 通过，`pnpm test:sql-safety` 5/5 通过；`pnpm test:naming` 为 29/32，通过前记录已不代表当前完整工作区。新增失败为迁移注释目录/脚本注释缺失、228 双库超长索引名和 MySQL 223 固定动态 DDL 未登记。此次提交保存当前代码，不代表这些门禁已修复或 CI 验收完成。
- 全量正式源码、契约、模板、样例、测试与文档纳入提交；根目录临时日志、CI 下载产物及生成预览副本保留本地，不提交、不推送。

### 2026-09-19 后续阻塞修复（本地验证，远端待跑）

- 在 `4ad8f039` 基线上新鲜复现命名 30/32，补齐注释与确定性索引名、审查固定动态 DDL 后恢复 32/32；前节失败保留为提交时历史证据。
- 核对 `a6d3d02e` 的 CI 与 API Native 失败：关闭 OIDC 时旧切租户与在线会话服务强依赖未注册的协议签发/撤销组件。现保留协议组件条件注册，将不依赖密钥的历史授权撤销服务移至公共装配；切租户仅在 OIDC 分支要求协议依赖齐备，缺失时零数据库访问并拒绝。
- 新增关闭装配两项回归先准确失败后通过，补启用但缺依赖的失败关闭回归；既有切租户、会话撤销与模块注册测试一并验证。组合安全修复聚焦 Unit 34/34，AOT Architecture 73/73，API AOT analyzers 0 警告/0 错误；provisioner 37/37。完整结果与下一阻塞见[总计划首批收口](2026-09-16-foundation-productization.md#2026-09-19-首批阻塞收口)。
- 当前提交尚无远端运行证据；这些本地结果不能关闭 V01—V24、双库恢复、真实浏览器或 Linux Native 发布运行门禁。
