# Full.NET 全栈多语言与本地化设计

- 日期：2026-07-17
- 状态：已确认
- 决策方式：依据项目所有者“后续自动确认（按推荐方案来）”的持续授权，采用推荐方案
- 适用范围：ASP.NET Core API/Worker、Vue、Layui、uni-app、Flutter，以及命中门禁后的 .NET MAUI
- 首期语言：简体中文 zh-CN、美国英语 en-US

## 1. 结论

Full.NET 采用“统一治理、平台原生实现”的多语言架构：

1. 全仓库共享 BCP 47 语言清单、默认语言、别名映射、回退顺序、错误码、术语和完整性门禁；
2. 不建立一个同时运行在 C#、浏览器、小程序和 Dart 上的万能翻译引擎；
3. Vue/Layui 延续已经落地的无框架管理端消息契约，并补齐 Element Plus、Day.js 与 Layui 组件自身的语言；
4. uni-app 使用 Vue I18n 以及 uni.getLocale、uni.setLocale、uni.onLocaleChange，并单独处理 pages.json、manifest.json 和小程序平台限制；
5. Flutter 使用 flutter_localizations、intl 与 gen_l10n/ARB；
6. ASP.NET Core 使用 RequestLocalizationMiddleware、IStringLocalizer 与模块级 .resx；
7. 公共协议始终以稳定 code、字段名、枚举值和 UTC/ISO 数据为逻辑依据；本地化 title、detail 和可见文本只能用于展示；
8. 后台任务、通知、邮件、报表和 AI 输出必须显式携带或解析目标语言，禁止依赖任意线程残留的 CurrentUICulture。

这种方案比“单一大字典”更符合各平台工具链，又比各端独立维护更能阻止语言清单、术语和错误语义漂移。

## 2. 当前实现与真实缺口

用户判断“没有完整设计多语言”是成立的，但 Vue/Layui 并非从零开始。

| 范围 | 当前事实 | 缺口 |
|---|---|---|
| Vue 管理端 | 已支持 zh-CN/en-US 自有文案、Element Plus/Day.js 组件语言、逐请求 Accept-Language、账号偏好原子同步和双端 E2E | 真实辅助技术人工验收与后续业务模块资源仍需逐切片完成 |
| Layui 管理端 | 已复用同一消息键，以纯文本 DOM 绑定切换语言，并通过公开 i18n.set 配置表格、分页、日期、表单和上传等组件消息 | 真实辅助技术人工验收与后续业务模块资源仍需逐切片完成 |
| ASP.NET Core | 已实现请求本地化、规范别名、Content-Language、本地化 ProblemDetails/violations、资源完整性、账号偏好与租户默认语言双库持久化 | 后台任务、通知及业务内容翻译需随真实消费者实现 |
| uni-app | 已建立 Vue 3/TypeScript 应用、Vue I18n、规范语言适配、pages/manifest、本地资源、逐请求 Accept-Language、账号偏好原子提交、ProblemDetails、96 项单测、类型检查、三目标 CLI 构建和 H5 E2E | 微信/支付宝开发者工具与真机未执行；真实登录、租户、会话失效、原生组件和发布流程仍待平台验收 |
| Flutter | 技术路线已确定 | clients/flutter 尚未创建；ARB、生成类、平台包名称、布局方向和请求语言均未落地 |
| .NET MAUI | 仅有决策门禁 | 命中门禁后才采用 .resx 与平台资源；当前不创建第二套 App |
| 业务内容 | 菜单、字典、通知、模板、报表仍在后续模块 | 尚未区分系统资源、用户输入和需要多版本翻译的业务内容 |
| 异步/Realtime/AI | 只有总体技术规划 | 没有接收者语言快照、模板版本、连接语言或 AI 输出语言契约 |

因此，现有 L0-L2 可表述为“服务端基础与双管理端支持两种语言”，但在 uni-app、Flutter、业务内容和异步输出完成前，仍不能表述为“Full.NET 全栈多语言已完成”。

## 3. 方案比较

| 方案 | 优点 | 主要问题 | 结论 |
|---|---|---|---|
| 一份 JSON/ICU 字典生成全部 C#、TypeScript、uni-app 和 Dart 运行时 | 表面上只有一个来源 | 各平台占位符、复数、生成器、组件库和包级资源规则不同；生成器会成为新的高耦合基础设施 | 不采用 |
| 每个平台独立维护语言清单和消息 | 最符合各端原生工具链 | 支持语言、回退、错误码和术语会长期漂移 | 不采用 |
| 共享治理契约，各平台使用原生资源格式 | 保留平台生成、类型检查和组件生态，同时可统一语义和 CI | 需要建立跨资源验证脚本 | 采用 |

首期不因只有两种语言就引入在线翻译中心、运行时远程字典或复杂翻译管理平台。后续出现非开发人员高频更新文案、十种以上语言或外部翻译供应商时，再为资源导入导出建立独立 Provider。

## 4. 统一语言契约

### 4.1 标准标识

仓库和 HTTP 契约统一使用 BCP 47 标签：

| 规范语言 | .NET | 浏览器/Vue/Layui | uni-app 框架映射 | Flutter ARB/Locale | 方向 |
|---|---|---|---|---|---|
| zh-CN | zh-CN | zh-CN | zh-Hans | app_zh_CN.arb / Locale('zh', 'CN') | ltr |
| en-US | en-US | en-US | en | app_en.arb（@@locale=en_US）/ Locale('en', 'US') | ltr |

uni-app 的框架内置简体中文标识是 zh-Hans；该差异只存在于 uni-app 适配层。对外 API、用户资料和审计字段始终保存 zh-CN，禁止同一语言同时保存 zh、zh-Hans、zh_CN 等多种形式。

首期别名解析：

- zh、zh-Hans、zh-SG 回退到 zh-CN；
- en、en-GB 等未单独提供资源的英语变体回退到 en-US；
- 未知或格式非法的语言忽略并回退，不把任意字符串构造成资源路径；
- 新增具体语言后，精确匹配始终优先于上述回退。

### 4.2 仓库级事实来源

实施时建立以下目录：

~~~text
localization/
├── locales.json
├── glossary.json
├── README.md
└── schemas/
    └── locale-catalog.schema.json
~~~

locales.json 记录默认语言、支持语言、平台映射、方向和回退；glossary.json 记录 Tenant、Host、TraceId、Access Token 等必须保持一致或禁止翻译的术语。该目录是治理事实来源，不直接成为生产运行时依赖。各平台编译自己的强类型资源，CI 比较它们与清单的一致性。

新增语言必须同时满足：

1. 语言已进入 locales.json，标签与平台映射合法；
2. 公共错误、认证、导航、设置和客户端壳层的必需键完整；
3. Vue、Layui、uni-app、Flutter 与服务端中已处于维护状态的范围全部通过资源检查；
4. 日期、数字、货币、复数、长文本、布局方向和组件库语言完成验证；
5. 不完整语言不能出现在生产语言选择器中。

### 4.3 消息命名

消息键采用稳定命名空间：

- common.*：通用动作、空状态和可访问名称；
- auth.*、tenancy.*、identity.*：模块界面；
- validation.*：验证语义；
- error.<稳定错误码>：错误展示，例如 error.identity.invalid_credentials；
- notification.*、email.*、report.*：服务端模板；
- platform.*：只在特定客户端存在的能力。

消息键不是权限码、路由、组件名或数据库主键。翻译文本不得被解释为 HTML、URL、SQL、正则、组件路径或 Agent Tool 参数。

## 5. 语言选择、持久化与同步

### 5.1 客户端活动语言

客户端按以下顺序确定活动语言：

1. 已认证账号最近一次由服务端确认的 PreferredLocale；
2. 匿名阶段本地保存的明确选择；
3. 浏览器、uni-app 或 Flutter 设备语言；
4. Full.NET 默认 zh-CN。

用户在客户端切换语言时：

- 匿名状态立即更新本地界面、后续请求的 Accept-Language 与本地偏好；
- 已认证状态携带当前 ProfileVersion 更新账号 PreferredLocale，只有服务端响应通过完整快照守卫后才原子提交本地语言和版本；
- 已认证保存失败时保留原语言、原 ProfileVersion、会话和租户，并显示可重试提示，禁止用乐观切换制造跨设备偏好不一致；
- 退出后保留最后语言供登录页使用，但不保留令牌、租户权限快照或其他敏感状态；
- 新设备在没有本地明确选择时采用服务端账号偏好。

PreferredLocale 不进入 Access Token Claim，避免纯展示偏好变化触发令牌轮换或被误认为授权信息。登录、刷新和租户切换后的既有会话 hydrate 从 /api/v1/me 取得偏好；请求本身始终以客户端当前 Accept-Language 为准。

### 5.2 租户默认语言

Tenancy 提供 DefaultLocale，供新账号初始化、公共租户内容和无人值守通知回退。它不能覆盖用户已明确选择的 PreferredLocale，也不能改变租户授权边界。

### 5.3 HTTP 协商

公共 API 使用以下规则：

- 客户端发送 Accept-Language；Full.NET 自有客户端发送单个规范标签；
- API 只启用受支持的 AcceptLanguageHeaderRequestCultureProvider，不把 QueryString 或普通 Cookie 作为生产 API 的语言来源；
- 不支持的请求语言安全回退到 zh-CN；写入 PreferredLocale 时不支持的值返回 HTTP 400 和 localization.unsupported_locale；
- 本地化响应写入 Content-Language；
- 公开且可缓存的本地化响应写入 Vary: Accept-Language；FusionCache 的本地化值必须把规范语言纳入 key；
- OpenAPI 声明 Accept-Language、Content-Language、稳定 code 和本地化文本非逻辑字段的约束。

HTTP 成功 DTO 默认保持语言中立。日期传 UTC/ISO 8601，枚举传稳定 code，金额同时传数值和明确币种；客户端按 locale 与用户时区显示。禁止服务端把 2026/07/17、1,234.56 等格式化文本当作业务数据返回。

## 6. ASP.NET Core 与模块资源

### 6.1 基础设施

新增 Full.NET.Localization BuildingBlock，负责：

- 配置默认语言和支持语言；
- 规范化 BCP 47 标签；
- 注册 AddLocalization 与 RequestLocalizationOptions；
- 只保留 Accept-Language Provider；
- 在 HTTP 管道早期设置 CurrentCulture/CurrentUICulture；
- 提供 ILocaleContext 和 ILocaleNormalizer；
- 为响应设置 Content-Language/Vary；
- 为 Worker 提供显式 CultureScope，离开作用域必须恢复原文化。

RequestLocalization 必须在异常处理、认证授权、租户中间件和模块 Endpoint 之前执行，确保 ProblemDetails 和验证文本使用同一语言。

### 6.2 模块级 .resx

资源按模块归属，禁止把所有错误堆进 Host：

~~~text
src/BuildingBlocks/Full.NET.Hosting/Resources/CommonErrors.resx
src/BuildingBlocks/Full.NET.Hosting/Resources/CommonErrors.en-US.resx
src/Modules/Full.NET.Modules.Identity/Resources/IdentityErrors.resx
src/Modules/Full.NET.Modules.Identity/Resources/IdentityErrors.en-US.resx
src/Modules/Full.NET.Modules.Tenancy/Resources/TenancyErrors.resx
src/Modules/Full.NET.Modules.Tenancy/Resources/TenancyErrors.en-US.resx
~~~

默认 .resx 使用 zh-CN，en-US 文件提供英文。模块注册自己的错误码前缀与资源标记类型，Hosting 聚合查找；找不到键时使用经过审查的安全默认文本并记录缺失资源指标，不能把资源键或堆栈暴露给用户。

### 6.3 ProblemDetails 与验证

ProblemDetails 保持标准 HTTP 语义：

- code、type、status、traceId、字段路径和验证 code 稳定且不本地化；
- title、detail 和兼容 errors 文本按请求语言本地化；
- 增加结构化 violations，元素至少包含 field、code、arguments；客户端可用本地资源重新渲染，也可回退到服务端文本；
- Error 领域对象保存稳定 Code、ErrorType、安全默认信息和结构化参数，不在 Handler 内读取 HTTP 文化；
- FluentValidation 规则使用稳定验证 code 与参数，避免把英文 WithMessage 当成唯一契约；
- Admin.NET 兼容层只改变外层映射，不建立另一套翻译资源。

客户端判断登录失效、冲突、限流或业务分支时只能读取 code/status，禁止比较 title/detail。

## 7. 客户端实现边界

### 7.1 Vue 管理端

当前 @fullnet/admin-i18n 继续作为管理端业务消息的轻量共享契约，首期不为“换库”而重写已通过的适配器。补齐：

- 根组件使用 ElConfigProvider 根据活动语言切换 Element Plus 语言包；
- 同步 Day.js locale，使日期选择器、月份、周起始等一致；
- HTTP 适配器自动发送 Accept-Language；
- 登录后同步 PreferredLocale，退出后保留非敏感语言偏好；
- 使用 Intl.NumberFormat、Intl.DateTimeFormat、Intl.RelativeTimeFormat，时区与语言分开处理；
- 大型模块资源达到性能阈值后按路由懒加载，首期两种壳层语言继续静态打包。

### 7.2 Layui 管理端

应用自有文案继续通过 @fullnet/admin-i18n 和纯文本绑定。Layui 2.13.8 已提供公开 i18n 模块，必须在第一次组件渲染前通过 LAYUI_GLOBAL.i18n 或公开 i18n.set 配置组件语言，覆盖 table、laypage、laydate、layer、upload、form 等实际使用组件。

禁止调用文档明确标记为私有的 i18n.$t，也禁止为了组件语言复制 Layui 内部源码。切换语言后需要重建的组件必须保存稳定业务状态并按公开 API 重渲染；不能以整页刷新掩盖会话或表单状态丢失。

### 7.3 uni-app

clients/uniapp 使用 Vue 3 + TypeScript + Vue I18n：

- 应用文案由 Vue I18n 管理；
- 通过适配器把外部 zh-CN/en-US 映射为 uni-app 的 zh-Hans/en；
- 使用 uni.getLocale、uni.setLocale 与 uni.onLocaleChange；
- locale/ 资源处理 pages.json 与 manifest.json；
- 小程序不支持的动态 pages.json 文案改用 uni.setNavigationBarTitle；
- tabBar 需要运行时切换时使用经过设计的自定义 tabBar，否则明确要求重启/重新进入；
- 微信/支付宝原生组件或 API 不能被 Vue I18n 控制的部分必须分别真机/开发者工具验收；
- uni.request 统一发送外部规范 Accept-Language，不把 zh-Hans 暴露给 API。

H5 同步 html lang/dir；微信和支付宝分别构建，禁止以 H5 成功推断小程序多语言成功。当前 H5 浏览器冒烟与三个 CLI 目标已通过，但微信、支付宝开发者工具未安装，所以 L3/C3 保持 `Implementing / Build-verified`，不标记为 `Verified`。

### 7.4 Flutter

clients/flutter 使用 Flutter 官方 gen_l10n：

- pubspec 启用 flutter generate；
- l10n.yaml 固定 ARB 输入、输出、缺失消息报告；
- app_en.arb 作为生成模板并声明 @@locale=en_US，app_zh_CN.arb 声明 @@locale=zh_CN；
- MaterialApp 使用生成的 localizationsDelegates 与 supportedLocales；
- onGenerateTitle 本地化应用标题；
- HTTP 拦截器发送规范 Accept-Language；
- 用户选择保存在安全且适合偏好的本地存储，令牌仍只进安全存储；
- Android/iOS/Windows/macOS/Linux 的应用名称、系统菜单和安装包元数据按平台资源分别处理；
- Widget 测试覆盖长文本、文本缩放、Locale 切换和方向；新增 RTL 语言前必须完成 Directionality 与镜像图标验收。

### 7.5 .NET MAUI

MAUI 仍受现有决策门禁约束。命中后使用 .resx、CurrentUICulture 与平台资源本地化应用名/图片，并复用同一个 HTTP 语言契约；不得因为服务端也使用 .resx 就直接引用服务端资源程序集。

## 8. 业务内容、通知与异步边界

系统 UI 和错误资源编译进发布物；用户或租户可编辑内容不能写进 .resx：

| 内容 | 存储与翻译策略 |
|---|---|
| 权限码、枚举 code、路由语义 | 永不翻译，客户端按稳定 code 显示 |
| 菜单标题、字典标签、公告、模板 | 需要多语言时由所属模块建立规范化 translation 表，唯一键包含 TenantId、EntityId、Locale |
| 用户名、租户名称、文件名 | 默认作为业务数据原样显示，不自动机器翻译 |
| 审计事件 | 存事件 code、参数和原始时间；查看时本地化，证据字段保持原值 |
| 邮件、短信、推送、PDF | 按接收者 PreferredLocale 或租户默认语言在服务端渲染，保存 TemplateKey、TemplateVersion、Locale 与参数快照 |
| SignalR | 优先发送 code + data 让客户端本地化；服务端文本必须按连接/接收者语言显式渲染 |
| gRPC/MessagePack/Outbox | 内部契约不本地化；只有确实需要服务端渲染的命令携带规范 Locale |
| AI/Agentic Web | Tool 名、Schema、权限与审计 code 保持稳定；用户自然语言输出显式指定 locale，模型不得翻译权限或工具参数 |

翻译表必须由所属模块拥有，不建立跨模块通用 EAV 翻译表。这样可以保留外键、唯一约束、租户范围和 Dapper SQL 的可解释性，并能在 SQL Server/MySQL 上建立相同业务约束。

## 9. 性能、缓存与安全

1. 两种首期语言的壳层资源静态编译；大型业务模块达到可测量体积阈值后按模块/语言懒加载；
2. .resx 使用 ResourceManager 缓存，禁止每请求扫描程序集或读取磁盘 JSON；
3. 请求文化解析只检查有界支持列表，不查询数据库；客户端已通过 Accept-Language 传递活动语言；
4. FusionCache 中任何本地化值的 key 必须包含规范 locale，失效 tag 同时覆盖实体和语言变体；
5. 缺失键、非法语言、回退次数和通知模板渲染失败进入低基数指标；locale 是有界标签，用户输入文本不得作为指标标签；
6. 翻译参数只进入文本格式化；Web 使用 textContent/Vue 插值，禁止未经清理的 v-html/innerHTML；
7. 资源文件不得包含密钥、连接信息、内部堆栈、SQL 或不应公开的运维说明；
8. RTL、Unicode 规范化、大小写比较和文化相关排序必须显式测试；安全标识比较始终使用 Ordinal/OrdinalIgnoreCase 或规范化 code，不使用 CurrentCulture。

## 10. 测试与 CI 门禁

### 10.1 公共门禁

- locales.json Schema、标签、默认语言和平台映射有效；
- 各维护端的 supportedLocales 与仓库清单一致；
- 必需消息键完整，缺失翻译报告为空；
- 错误码与服务端资源、客户端 error.* 映射一致；
- 资源不包含 HTML/脚本占位或未知格式参数；
- OpenAPI 的语言 Header 与结构化验证契约不漂移。

### 10.2 服务端

- 单元测试覆盖 Accept-Language q 值、别名、未知值、回退和 CultureScope 恢复；
- API 测试对 zh-CN/en-US 断言相同 status/code/traceId、不同本地化 title，并断言 Content-Language；
- FluentValidation violations 的 code/arguments 在两种语言下相同；
- SQL Server/MySQL 同时覆盖 PreferredLocale、DefaultLocale 和翻译表唯一约束；
- Worker 并行处理不同语言时互不串文化。

### 10.3 客户端

- Vue/Layui 同场景 E2E 同时断言应用文案、Element Plus/Layui 组件、请求 Header、刷新持久化和长文本布局；
- uni-app 分别构建 H5、mp-weixin、mp-alipay；H5 自动验证标题、错误、语言切换与 API Header，两个小程序必须在各自开发者工具或真机补齐同场景证据；
- Flutter 执行 flutter gen-l10n、flutter analyze、单元/Widget 测试和受影响平台构建；
- 所有客户端都验证语言切换不清除会话、不改变租户、不扩大权限、不依赖本地化错误文本。

## 11. 分阶段交付

| 阶段 | 交付 | 退出条件 |
|---|---|---|
| L0 统一契约 | locales/glossary、资源规范、验证脚本 | 清单与现有管理端资源一致，CI 能阻止缺键和非法语言 |
| L1 后端本地化 | BuildingBlock、Accept-Language、.resx、ProblemDetails/violations | zh-CN/en-US API 集成测试通过，逻辑 code 完全一致 |
| L2 双管理端补齐 | Element Plus/Day.js、Layui i18n、Header、账号偏好 | 两端应用与组件文案、偏好同步和 E2E 通过 |
| L3 uni-app | 工程、Vue I18n、平台映射、pages/manifest、三目标构建；当前 `Implementing / Build-verified` | H5/微信/支付宝分别完成语言切换、登录/API/错误冒烟 |
| L4 Flutter | ARB/gen_l10n、请求语言、平台资源、移动/桌面验证 | 每个声明平台有构建与本地化冒烟证据 |
| L5 业务内容与异步 | 翻译表、通知/报表模板、Realtime、AI 输出语言 | 多接收者、多租户、多语言并发与回退可验证 |
| L6 MAUI（按需） | 命中门禁后的 .resx 与平台资源 | 独立 ADR、平台范围与构建矩阵通过 |

依赖顺序为 L0 → L1 → L2；L3 和 L4 在 L1 稳定后可分别推进。L5 按实际模块切片进入，不阻塞基础客户端。L6 不进入默认排期。

## 12. 退出定义

Full.NET 只有满足以下条件才能宣称“支持多语言”：

1. 当前发布物声明的每个客户端均列出真实支持语言与未支持平台差异；
2. API、双管理端以及已发布的 uni-app/Flutter 目标使用同一规范语言和稳定错误语义；
3. 服务器发起的通知、报表或 AI 输出不会依赖随机线程文化；
4. 日期、数字、货币、时区、复数、长文本和组件库语言完成验收；
5. 缺失资源与非法语言有自动门禁；
6. 未完成的 uni-app、Flutter、业务内容或 MAUI 不得由管理端壳层的 zh-CN/en-US 成果代替。

## 13. 官方参考

- ASP.NET Core globalization and localization: https://learn.microsoft.com/aspnet/core/fundamentals/localization?view=aspnetcore-10.0
- .NET localization resources: https://learn.microsoft.com/dotnet/core/extensions/localization
- uni-app internationalization: https://uniapp.dcloud.net.cn/tutorial/i18n.html
- uni-app locale APIs: https://uniapp.dcloud.net.cn/api/ui/locale
- uni-app CLI project: https://uniapp.dcloud.net.cn/quickstart-cli.html
- Flutter internationalization: https://docs.flutter.dev/ui/internationalization
- .NET MAUI localization: https://learn.microsoft.com/dotnet/maui/fundamentals/localization?view=net-maui-10.0
- Element Plus internationalization: https://element-plus.org/en-US/guide/i18n
- Layui i18n module: https://layui.dev/docs/2/i18n/
- BCP 47 / RFC 5646: https://www.rfc-editor.org/info/rfc5646/
- HTTP semantics / RFC 9110: https://www.rfc-editor.org/rfc/rfc9110.html
