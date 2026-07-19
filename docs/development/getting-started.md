# Full.NET 本地开发与运行指南

## 1. 前置环境

安装 .NET 10 SDK、Git 和 Docker Desktop。Windows 环境应启用 WSL 2，并让 Docker Desktop 使用 Linux containers。先确认 Docker Engine 可用：

```powershell
docker version
docker run --rm hello-world
```

不要把真实口令、连接串或访问令牌提交到 `appsettings*.json`。本地覆盖请使用环境变量或 .NET User Secrets，部署环境使用平台的 Secret 管理能力。

## 2. 构建与测试

```powershell
dotnet restore Full.NET.slnx
dotnet build Full.NET.slnx --configuration Release
dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --minimum-expected-tests 294
dotnet tests/Full.NET.CompatibilityTests/bin/Release/net10.0/Full.NET.CompatibilityTests.dll --minimum-expected-tests 5
dotnet tests/Full.NET.ArchitectureTests/bin/Release/net10.0/Full.NET.ArchitectureTests.dll --minimum-expected-tests 26
dotnet tests/Full.NET.IntegrationTests/bin/Release/net10.0/Full.NET.IntegrationTests.dll --minimum-expected-tests 60 --timeout 45m
```

集成测试会通过 Testcontainers 启动真实 SQL Server 和 MySQL，因此 Docker 必须保持运行。CI 不跳过任何数据库测试。

测试项目使用 Microsoft.Testing.Platform 的可执行测试宿主。先完成 Release 构建，再直接运行生成的测试 DLL；`--minimum-expected-tests` 可以防止测试发现异常被误判为成功。

### 2.1 客户端工作区与双管理端

客户端要求 Node.js 24 和 pnpm 10.26.0。首次进入仓库先启用 Corepack，再使用锁文件还原：

```powershell
corepack enable
pnpm install --frozen-lockfile
pnpm test:naming
pnpm test:workspace
pnpm test:clients
pnpm build:clients
pnpm test:e2e
pnpm test:e2e:uniapp
```

`pnpm test:clients` 运行共享契约、`@fullnet/admin-i18n`、Vue、Layui 和 uni-app 单元测试，当前管理端与共享包门槛为 122 项，uni-app 为 96 项；`pnpm test:e2e` 启动两个本地服务，并用同一组 30 项 Playwright 场景验证动态导航、超级管理员列表/审计/密码重认证、租户进入/恢复/返回 Host、登录、退出、403、ProblemDetails/TraceId 和未知组件拒绝。E2E 同时覆盖 `zh-CN/en-US` 组件语言、逐请求 `Accept-Language`、刷新恢复、偏好保存失败回滚、稳定错误码、WCAG 2.2 A/AA axe 扫描、跳转链接、路由焦点、320 CSS px 重排和减弱动画偏好，禁止通过 axe 排除项绕过缺陷。

排查单个客户端层时可以直接运行：

```powershell
pnpm --filter @fullnet/admin-i18n test
pnpm --filter @fullnet/admin test
pnpm --filter @fullnet/admin-layui test
pnpm --filter @fullnet/admin-parity-e2e test
```

真实后端双管理端冒烟（需 Docker、.NET 10 SDK，且本机 `localhost` 可访问；管理端必须使用 `http://localhost`，不得用 `127.0.0.1`，否则 Refresh Cookie 无法写入）：

```powershell
pnpm test:e2e:real
```

该套件通过 Testcontainer 启动 SQL Server、执行 Migrator `--seed development` 并拉起 API（默认 `http://localhost:5149`），再对 Vue/Layui 各跑登录、动态导航与退出场景；禁止 `page.route` mock。已手动启动栈时可跳过引导：

```powershell
$env:FULLNET_E2E_SKIP_BOOTSTRAP = "1"
$env:FULLNET_E2E_API_URL = "http://localhost:5149"
pnpm test:e2e:real
```

### 2.2 uni-app 三目标基础

uni-app 要求同一 Node.js 24 与 pnpm 10.26.0 工作区。H5 开发、单元测试、标准 SFC 类型检查和三个生产目标分别运行：

```powershell
pnpm --filter @fullnet/uniapp dev:h5
pnpm --filter @fullnet/uniapp test
pnpm --filter @fullnet/uniapp typecheck
pnpm --filter @fullnet/uniapp build:h5
pnpm --filter @fullnet/uniapp build:mp-weixin
pnpm --filter @fullnet/uniapp build:mp-alipay
pnpm test:e2e:uniapp
```

业务、API、账号资料与本地存储只使用规范 BCP 47 标签 `zh-CN/en-US`；uni-app 的 `zh-Hans/en` 只存在于平台适配器和平台资源文件。匿名选择立即保存在设备；认证选择通过 `PUT /api/v1/me/locale`，请求仍携带切换前已提交的 `Accept-Language`，只有响应中的规范语言与递增 `ProfileVersion` 通过守卫后才提交。失败保留旧语言、版本和认证视图；业务分支只读取稳定 ProblemDetails code，未知 code 安全展示服务端 title 与 `traceId`。

H5 E2E 使用 DEV-only 测试端口注入认证快照并复用正式控制器、`HttpClient` 和 `uni.request`；端口和 fixture marker 会接受生产 H5 产物扫描，不进入 release。它不是登录功能，也不生成 Token 或假用户。微信与支付宝 CLI 构建成功后，仍须分别在对应开发者工具导入 `clients/uniapp/dist/build/mp-weixin` 和 `clients/uniapp/dist/build/mp-alipay`，执行中文启动、英文切换、重启保持、真实登录/API Header、错误、会话失效和导航标题验证。缺少工具时只能记录 `Not executed — required tool not installed`，不能写成已验证。当前证据见[uni-app 多语言验证记录](../verification/uniapp-localization.md)。

自动检查不能替代真实辅助技术。版本发布前仍须在 Windows Edge + NVDA 下人工冒烟登录、导航、错误反馈和租户切换，并检查浏览器 200% 缩放及强制颜色模式；这些项目完成前不得把 C1 标记为 `Verified`。生产构建会把 Layui 2.13.8 从锁定的 MIT npm 包打入本地产物，不依赖公共 CDN，也不包含 layuiAdmin 产品主题源码或资产。`@axe-core/playwright` 仅为 MPL-2.0 开发测试依赖，不进入最终发布物。

直接运行 API 时默认地址为 `http://localhost:5149`。分别在两个终端设置同一个 API 地址并启动开发服务：

```powershell
$env:VITE_API_BASE_URL = "http://localhost:5149"
pnpm --filter @fullnet/admin dev

$env:VITE_API_BASE_URL = "http://localhost:5149"
pnpm --filter @fullnet/admin-layui dev
```

开发服务监听 `127.0.0.1:5173/5174`；涉及真实 Cookie 会话时，请在浏览器使用 `http://localhost:5173` 和 `http://localhost:5174`，与 `http://localhost:5149` API 保持同站，避免 `SameSite=Strict` Cookie 被跨站规则阻断。两端使用同一个 `/api/v1` 契约和标准 ProblemDetails，并已实现内存 Access Token、启动刷新、并发 401 单次刷新、退出、租户切换和动态权限导航。生产环境可在构建时提供 `VITE_API_BASE_URL`；Layui 也支持在入口模块加载前设置 `globalThis.FULLNET_CONFIG.apiBaseUrl`。

服务端返回的导航元数据不是可执行配置。Vue 与 Layui 都必须先执行共享契约校验，再把语义组件、路由和路径映射到各自源码内的精确白名单；未知标识会被拒绝，禁止动态导入任意路径、执行字符串代码或插入任意 HTML。按钮隐藏只改善交互，API 仍执行服务端权限策略。Access Token、有效租户和权限快照只保存在内存，页面刷新通过 Refresh Cookie 恢复，不得写入 `localStorage` 或 `sessionStorage`。

### 2.3 多语言当前边界与后续契约

当前已实现 Vue/Layui 管理端的 `zh-CN/en-US` 自有文案、语言持久化、`html lang`、页面标题、Element Plus/Day.js、Layui 公开 `i18n.set` 组件语言和双端 E2E；所有管理端 HTTP 请求在发送前读取当前活动语言并覆盖为规范 `Accept-Language`，认证刷新重试保持相同语义。账号语言偏好与租户默认语言已通过双库 `004_LocalizationPreferences.sql` 持久化；客户端只在完整 `/api/v1/me` 快照通过守卫后同步偏好，认证切换通过 `PUT /api/v1/me/locale` 使用独立 `ProfileVersion`，只有响应通过守卫才提交本地语言和版本。保存失败保留会话、租户、旧语言和旧版本，TokenResponse/JWT 不携带偏好。服务端已建立规范别名映射、异步 CultureScope、本地化 ProblemDetails、模块错误资源与响应头能力；标准错误的 `status/code/traceId/violations` 不随语言变化，`title` 与兼容适配器的 `message` 在响应边界按协商语言解析。uni-app 已有基础应用、三目标构建和 H5 自动冒烟，但仍缺两个小程序开发者工具、真实登录/租户/会话流程验收，因此保持 `Implementing / Build-verified`。Flutter、业务翻译表、通知/报表、Realtime 与 AI 输出仍属于后续阶段，不得据此宣称 Full.NET 全栈多语言已经完成。

全栈方案统一使用 BCP 47 的 `zh-CN` 和 `en-US`；uni-app 内部的 `zh-Hans` 与 Flutter ARB 的 `zh_CN` 只在各自适配层出现。HTTP 业务逻辑始终依赖稳定 `status/code/traceId`，不比较本地化 `title/detail`。日期按 UTC/ISO 传输，语言和时区分别处理；通知、报表、Realtime 服务端文本与 AI 输出必须显式指定接收者语言。

详细设计与实施顺序见：

- [全栈多语言与本地化设计](../superpowers/specs/2026-07-17-full-stack-localization-design.md)；
- [全栈多语言实施计划](../superpowers/plans/2026-07-17-full-stack-localization.md)；
- [客户端交付路线图](../roadmap/client-delivery-roadmap.md)。

## 3. 使用 Aspire 启动完整环境

```powershell
dotnet user-secrets set "Parameters:identity-bootstrap-username" "admin" --project src/Hosts/Full.NET.AppHost/Full.NET.AppHost.csproj
dotnet user-secrets set "Parameters:identity-bootstrap-password" "<至少12位且含大小写、数字和特殊字符>" --project src/Hosts/Full.NET.AppHost/Full.NET.AppHost.csproj
dotnet run --project src/Hosts/Full.NET.AppHost/Full.NET.AppHost.csproj
```

也可以不预先写入 User Secrets，直接在 Aspire Dashboard 的未解析参数提示中输入，并选择保存到 User Secrets。密码由 Secret Parameter 传给 Migrator，不作为普通参数发布。终端会输出带一次性登录令牌的 Dashboard 地址，正常启动顺序是：

1. 数据库和 Redis 就绪；
2. Migrator 执行 DbUp 迁移并以代码 0 退出；
3. API 和 Worker 启动；
4. API 的 `/health/live`、`/health/ready`、`/health/startup` 返回健康状态。

AppHost 默认使用 SQL Server。切换到 MySQL，可修改 `src/Hosts/Full.NET.AppHost/appsettings.json`：

```json
{
  "UseMySql": true
}
```

也可以仅对当前终端覆盖：

```powershell
$env:UseMySql = "true"
dotnet run --project src/Hosts/Full.NET.AppHost/Full.NET.AppHost.csproj
```

AppHost 给 Migrator 传入 `--seed development` 以及两个 Bootstrap Parameter。Migrator 先完成迁移，再通过模块化 Seed Orchestrator 确定性执行 Baseline 和 Development Contributor，幂等协调标识为 `local`、域名为 `localhost` 的开发租户及首个宿主管理员；账号已存在时不会覆盖密码。

直接运行 Migrator 且不传 Seed 参数时只执行迁移；可显式使用 `--seed baseline|development|demo|test`，Production 只允许 Baseline，Development/Demo/Test 确定性继承 Baseline。`--seed-local` 暂时映射到 Development 并输出弃用告警。首个管理员仍要求成对提供 Secret，缺失时以稳定码 `seeding.bootstrap.secret_missing` 失败；具体测试场景数据继续由临时数据库内的 Test Factory 隔离创建。完整 Profile 双库 E2E 与生产运维验收仍待后续任务，详细边界见[种子数据模块设计](../superpowers/specs/2026-07-17-seed-data-module-design.md)和[测试先行实施计划](../superpowers/plans/2026-07-17-seed-data-module.md)。

### 3.1 Identity 会话与密钥

浏览器会话端点如下：

| 方法 | 路径 | 说明 |
|---|---|---|
| `POST` | `/api/v1/auth/login` | 校验精确 Origin、限流和账号锁定，返回短期 Access Token |
| `POST` | `/api/v1/auth/refresh` | 校验 Refresh Cookie 与 `X-CSRF-Token`，原子轮换会话 |
| `POST` | `/api/v1/auth/logout` | 撤销会话并清除 Cookie |
| `GET` | `/api/v1/me` | 从数据库返回当前用户、Actor/有效作用域、账号语言偏好和资料版本 |
| `PUT` | `/api/v1/me/locale` | 规范化并乐观并发更新当前认证账号语言偏好 |
| `GET` | `/api/v1/navigation` | 返回当前有效作用域的权限导航树 |
| `GET` | `/api/v1/tenancy/available` | 返回当前 Actor 可进入的活动租户列表 |
| `PUT` | `/api/v1/tenancy/context` | 以乐观并发方式进入租户或返回 Host，并签发新 Access Token |

Refresh Token 只存在于 `Secure`、`HttpOnly`、`SameSite=Strict` 的 `__Host-fullnet-refresh` Cookie，数据库只保存 SHA-256 哈希；前端可读的 `fullnet-csrf` Cookie 只用于双提交 CSRF 校验。Access Token 默认 10 分钟，只保存在管理端内存，页面刷新后必须通过 Refresh Cookie 恢复。Refresh 会话保存 `ActiveTenantId`，因此刷新和令牌轮换不会退回错误租户；切换冲突返回 HTTP 409，客户端只自动刷新一次上下文后要求用户重试。

Access Token 使用以下 Full.NET 私有 Claim；自定义 Host 或网关不得重命名、信任客户端伪造值或把它们降级为普通请求 Header：

| Claim | 语义 |
|---|---|
| `fullnet_actor_scope` | 账号原始作用域，例如 `host` 或未来的 `tenant:{TenantId:N}` |
| `fullnet_scope` | 当前有效作用域；取值为 `host` 或 `tenant:{TenantId:N}` |
| `fullnet_tenant_id` | 当前有效租户标识；Host 上下文中不存在 |
| `fullnet_permission` | 可重复出现的当前有效权限码 |

当前授权上下文由双库迁移 `003_AuthorizationContext.sql` 建立。它只提供最小 RBAC、默认宿主管理权限和上下文切换基础，不代表用户、角色、菜单、组织或租户账号 CRUD 已经完成。租户中间件只信任签名令牌中的有效租户；请求体、查询字符串和自定义租户 Header 都不能改变授权上下文，域名与令牌租户不一致时返回 HTTP 403 `tenancy.context_mismatch`。

Development 配置允许临时 RSA 签名密钥，并仅允许列出的本地管理端 Origin。Production 必须从 Secret 管理器提供以下配置，否则 API 启动校验失败：

```text
Identity__ActiveKeyId=<当前密钥标识>
Identity__SigningKeys__<当前密钥标识>__PublicKeyPem=<RSA 公钥 PEM>
Identity__SigningKeys__<当前密钥标识>__PrivateKeyPem=<RSA 私钥 PEM>
Identity__AllowedOrigins__0=https://admin.example.com
Tenancy__HostDomains__0=api.example.com
```

密钥轮换时先同时配置新旧公钥和新私钥，再切换 `ActiveKeyId`；确认旧令牌全部过期后才移除旧公钥。不得在生产启用 `Identity__AllowDevelopmentEphemeralSigningKey`，不得把 PEM、Bootstrap 密码、Cookie 或 Token 写入仓库和日志。

## 4. 部署和迁移顺序

生产部署必须先运行 `Full.NET.Host.Migrator` 并确认 `003_AuthorizationContext.sql` 等迁移成功，再发布或启动 API/Worker，最后发布 Vue/Layui 客户端。API 永远不在启动时执行生产迁移，Worker 也不会修改数据库结构。这样可以让数据库契约先于 API 和客户端生效，并让迁移权限、运行时权限和回滚决策保持独立。

必要配置项：

```text
Database__Provider=SqlServer        # 或 MySql
Database__ConnectionName=fullnet
Database__MySqlGuidStorageMode=Binary16
ConnectionStrings__fullnet=<由 Secret 管理器注入>
```

`Database__MySqlGuidStorageMode` 在 Production 必须显式配置为 `Binary16`，`LegacyChar36` 只允许用于迁移测试与 009 之前的受控工具连接。API 与 Worker 启动时会核对 `fn_uuid_contract_state.SchemaMode`，模式不一致即拒绝启动。普通 API、Worker 与 Seed 连接不允许 MySQL 用户变量，只有 Migrator 连接会为条件 DDL 启用该能力。

执行 009 时还必须仅向 Migrator 注入 `UuidBinaryContract__MaintenanceMode=true`、`BackupVerified=true`、`LegacyWritersStopped=true` 和已登记的 `DestructiveDdlApprovalId`；详见 [UUID Binary16 迁移 Runbook](uuid-binary-migration-runbook.md)。

## 5. 缓存约定

FusionCache 是唯一缓存实现，业务代码可以依赖 `IFusionCache`，也可以依赖由 `.AsHybridCache()` 暴露的 Microsoft `HybridCache`；两者指向同一个底层实例。

没有 Redis 连接串时，框架退化为单进程内存缓存，适合单实例开发和轻量部署。多实例部署必须配置 `ConnectionStrings__redis` 或 `Cache__RedisConnectionString`，以启用分布式缓存和 Redis Backplane。租户缓存 key 必须通过 `CacheKeyBuilder` 构建，失效优先使用租户/域名 tag。

## 6. HTTP、JSON 与 Admin.NET 兼容

默认公共 API 使用真实 HTTP 状态码：成功返回直接 DTO；失败返回 RFC ProblemDetails，并在扩展字段中携带稳定错误 `code`、`traceId` 与结构化 `violations`。`title` 和兼容 `errors` 在响应边界本地化，调用方必须依据机器字段处理分支；默认不会把所有结果包成 `success/data/code`。

每个模块应为自己的 HTTP DTO 提供 System.Text.Json `JsonSerializerContext`，并把生成的 context 插入 `TypeInfoResolverChain`。不要在请求热路径创建新的 `JsonSerializerOptions`，也不要引入 Newtonsoft.Json。

`Error` 保留既有四参数构造、可初始化的 `Message` 和四元解构契约；结构化 `Arguments/ValidationViolations` 只通过无歧义的扩展构造增量提供。JSON 继续输出 `message`，`DefaultMessage` 只是标记为 `JsonIgnore` 的安全语义别名，不得同时输出 `message/defaultMessage`。生产者应让每条兼容 `ValidationErrors` 与同序 `ValidationViolation` 一一对应；映射器会安全保留尚未配对的旧消息，但不会记录可能包含用户输入的消息内容。

需要兼容现有 Admin.NET 前端时，在自定义 Host 中于服务默认值之后显式启用：

```csharp
builder.AddFullNetServiceDefaults();
builder.Services.AddAdminNetCompatibility();
```

该适配器只替换 `IApiResultMapper`，仍保留 400/404/409/500 等真实状态码，也不会包裹文件、流、SSE、SignalR、健康检查或 `204` 响应。

## 7. 验证管道约定

模块通过 `AddFullNetFluentValidation()` 启用适配层，并显式注册每个 `IValidator<TCommand>`。禁止程序集扫描；显式注册让模块依赖、启动成本和 Native AOT/裁剪行为保持可预测。

Validator 只放输入结构规则，例如必填、格式、范围和长度。数据库唯一性、权限、当前状态和其他依赖外部状态的业务规则仍由 Handler/Domain 负责。HTTP Request DTO 与内部 Command 保持分离；不要在 Endpoint 再复制一套只对 HTTP 生效的同类校验。

验证失败统一返回 `ErrorType.Validation` 和稳定错误码 `validation.failed`，由现有 API 映射器输出 HTTP 400 ProblemDetails。调度管道在事务终端委托之外执行，因此无效的事务命令不会打开 Dapper 事务，也不会调用 Handler。

## 8. 内部消息与 Outbox

当前进程内调用使用强类型 Contracts。事务性集成事件写入 `fn_outbox_message`，payload 固定为 `application/x-msgpack`，并同时保存事件 `Type`、`SchemaVersion`、租户、trace 和发生时间。

Worker 以租约方式批量获取消息，按 `(EventType, SchemaVersion)` 精确匹配唯一处理器。成功后才标记完成；失败会释放租约并指数退避。处理器必须幂等，因为至少一次投递允许重复执行。禁止为 Outbox 增加 JSON fallback、typeless 或 contractless MessagePack resolver；合约成员必须使用稳定、唯一的整数 key。

## 9. 日志与可观测性

应用代码使用 `ILogger<T>`，高频路径使用 `LoggerMessage` 源生成。Serilog Console Sink 位于有界异步队列后，默认队列满时丢弃而不阻塞请求；可通过以下设置调整：

```json
{
  "FullNet": {
    "Logging": {
      "AsyncBufferSize": 10000,
      "BlockWhenFull": false
    }
  }
}
```

应监控 `fullnet.logging.queue.depth`、`fullnet.logging.queue.capacity`、`fullnet.logging.events.dropped` 和 `fullnet.localization.error.fallbacks`。本地化回退指标只包含稳定 `code/locale` 标签，禁止附加格式化参数、密码或用户输入。普通异步日志不承担审计账本职责；安全/业务审计后续使用独立可靠存储。日志不得记录请求体、Cookie、Authorization、连接串或消息 payload。

## 10. 通信技术矩阵

| 场景 | 当前选择 | 引入时机 |
|---|---|---|
| 模块内/模块间调用 | 进程内强类型 Contracts + CQRS | M1 已启用 |
| 跨进程服务调用 | gRPC + Protobuf | 出现真实服务拆分、独立部署与容量边界后 |
| 可靠异步事件 | MessagePack Outbox | M1 已启用 |
| 浏览器实时通信 | SignalR，优先 MessagePack、必要时 JSON | M2 Realtime |
| AI/Agentic Web | Microsoft.Extensions.AI、Agent Framework、MCP、AG-UI | 独立 M5+ 计划 |

不要仅为“将来可能微服务化”而提前把模块内调用改成 gRPC；先保持模块边界和合约稳定，再按真实部署需求拆分。
