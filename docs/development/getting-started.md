# Full.NET 本地开发与运行指南

新加入的开发者请先阅读[人类阅读入口（Onboarding）](onboarding.md)，再按本文搭建环境。当前能力和未完成边界以[能力状态矩阵](../roadmap/capability-status.md)为准；AI 开发代理还必须遵守根目录 [`AGENTS.md`](../../AGENTS.md) 和 [`rules/`](../../rules/README.md)。

项目验收以规定测试在本地实际通过为准；容器、kind、浏览器和 Linux 原生运行均可在本地完成，CI 和专用生产等价环境不再强制。范围、证据和状态遵守 [开发质量 §11](../../rules/development-quality.md#11-测试与验证)。

## 1. 前置环境

- .NET 10 SDK；
- Node.js 24 和 pnpm 10.26.0，建议通过 Corepack 管理；
- Git；
- Docker Desktop，或兼容 Docker Engine。Windows 应启用 WSL 2，并使用 Linux containers。

先确认 Docker Client、Server 和容器运行均正常：

```powershell
docker version
docker run --rm hello-world
```

不要把真实口令、连接串、Token、证书或私钥提交到 `appsettings*.json`。本地使用环境变量或 .NET User Secrets；部署环境使用平台 Secret 管理能力。

## 2. 还原、构建与后端测试

```powershell
dotnet restore Full.NET.slnx
dotnet build Full.NET.slnx --configuration Release
pnpm test:dotnet:unit -- --no-build
pnpm test:dotnet:compatibility -- --no-build
pnpm test:dotnet:architecture -- --no-build
```

测试套件、最低发现数、超时与 Integration 分片只维护在 [`eng/testing/test-matrix.json`](../../eng/testing/test-matrix.json)。本文不复制测试数量，避免门槛随代码增长后漂移。

### 2.1 受影响 Integration 测试

工作区干净、单窗口开发时，先记录任务基线：

```powershell
$taskBase = git rev-parse HEAD
pnpm test:inner -- --base $taskBase --plan
pnpm test:slice -- --base $taskBase
```

工作区已经有其他改动或任务跨多个窗口时，使用任务快照：

```powershell
pnpm test:task:start -- <task-id>
pnpm test:inner -- --snapshot <task-id> --plan
pnpm test:slice -- --snapshot <task-id>
pnpm test:integration:affected -- --snapshot <task-id> --phase merge
```

常用专项入口：

```powershell
pnpm test:integration:smoke
pnpm test:integration:api:sqlserver
pnpm test:integration:api:mysql
pnpm test:integration:migrations
pnpm test:integration:infrastructure
pnpm test:integration:partitions
pnpm test:integration:durations
```

Integration 依赖按首次使用启动。单 Provider 聚焦测试不会无条件启动另一数据库和 Redis；数据库行为变更必须让受影响选择器命中 SQL Server 与 MySQL。完整 Integration 可在本地按矩阵分批验收，CI 为可选回归；普通修改仍优先受影响验证，避免无关全量重复运行。

测试结束后等待数据库容器和 Ryuk 自然退出，并检查没有遗留的 SQL Server、MySQL、Testcontainers 或任务 Runner。环境缺失、发现数为零、命令未执行都不能表述为测试通过。

## 3. 客户端工作区

首次还原客户端依赖：

```powershell
corepack enable
pnpm install --frozen-lockfile
```

标准客户端门禁：

```powershell
pnpm test:naming
pnpm test:sql-safety
pnpm test:openapi
pnpm test:openapi:breaking -- --base-ref main
pnpm test:workspace
pnpm test:clients
pnpm test:performance-governance
pnpm build:clients
pnpm test:bundle-budgets
pnpm test:e2e
pnpm test:e2e:uniapp
```

### 3.1 Vue 管理端

`ui/admin` 是后台产品的唯一持续交付线。`ui/admin-layui` 已冻结，不再新增页面、按钮、适配器或功能对等实现；只有明确授权的安全修复、迁移或退役任务可以修改。

会话、通知及概览的启动操作可从 `@fullnet/client-contracts/startup` 导入，保留共享 HTTP、取消、错误及响应守卫语义；普通业务页面继续使用完整入口。两个入口均由同一 OpenAPI 快照生成，禁止手工修改 `startup-*.generated.ts`。涉及首屏依赖时，使用 `pnpm test:e2e:startup-production` 构建生产产物并验证登录页资源、会话恢复、导航及设置面板按需打开；该入口只执行直接场景，不替代真实后端或完整页面验收。

账号自助入口为 `/#/register` 和 `/#/recover-password`，实际可用性由服务端注册策略及邮件渠道决定。先获取邮箱验证码，再填写最新验证码提交；发送中禁止重复请求，修改邮箱会清除旧挑战，重发失败或结果未知时须重新获取。找回密码的统一提示不确认账号是否存在。较早投递的失败不会撤销后续重发的新码，验证码仍只能消费一次；页面与公开请求语言跟随后台当前语言。双库竞争和受控 HTTP 浏览器已定向验收，不代表真实邮件厂商已认证。

Host 的注册方式管理页可选择禁用、仅邀请或开放注册，需有策略读取/更新权限并显式保存。版本冲突或保存结果未知后先“重新读取注册策略”，确认服务端状态再修改，页面不会自动重放写请求。开放模式仍接受有效邀请，并以邀请确定注册目标；邀请参数不完整或绑定失败不会回落普通路径或提交半成品账号。普通开放注册须提供明确的 RegistrationWayId。

在注册方式列表中查看启用方式的“注册链接”，复制后提供给注册者；剪贴板不可用时可选中只读链接手动复制。链接形式为 `/#/register?registrationWayId=<方式 GUID>`，注册页保留路径编号提交，服务端重新校验方式及目标租户。缺失/非法入口会提示并阻止发送验证码；明确的方式链接不会复用同一标签页中先前放弃的邀请，直接重新打开邀请页面则仍可恢复已保存的邀请。

租户成员邀请可选择当前企业的启用注册方式，为尚无账号的受邀人生成注册邀请链接。新用户验证邮箱并注册后，使用新账号登录，在“租户上下文”显式接受邀请；已有账号使用页面提供的原账号登录入口，再确认邀请令牌。邀请凭据从 URL 剥离后仅保留在当前标签页，未绑定邀请不会因为资料邮箱相同而自动显示。已接受但仍待激活时可刷新或重试；只有成员激活后才允许进入目标企业，新账号不会获得平台租户目录或业务管理权限。方式的完整自定义角色/部门分配仍属后续差集。

普通统一账号退出企业后，原凭据不能继续访问该企业受权限保护的业务接口；仍可核对本人 Removed 状态并返回 Host。退出一家企业不禁用全局账号，在允许多会话的策略下也不撤销另一家企业的独立会话。平台持久化的跨企业管理授权保留其既有范围；普通账号默认自助切换资格不会产生这项授权。后台冻结绑定和实时连接重复核证也读取当前成员准入；具体任务中止、副作用和连接关闭继续由各消费者执行，不以 HTTP 结果代替其实际旅程验证。

本地变量见 [`ui/admin/.env.example`](../../ui/admin/.env.example)（复制为 `ui/admin/.env.local`）。直接连接本地 API 时：

```powershell
$env:VITE_API_BASE_URL = "http://localhost:5149"
pnpm --filter @fullnet/admin dev
```

启用身份中心 OIDC 登录（T08，可选；默认 `legacy` 用户名密码）时，Vue 使用同源 BFF 机密客户端，由服务端完成回调与令牌保管。后台不再注册 SPA 公开客户端；API 与首次初始化的 Migrator 必须使用同一组 BFF 配置。

OIDC 客户端以数据库为唯一权威源。`Identity:Oidc:Clients` 仅由 Migrator 的 Baseline 初始化尚不存在的客户端；API 启动不注册或覆盖客户端。首次部署须向 Migrator 提供同一组显式 OIDC 初始化配置并执行受控 seed；后续策略、回调、停用与 secret 轮换通过管理 API 保存。API 可以不配置初始化客户端列表。需要离线续期的客户端必须显式允许 `offline_access`，只配置 `openid` 不会自动获得 `profile` 或 refresh grant。

非 Development/Testing 环境启用中心时，Issuer 必须是 HTTPS，必须配置持久化签名材料及 32 字节协议加密密钥；缺失时启动失败。多实例共享必要的协议加密与 Data Protection 材料；签名、加密与 Cookie 密钥职责分开，秘密只由受控 Secret 注入。中心 Cookie 在生产强制 Secure；开发态 HTTP 例外不进入生产。

签名轮换按以下顺序执行；`ActiveSigningKeyId` 用于 Migrator 首次初始化和节点启动材料检查，不能靠修改它覆盖已持久化的数据库活动指针。清理旧材料时，该配置仍须指向本节点具备私钥的已活动 `kid`。

1. 向所有 API 和签发退出通知的 Worker 部署候选 RSA 材料，保留旧公钥；同一 `kid` 永远绑定同一公钥指纹。仅材料编码不同不构成新密钥，禁止复用退役 `kid`。
2. 在签名密钥管理页发布候选公钥，核对各节点 JWKS，等待 `Identity:Oidc:SigningPrepublicationSeconds`（默认 300 秒，生产至少 60 秒）。实际窗口须匹配接入 RP 的 JWKS 缓存/未知 kid 刷新策略，默认值不是任意外部 RP 的无中断保证。`/health/ready` 登记 API 的 90 秒短租约，Worker 在投递循环登记；`/health/live` 不依赖数据库。Helm 将 `MinimumReadyApiNodes` 绑定最少 API 副本数，自行部署时也必须显式匹配。
3. 只有存活节点的公开材料、协议加密指纹、令牌期限配置和策略版本一致，且每个节点具备候选私钥、API 数量达标，管理页才允许激活。治理 API 在事务内重新检查并 CAS 更新活动策略；失联节点的旧租约在到期前保守阻止轮换。未登记或已失联节点必须经 readiness 再接流量，不能绕过探针直接加入服务。
4. 旧钥进入保留态。退役截止取配置完整令牌窗口和数据库现存合法令牌/中心会话最长到期时间的保守上界；到期后由有权限的操作员退役，不自动删除 Secret。探针同步后各节点 JWKS 移除旧钥；资源 API 直接查询权威退役状态，即使对端尚未同步、仍缓存旧公钥也拒绝旧 `kid`。核对各节点就绪与 JWKS 后，再将启动检查配置指向当前活动 `kid`、移除已退役材料并滚动重启；数据库活动指针保持权威。活动钥不能退役，退役不能撤销或重新发布；外部 RP 自身的缓存和离线 JWT 策略不会由本地退役操作自动改变。

节点声明仅保存公开材料摘要、密钥标识和租约，不存储私钥。签发时缺少活动私钥或权威状态不可用即失败关闭；退役状态校验故障也不放行 Bearer。首次就绪后，已知 JWKS 可在数据库短暂不可用时继续提供。聚焦验证与尚未证明的生产范围见[唯一活动计划](../superpowers/plans/2026-09-13-identity-oidc-sso-evolution.md)。

OIDC 浏览器定向验收使用 `pnpm test:e2e:real:oidc-center`（或 `node scripts/testing/run-identity-oidc-runtime.mjs`）：夹具自动准备双库、临时 HTTPS Vue 代理、双 Kestrel API、独立 HTTPS RP 和桌面/移动浏览器。Windows 需安装 Edge；其他平台需已安装 Playwright Chromium。Linux Native AOT 对应 `node scripts/testing/run-identity-oidc-runtime.mjs --native`，要求 Docker 和既定 `fullnet-native-aot-publish-sdk:10.0.402` 镜像，真实发布并运行 ELF，不以 Windows 跳过代替。Native 构建申请 12 GiB，资源门禁另预留 4 GiB，Linux guest 可用内存需至少 10 GiB；实际能否启动仍由门禁判断。源码只读挂载，运行目录位于系统 TEMP；成功自动清理，失败保留诊断记录，结束后清理可再生二进制。测试证书只用于本轮进程信任，不安装到机器根证书库。

首次缺少发布 SDK 镜像时，使用仓库的 [Dockerfile](../../eng/docker/Dockerfile.api-native-aot-linux-sdk) 准备：`docker build -t fullnet-native-aot-publish-sdk:10.0.402 -f eng/docker/Dockerfile.api-native-aot-linux-sdk eng/docker`。这一步只准备 SDK 与原生链接工具，不运行业务验收。

历史 HTTP `vue-admin-oidc-center` SPA 项目及其令牌读取夹具已退出活动 Playwright 配置；旧业务探针文件保留作场景追踪，不再是 BFF 验收入口，也不计入新入口的通过数量。普通 legacy 管理端与独立协议 RP 夹具仍由原真实栈入口维护。改造结论只覆盖本轮定向场景，不能据此声称历史全部业务探针已在 BFF 下重跑。

标准 RP 退出地址由 Discovery 的 `end_session_endpoint` 取得（`/connect/logout`），支持 GET 和表单 POST。端点先展示确认页，用户确认且防伪校验通过后撤销当前浏览器中心会话及关联应用会话，并清除中心 Cookie；其他设备保持登录。`post_logout_redirect_uri` 必须精确注册，`state` 由协议组件返回；无效 `id_token_hint` 拒绝请求。配置 `BackchannelLogoutUri` 的客户端由 Worker 可靠投递标准退出通知；部署方须先在 `Identity:Oidc:BackchannelAllowedOrigins` 精确许可 HTTPS 来源，后台客户端页可查询失败及重试。中心和 BFF Cookie 始终为 Secure，浏览器登录/退出必须使用 HTTPS。

```powershell
$env:VITE_IDENTITY_AUTH_MODE = "oidc-center"
$env:VITE_IDENTITY_OIDC_CLIENT_ID = "fullnet-admin-bff"
$env:VITE_HTTPS_CERT = "<受信任开发证书 PEM 路径>"
$env:VITE_HTTPS_KEY = "<开发证书私钥路径>"
# BFF 不配置跨源 VITE_API_BASE_URL，后端代理目标由 VITE_API_PROXY_TARGET 指定。
Remove-Item Env:VITE_API_BASE_URL -ErrorAction SilentlyContinue
# 下列配置也须提供给 API 和 Migrator；秘密只由受控 Secret 注入。
$env:Identity__Browser__Enable = "true"
$env:Identity__Browser__PublicOrigin = "https://localhost:5173"
$env:Identity__Browser__ClientId = "fullnet-admin-bff"
$env:Identity__Browser__ClientSecret = "<由 Secret 注入>"
$env:Identity__Browser__StateSigningKeyBase64 = "<独立持久化 32 字节 Base64 密钥>"
$env:Identity__Browser__StateEncryptionKeyBase64 = "<另一独立持久化 32 字节 Base64 密钥>"
pnpm --filter @fullnet/admin dev
```

`PublicOrigin` 必须精确等于浏览器来源，回调固定为该来源的 `/api/v1/identity/browser/callback`。浏览器状态密钥不得复用协议加密密钥，多节点共享必要的状态密钥和 Data Protection 材料。API 经可信代理接收 HTTPS 来源，BFF 自身须能访问同源 Discovery/JWKS 和令牌端点。生产反向代理的同源入口可替代 Vite 证书配置。

启用 BFF 时旧密码登录/刷新与 legacy OAuth 回调不映射，JWT issuer 信任同步收敛，不能只改 Vite 变量作为服务端回退。切换前后端模式后已撤销会话仍保持撤销，不恢复旧浏览器令牌存储。

仅运行原本未启用 BFF 的 `legacy` 开发环境时，移除 Vue OIDC 模式变量后重启 Vite：

```powershell
Remove-Item Env:VITE_IDENTITY_AUTH_MODE -ErrorAction SilentlyContinue
Remove-Item Env:VITE_IDENTITY_OIDC_CLIENT_ID -ErrorAction SilentlyContinue
pnpm --filter @fullnet/admin dev
```

验证：登录页展示账号/密码表单且无「前往身份中心登录」；浏览器 `sessionStorage` 不含 `fullnet.admin.oidc.refresh`；已登录会话应通过 legacy 刷新 Cookie 或重新登录恢复。自动化探针见 `vue-admin` 项目 `auth-smoke.spec.mjs`（遗留 OIDC 凭据不阻断登录、刷新后 Cookie 恢复）。

涉及 Refresh Cookie 时，浏览器与 API 应使用同一个 `localhost` 站点语义，不要混用 `localhost` 和 `127.0.0.1`，否则 `SameSite=Strict` Cookie 可能被浏览器按跨站请求拒绝。

Vue 使用 `/api/v1`、标准 HTTP 状态码和 ProblemDetails，并从 `@fullnet/client-contracts` 消费共享契约。`legacy` 模式仅在内存持有 access token，通过 HttpOnly Refresh Cookie 恢复会话；`oidc-center` 使用同源 BFF，浏览器不接收或持久化 OAuth 令牌，通过安全会话 Cookie 恢复，CSRF 值只留内存。权限快照只保存在内存，禁止把令牌或权限快照写入 `localStorage`/`sessionStorage`。

无权限时 Vue 不创建对应页面或操作按钮，但前端隐藏只改善体验。所有受保护 Endpoint 仍必须使用同一稳定权限码重新授权，绕过前端直接调用必须返回 403。

### 3.2 真实后端浏览器测试

`pnpm test:e2e:real` **不是** inner 门禁，只用于功能 `Verified` 关闭，或修复真实 CORS、Cookie、CSRF、Session 与跨 Origin 凭据问题。日常代码迭代使用 `pnpm test:inner`。

真实栈 E2E 需要 Docker、.NET 10 SDK 和可用的本地端口：

```powershell
pnpm test:e2e:real
```

仅验证 Vue `oidc-center` 真实栈场景时：

```powershell
pnpm test:e2e:real:oidc-center
```

普通真实栈会启动数据库、Migrator、真实 API 和 Vue；默认 `vue-admin` 项目（端口 25173）覆盖 legacy 登录与回退探针。OIDC 专用入口独立运行双库、HTTPS BFF 与第二 RP，覆盖桌面/移动登录、回调跨 API、恢复、CSRF、租户切换、密钥发布/激活/详情和应用/中心退出；每库要求两个 Playwright 项目均实际通过，不允许跳过。真实栈禁止用 `page.route` Mock 替代后端行为。历史 `vue-admin-oidc-center` HTTP SPA 项目已退出活动配置，原 `admin-oidc-center.spec.mjs` 的 129 项业务探针保留为追踪清单，不能当作已在 BFF 下重验。

独立成熟 RP 与真实 Worker 网络联调使用单独入口；在仓库根安装 pnpm 依赖并完成 .NET restore 后执行：

```powershell
pnpm test:identity:oidc:interop
# 追加所开放 profile 的 OIDF 官方模块，并明确保存去敏度量。
pnpm test:identity:oidc:interop --conformance --evidence docs/verification/2026-10-27-identity-oidc-interop-results.json
```

该入口需要 Docker Desktop Linux、.NET 10 SDK、Node 24、SQL Server/MySQL 测试镜像、`redis:8.6` 和已准备的 `fullnet-native-aot-publish-sdk:10.0.402`。官方模块额外需要 `mongo:6.0.13` 与研究报告 §16.7 所锁定的套件镜像，Docker 内须能通过 `host.docker.internal` 访问宿主临时 HTTPS 端口。证书仅信任于本轮 Node/JVM/.NET 客户端，私钥和客户端 secret 从系统 TEMP 文件加载，结束后清理；无需修改机器根证书或提供外部部署密钥。真实 Worker 使用本轮数据库和独立 Redis，Outbox 轮询 100 ms、最大空闲轮询 1000 ms；传播预算只用于此本地回归。

issuer 含 `/identity` 时，默认 Discovery 为 `/identity/.well-known/openid-configuration`。当前应用退出 API 要求有效 Bearer，且 client 必须匹配该令牌，只撤销其应用会话；BFF 页面使用 Cookie/CSRF 退出入口。网络入口不进入普通 infrastructure/full 分片；所选官方模块通过不代表完整 profile 认证。结果与容量边界见[联调报告](../verification/2026-09-13-identity-oidc-sso-research-validation.md#167-2026-10-27-独立-rp真实网络与官方套件补验)。

网络夹具在自有 API/RP/Worker 停止后，按其刚创建的完整随机库名清理本轮数据库及对应 MySQL 授权；不按前缀批量删除共享实例中的库，不结束其他任务的数据库进程。清理失败会使该轮失败并留下最小去敏定位记录。

已有独立栈时可以跳过自动引导：

```powershell
$env:FULLNET_E2E_SKIP_BOOTSTRAP = "1"
$env:FULLNET_E2E_API_URL = "http://localhost:5149"
pnpm test:e2e:real
```

### 3.3 uni-app

uni-app 基础验证和三目标构建：

```powershell
pnpm --filter @fullnet/uniapp dev:h5
pnpm --filter @fullnet/uniapp test
pnpm --filter @fullnet/uniapp typecheck
pnpm --filter @fullnet/uniapp build:h5
pnpm --filter @fullnet/uniapp build:mp-weixin
pnpm --filter @fullnet/uniapp build:mp-alipay
pnpm test:e2e:uniapp
```

业务、API 和账号资料使用规范 BCP 47 标签 `zh-CN`、`en-US`。微信与支付宝构建完成不代表对应平台已验证；仍须在各自开发者工具中执行启动、语言切换、重启保持、真实登录、API Header、错误、会话失效和导航标题检查。缺少工具时只能记录 `Not executed — required tool not installed`。

## 4. 使用 Aspire 启动完整开发环境

可将开发 Bootstrap 账号写入 AppHost 的 User Secrets：

```powershell
dotnet user-secrets set "Parameters:identity-bootstrap-username" "admin" --project src/Hosts/Full.NET.AppHost/Full.NET.AppHost.csproj
dotnet user-secrets set "Parameters:identity-bootstrap-password" "<至少12位且含大小写、数字和特殊字符>" --project src/Hosts/Full.NET.AppHost/Full.NET.AppHost.csproj
dotnet run --project src/Hosts/Full.NET.AppHost/Full.NET.AppHost.csproj
```

也可以在 Aspire Dashboard 的未解析参数提示中输入并保存到 User Secrets。密码是 Secret Parameter，不应出现在命令日志、普通配置或提交历史中。

正常启动顺序：

1. 数据库和 Redis 就绪；
2. Migrator 执行 DbUp 迁移和 Development Seed，然后以代码 0 退出；
3. API 和 Worker 在 Migrator 成功后启动；
4. API 的健康端点反映实际运行状态。

AppHost 使用 Development Seed，幂等创建标识为 `local`、域名为 `localhost` 的开发租户和首个宿主管理员；账号已经存在时不会覆盖密码。

健康端点语义：

- `/health/live`：只证明进程能够响应，不检查数据库、Redis 或 Schema；
- `/health/ready`：检查当前数据库和已配置的 Redis/Backplane 等必要依赖；
- `/health/startup`：验证当前数据库已完成必须的 Schema Contract 和初始化阶段。

`ready` 或 `startup` 缺少真实检查时不得返回供编排器使用的假成功。

AppHost 默认使用 SQL Server。只对当前终端切换为 MySQL：

```powershell
$env:UseMySql = "true"
dotnet run --project src/Hosts/Full.NET.AppHost/Full.NET.AppHost.csproj
```

也可以在 `src/Hosts/Full.NET.AppHost/appsettings.json` 中设置：

```json
{
  "UseMySql": true
}
```

本地 AppHost 可以让 Cache 与 Realtime 共用 Redis；成熟生产参考拓扑必须按部署规则使用独立 Cache/Backplane Redis 与 Realtime Redis，例外需要容量和故障域证据。

## 5. 迁移、Seed 与部署顺序

生产部署顺序固定为：

1. 运行 `Full.NET.Host.Migrator`；
2. 确认迁移、必要 Baseline Seed 和 Schema Contract 成功；
3. 启动或滚动发布 API、Worker；
4. 发布 Vue 静态资源。

API 和 Worker 不在启动时执行生产迁移，也不运行 Seed。基础配置示例：

```text
Database__Provider=SqlServer        # 或 MySql
Database__ConnectionName=fullnet
Database__MySqlGuidStorageMode=Binary16
ConnectionStrings__fullnet=<由 Secret 管理器注入>
```

Migrator 默认只迁移。需要播种时显式使用：

```powershell
dotnet run --project src/Hosts/Full.NET.Host.Migrator/Full.NET.Host.Migrator.csproj -- --seed baseline
```

支持的 Profile 为 `baseline|development|demo|test`。Production 只允许 Baseline；Development、Demo、Test 作为环境 Overlay 确定性继承 Baseline。生产 Bootstrap Secret 和失败处理见[生产 Seed 运维说明](../operations/seed-production-baseline.md)。

MySQL 正式模式是 RFC 9562 大端字节序 `BINARY(16)`。业务模块和 Dapper 调用只处理 C# `Guid`，不得自行交换 UUID 字节。008/009 迁移已经完成代码与双库恢复验证；生产维护窗口、备份恢复和 RPO/RTO 演练仍按 [UUID Binary16 迁移 Runbook](uuid-binary-migration-runbook.md)执行。

## 6. Identity、可信代理与会话

浏览器会话的主要端点：

| 方法 | 路径 | 说明 |
|---|---|---|
| `POST` | `/api/v1/auth/login` | 校验精确 Origin、限流和账号状态，返回短期 Access Token |
| `POST` | `/api/v1/auth/refresh` | 校验 Refresh Cookie 与 `X-CSRF-Token`，原子轮换会话 |
| `POST` | `/api/v1/auth/logout` | 撤销会话并清除 Cookie |
| `GET` | `/api/v1/me` | 返回当前账号、有效作用域、语言偏好和资料版本 |
| `PUT` | `/api/v1/me/locale` | 以乐观并发更新账号语言偏好 |
| `GET` | `/api/v1/navigation` | 返回当前有效作用域的授权导航树 |
| `GET` | `/api/v1/tenancy/available` | 返回当前账号可进入的活动租户 |
| `PUT` | `/api/v1/tenancy/context` | 进入租户或返回 Host，并签发新 Access Token |

Refresh Token 只存在于 `Secure`、`HttpOnly`、`SameSite=Strict` Cookie，数据库只保存哈希。前端可读 CSRF Cookie 仅用于双提交校验。调用方不得信任客户端自定义租户 Header、权限快照或未签名 Claim。

Production 必须从 Secret 管理器提供 RSA 密钥和允许的管理端 Origin，例如：

```text
Identity__ActiveKeyId=<当前密钥标识>
Identity__SigningKeys__<当前密钥标识>__PublicKeyPem=<RSA 公钥 PEM>
Identity__SigningKeys__<当前密钥标识>__PrivateKeyPem=<RSA 私钥 PEM>
Identity__AllowedOrigins__0=https://admin.example.com
Tenancy__HostDomains__0=api.example.com
```

`Tenancy:HostDomains` 只接受精确主机名或 IP，不接受协议、端口、路径、通配符或全网范围。非法配置在宿主启动时失败。

API 默认不信任转发 Header。只有实际部署在可信代理之后时才启用 `TrustedProxy`，并登记最小代理 IP/CIDR 和精确 `ForwardLimit`；禁止 `0.0.0.0/0`、`::/0` 或公网客户端网段。业务模块统一读取规范化后的 `Connection.RemoteIpAddress`，不得自行解析 `X-Forwarded-For`。

## 7. 缓存与实时通信

FusionCache 是唯一缓存实现，通过 `.AsHybridCache()` 同时暴露 FusionCache 与 Microsoft `HybridCache` 抽象。业务条目必须通过统一策略注册表获得 C0/S0-L2/S1/S2/N0 分类和选项，禁止在模块中随意手写 TTL。

缓存失效不使用 Outbox。事务提交后当前实例直接清理 L1/L2，Redis Backplane 快速通知其他实例，TTL、版本和权威源读取负责最终收敛。强一致类别禁用 L1 或直接读取权威源；Redis Pub/Sub 不是可靠事务总线。

未配置 Redis 时可以作为单实例开发模式运行。多实例部署必须配置共享 L2 与 Backplane，并让 `/health/ready` 反映 Redis 可用性。缓存键与 Tag 统一通过 `CacheKeyBuilder` 构造，不在日志或指标中附加完整缓存键、租户名、域名或异常文本。

业务模块通过 `IRealtimePublisher` 发布实时通知，不直接依赖 SignalR Hub。多 API 节点可以设置 `Realtime:RedisBackplaneConnectionString`；配置后 `/health/ready` 增加 Realtime Backplane 检查。实时推送是尽力下行，客户端必须刷新权威 HTTP 状态，可靠业务传播仍使用事务 Outbox。故障演练见 [Realtime Redis Backplane 运维说明](../operations/realtime-redis-backplane.md)。

## 8. HTTP、JSON 与兼容层

默认 API 使用真实 HTTP 状态码。成功直接返回强类型 DTO；失败返回 RFC ProblemDetails，并携带稳定 `code`、`traceId` 和可选结构化校验集合。调用方依据机器码处理逻辑，不比较中文或英文错误文本。

HTTP JSON 使用 System.Text.Json。每个模块维护自己的 `JsonSerializerContext`，并由模块注册入口加入 Host 的 `TypeInfoResolverChain`。生产 Endpoint 的请求、响应、分页项和 ProblemDetails 类型受 Architecture 门禁覆盖；不要在请求热路径创建新的 `JsonSerializerOptions`，也不要把 Newtonsoft.Json 作为核心 API 默认实现。

需要兼容旧 Admin.NET 响应形状时，在自定义 Host 中显式启用：

```csharp
builder.AddFullNetServiceDefaults();
builder.Services.AddAdminNetCompatibility();
```

兼容层只改变普通 JSON 响应形状，仍保留真实 HTTP 状态码；文件、流、SSE、SignalR、健康检查和 `204 No Content` 不进入统一包络。

## 9. 事务、跨模块通信与 Outbox

模块内查询可以对本模块表使用参数化 `JOIN/LEFT JOIN`、批量查询和多结果集；模块内写入由一个 `ICommandTransaction` 原子维护本模块业务表、必要审计和 Outbox。

后台列表、搜索、报表及同语义导出允许受控跨模块只读 JOIN：集中在 Queries/*Sql.cs，返回业务 DTO，按读取目录精确登记模块/表/列与 SQL 摘要，落实租户、数据权限和运行时依赖。业务操作和权威判断使用最小 Contract Port；禁止跨模块写表、外键与事务。可靠状态推进仍使用各模块本地事务、Outbox、幂等、补偿和对账。区分当前信息与历史快照，未来按实际拆分边界替换查询；不默认建设投影。见 [ADR-0014](../architecture/adr/ADR-0014-controlled-cross-module-read-queries.md) 和 [读取依赖规范](../../contracts/architecture/module-read-queries.md)。

事务内禁止等待 HTTP、gRPC、Broker、Redis、对象存储或文件副作用。写入后可能返回失败 `Result` 的路径使用 `ICommandTransaction.ExecuteResultAsync`，避免失败结果仍提交。

可靠 Integration Event 以 MemoryPack 二进制写入 `fn_outbox_message`，同时保存 `MessageId`、`MessageType`、`SchemaVersion`、`ContentType`、Tenant、Trace 和发生时间。Worker 使用数据库租约至少一次处理，按 `MessageType + SchemaVersion` 精确匹配 Handler；重复处理必须幂等，坏载荷、未知版本或超过尝试上限进入可查询死信。

禁止为 Outbox 增加 JSON fallback、MemoryPack Union、接口、`object` 或反射式多态。事件必须使用白名单 `[MemoryPackable] partial` 具体 DTO；版本发布采用 consumer-first、producer-second、最后退役旧消费者，具体见 [Outbox Worker 运维说明](../operations/outbox-worker-topology.md)。

## 10. 日志、指标与基准

业务代码使用 `ILogger<T>`，高频路径使用 `LoggerMessage` 源生成。普通和高优先级日志使用独立有界异步通道，共享一个退出预算；日志队列不是审计账本。不得记录请求体、Cookie、Authorization、连接串、私钥或消息 payload。故障处置见[日志降级运维说明](../operations/logging-degraded-mode.md)。

指标只使用低基数标签，禁止附加用户、租户、域名、完整路径、缓存键、消息 ID 或异常文本。容量结果在生产等价环境认证前统一标记 `Capacity-not-verified`。

审计查询基准：

```powershell
dotnet run --project benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj -c Release -- audit-query
dotnet run --project benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj -c Release -- audit-query --mode sqlserver-plan-ab --providers sqlserver
dotnet run --project benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj -c Release -- audit-query --mode cursor-ab
```

生产等价混合负载入口：

```powershell
dotnet run --project benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj -c Release -- mixed-load
```

基准工件写入 `BenchmarkDotNet.Artifacts/`，不提交仓库。本地结果只用于回归和决策输入，不能直接宣传为生产 SLA 或容量证明。

## 11. 通信技术边界

| 场景 | 当前选择 | 引入或升级条件 |
|---|---|---|
| 同进程模块通信 | 强类型 Contract Port、Command/Query | 默认模式，不制造网络边界 |
| 跨进程同步服务 | gRPC + Protobuf | 出现真实独立部署、SLA 或容量边界后 |
| 可靠异步事件 | MemoryPack Outbox | 需要本模块状态与事件原子提交时 |
| 浏览器实时通信 | SignalR JSON | 尽力通知，不代替可靠业务事件 |
| AI / Agent / MCP | 供应商中立抽象和可替换 Provider | 独立业务规格、数据边界和审计获批后 |

不要为了“未来可能微服务化”提前把模块内调用改成 HTTP 或 gRPC。先保持数据所有权、契约、投影、幂等和对账边界稳定，再依据可测量的独立伸缩、故障隔离或发布需求拆分。

## 12. 工作流管理改派

在“工作流实例”中读取活动实例，具有 `workflow.instances.read` 和 `workflow.instances.recover` 才可打开改派。按姓名和账号选择当前范围的活动用户；候选支持分页，翻页会清除原选择，读取失败后可手动重读，不自动选择任何用户。已移除的企业成员、停用账号和其他范围的用户不可作为当前候选；协作实例另受已同意成员集合约束。

候选响应同时绑定原实例、显式待办和各自修订；实例已变化时关闭旧选择，重新读取详情后再操作。选中候选不等于取得业务授权，正式写入仍独立验证目标活动资格、实例和待办修订及协作边界。改派成功保留原待办历史并产生新的办理待办；同操作键重试返回原回执。

关闭在途操作或网络结果未知时，页面保留未知门槛。先读取原实例，再显式核对；核对只解锁，不会自动发送改派，人工再次提交同一意图沿用原幂等键。更换账号、会话或企业后不能接入旧候选或旧确认结果。撤回、暂停、恢复等实例动作使用同一页面归属保护，催办、连接器和路由仍使用各自精确权限。

## 13. 导入来源失效与重新上传

执行、恢复和重试在排队前实际打开原来源文件，并在等待文件读取后重验当前会话和原业务能力。来源已不存在时返回 `409 / import_export.source.unavailable`，保持原任务状态、版本、能力、检查点和会话绑定；存储暂时故障仍返回原错误，不标记为永久丢失。排队之后 Worker 仍独立读取来源、复核资格和逐行处理，排队前核验不保证文件此后始终可用。

页面显示来源失效后，不会反复排队。可以先刷新详情再人工重试，或使用“重新上传并创建任务”打开空白上传入口；新入口仍需要创建、模板读取及对应业务权限，不自动复制旧文件或提交。部分成功任务已经写入的行不会撤销，重新上传前必须先核对业务结果并排除已成功的数据。原任务的取消和已有错误回执仍按各自资格处理。

这项能力处理实际来源缺失，不设置保留期限，也不自动删除来源文件；通用来源到期政策与清理编排继续在产品化计划中单独维护。

## 14. 文档预览结果缺失与重新生成

“预览任务”下载结果时，只有服务端明确返回结果文件不存在，页面才显示“重新生成此版本”。该入口需要独立创建权限、服务端转换能力已启用，以及原任务带有明确版本。确认框固定原文档和版本，确认后复用现有创建接口生成全新任务，原任务和历史状态保留；权限错误、暂时存储故障和无法定位原版本的历史任务不会被当作可重新生成的结果。

失败或取消任务的“新建重试”仍使用独立重试权限。创建和重试不会在认证失败后自动重发；响应丢失时先保留未知回执，人工核对并允许再次创建后才能再次提交，核对本身不发送写请求。取消结果未知则先成功读取对应任务详情，再决定是否继续取消，列表刷新不解除此门槛。

后台只轮询非终态任务，隐藏页面不读取，每轮自动刷新最多五分钟，单次读取最多三十秒。自动读取失败后暂停，手动刷新恢复；切换账号、会话或企业后旧响应不能接入当前页面。此功能不设定结果保留期，也不执行文件清理。
