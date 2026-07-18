# Full.NET 多客户端前端策略设计

- 日期：2026-07-17
- 状态：已确认
- 决策方式：依据项目所有者“后续自动确认（按推荐方案来）”的持续授权，采用推荐方案
- 适用范围：Vue/Layui 双管理端、H5、小程序、原生移动端与 PC 桌面端

## 1. 目标

Full.NET 需要同时满足产品长期演进与项目快速交付，因此客户端不能只覆盖一个 Vue 管理后台。项目明确要求 Vue 与 Layui 两套管理端同步建设并覆盖相同的后台管理功能；面向终端用户的 uni-app 与 Flutter 则按各自平台职责交付，不复制后台全部功能。本设计建立“双管理端等功能范围 + 多平台业务客户端”体系：

1. `ui/admin` 是长期产品化的主管理端；
2. `ui/admin-layui` 是无需 SPA 框架、适合传统部署和快速二开的完整管理端；
3. `clients/uniapp` 使用一套代码覆盖 H5、微信小程序和支付宝小程序；
4. `clients/flutter` 使用一套代码覆盖原生移动端及 Windows、macOS、Linux 桌面端；
5. .NET MAUI 不进入默认全量实现，只在 C# 技术栈或 Windows 企业交付确有需求时提供可选模板。

Vue 与 Layui 共享后台功能清单和完成门禁，但不共享具体 UI 组件源码。所有客户端共享 API 契约、安全模型、权限语义、错误模型和设计令牌。这样可以减少业务协议分叉，同时允许各平台采用符合自身交互习惯的实现。

## 2. 方案比较与结论

### 2.1 备选方案

| 方案 | 优点 | 主要问题 | 结论 |
|---|---|---|---|
| 只保留 Vue 管理端 | 建设和维护成本最低 | 无法覆盖小程序、原生应用、桌面和无构建轻量交付 | 不采用 |
| 所有客户端全部全量对标 | 技术选择最多 | 后台功能在小程序、移动端、桌面端无差别复制，成本失控 | 不采用 |
| Vue/Layui 双管理端等功能范围，其他客户端按平台职责交付 | 同时支持现代 SPA 和传统 JS/HTML 项目快速交付 | 每个后台模块需要两套 UI 实现和双端 E2E | 采用 |

### 2.2 最终技术选择

| 交付形态 | 目录 | 技术栈 | 产品定位 |
|---|---|---|---|
| 主管理端 | `ui/admin` | Vue 3 + TypeScript + Vite + Element Plus + Art Design Pro 基线 | 完整后台、复杂交互、长期产品化 |
| JS/HTML 管理端 | `ui/admin-layui` | Layui 2 + 独立实现的 HTML/CSS/原生 JavaScript 后台壳层 | 完整后台、快速交付、轻依赖、传统部署 |
| H5 与小程序 | `clients/uniapp` | uni-app + Vue 3 + TypeScript + uni-ui | H5、微信小程序、支付宝小程序统一业务客户端 |
| 原生移动与桌面 | `clients/flutter` | Flutter 3.44 + Dart + Material 3/Cupertino | Android、iOS、Windows、macOS、Linux |
| C# 原生模板 | `clients/maui-template`（按需） | .NET MAUI | C# 团队、Windows 优先或企业原生项目 |

`.NET MAUI` 目录在真实项目通过决策门禁前不创建，防止未被使用的模板持续消耗升级和测试成本。

## 3. 主管理端：Vue 3

### 3.1 已确定基线

`ui/admin` 采用 Vue 3、TypeScript、Vite 和 Element Plus，并采用 MIT [Art Design Pro](https://github.com/Daymychen/art-design-pro) 作为管理壳层基线。这与本地 Admin.NET.Pro `Web` 的基础路线一致，方便对照菜单、权限、表格、表单和业务流程，但 Full.NET 不复制 Admin.NET.Pro 的源码、目录结构或全部依赖。

Art Design Pro 以固定上游提交选择性迁入，而不是把 Full.NET 变成其后端示例的换皮版本。可采用布局、主题、菜单外观、标签页、通用表格/表单交互和无业务耦合组件；ECharts 作为标准图表引擎纳入，但必须按图表类型模块化注册、路由级懒加载并提供数据表/文本摘要。必须保留 Full.NET 已验证的内存 Access Token、HttpOnly Refresh Cookie、CSRF、精确 CORS、ProblemDetails、租户切换、权限导航白名单、账号语言偏好和 OpenAPI 契约。Mock、演示业务、品牌图像、请求封装、认证与动态路由不能直接覆盖现有实现。

基础依赖限于：

- Vue Router：路由和权限导航；
- Pinia：会话、租户、权限与跨页面状态；
- OpenAPI 生成的 TypeScript 客户端：后端契约访问；
- `@microsoft/signalr`：Realtime 模块启用后的浏览器实时通信；
- Element Plus：通用管理端组件。
- Art Design Pro：主管理壳层、主题、布局和可复用交互基线；不是后端协议来源。
- Apache ECharts：Vue 管理端标准图表引擎；模块化、懒加载并由 Full.NET 统一主题和无障碍替代内容。
- Tiptap Core：Vue/Layui 默认富文本引擎；只使用 MIT Core/开源扩展，付费 Pro 扩展不进入默认框架。

VXE Table、低代码设计器、大屏组件以及 Tiptap Profile 之外的富文本扩展等重型依赖，只在对应功能规格确认后引入；禁止为了“以后可能使用”预装 Admin.NET.Pro 的完整依赖集合。

### 3.2 功能范围

主管理端是 Admin.NET 功能对标的默认客户端，按后端模块逐步覆盖登录、租户、用户、角色、菜单、组织、字典、配置、审计、文件、通知、任务、代码生成以及后续官方模块。

## 4. JS/HTML 完整管理端：Layui

### 4.1 定位

Layui 官方定位是面向浏览器环境、以原生 HTML/CSS/JavaScript 为基础的模块化 Web UI 库，支持直接引入资源且不要求构建工具。Full.NET 将其作为第二套完整管理端：运行时保持轻量，但功能范围与 Vue 管理端一致。复杂状态通过清晰的原生 JavaScript 模块拆分处理，不能为了实现完整功能而暗中引入另一套 SPA 框架。

`ui/admin-layui` 遵循以下原则：

- 使用固定且经过测试的 Layui 2.x 版本，依赖资源随项目发布；
- 生产环境默认不从公共 CDN 加载核心脚本和样式；
- 使用现代原生 JavaScript 模块组织核心能力，不引入 Vue、React 或另一套 SPA 运行时；
- 可以使用 Node.js 完成质量检查和可选打包，但浏览器运行时不依赖 Node.js；
- 通过同一 `/api/v1` 和 ProblemDetails 契约访问后端。

用户指定的 [`layuiAdmin`](https://dev.layuion.com/themes/layuiAdmin) 作为布局、导航和后台功能体验参考。该主题页面明确说明其主要面向既有授权用户、自 2021 年起不再面向新用户获取，并禁止公开传播产品源文件，因此它不能作为 Full.NET MIT 发布物的代码依赖或模板来源。Full.NET 必须基于 MIT 的 Layui 核心组件独立实现后台壳层，禁止复制 layuiAdmin 的源码、CSS、图片、字体、模板和其他产品资产。只有取得允许公开源文件并以 MIT 再发布的明确书面授权后，才可以重新评估直接复用。

独立实现采用洁净室设计原则：可以参考深色侧栏、顶部导航、多标签工作区、卡片容器、紧凑表格等通用中后台范式，但必须重新定义 Full.NET 的设计令牌、布局尺寸、图标组合、页面结构、CSS 类名、JavaScript 模块和交互细节。设计过程以公开页面观察和 Full.NET 自身需求为输入，不获取非公开主题包，不从浏览器复制样式规则，不使用截图切图，也不以像素级相似作为验收目标。视觉验收关注信息层级、操作效率、一致性、响应式和可访问性。

建议结构：

```text
ui/admin-layui/
├── index.html
├── assets/
│   └── vendor/layui/
├── js/
│   ├── core/
│   │   ├── http.js
│   │   ├── auth.js
│   │   ├── router.js
│   │   ├── permissions.js
│   │   └── problem-details.js
│   ├── generated/
│   └── pages/
└── tests/
```

### 4.2 双管理端功能对等规则

Layui 端与 Vue 端不追求像素一致，也不复制 Vue 组件，但两者必须覆盖同一后台功能清单。登录、租户切换、菜单/按钮权限、用户、角色、组织、字典、配置、审计、文件、通知、任务、代码生成，以及后续纳入管理后台的官方模块都采用双端同步交付。

每个后台功能的客户端状态单独记录为 `Vue` 与 `Layui` 两列。只有两端都满足以下条件，功能才可以标记为客户端 `Verified`：

1. 页面入口、查询、创建、修改、删除、批量操作和导入导出等适用流程一致；
2. 权限标识、租户隔离、数据范围和敏感操作确认一致；
3. API、分页、验证、ProblemDetails 和稳定错误码处理一致；
4. 关键 E2E 场景分别在两端通过；
5. 任何因技术平台无法实现的差异都有显式设计记录和等价替代交互。

开发节奏采用同一后台模块的双端纵向切片，不允许先长期完成 Vue 全量功能，再集中补写 Layui。允许在一个短周期内先实现其中一端以验证交互，但同一功能进入里程碑验收前必须补齐另一端。

## 5. H5、微信小程序与支付宝小程序：uni-app

### 5.1 平台范围

`clients/uniapp` 使用 uni-app Vue 3 + TypeScript，一套业务代码首期构建三个目标：

- H5；
- 微信小程序 `mp-weixin`；
- 支付宝小程序 `mp-alipay`。

默认 UI 组件库采用 DCloud 官方 [uni-ui](https://github.com/dcloudio/uni-ui)，通过 npm 包依赖和 easycom 使用，不复制组件源码。原版 uView 2 不进入默认依赖；只有 uni-ui 无法满足两个以上真实业务模块、候选库明确支持当前 uni-app Vue 3/H5/微信/支付宝版本、许可证和体积门禁通过时，才能用 ADR 引入少量补充组件。禁止同时以 uni-ui 和另一整套组件库作为基础层。

uni-app 同样具备 App 构建能力，但 Full.NET 默认不使用它输出原生 App，避免与 Flutter 的职责重叠。后续只有在特定项目明确选择 uni-app App 时，才建立独立的架构决策记录。

### 5.2 代码组织

共享页面、领域状态和验证逻辑不得直接调用平台 API。微信登录、支付宝登录、支付、分享、订阅消息、文件选择等差异必须集中在平台适配层，并通过条件编译选择实现。

建议结构：

```text
clients/uniapp/src/
├── api/
│   ├── generated/
│   └── transport/
├── features/
├── platform/
│   ├── common/
│   ├── h5/
│   ├── mp-weixin/
│   └── mp-alipay/
├── stores/
└── pages/
```

基础页面包括启动配置、登录/平台登录、租户选择、首页、导航、个人中心、账号安全、统一错误页、离线提示和通知入口占位。

### 5.3 后端调用和安全

- 统一使用 `/api/v1`、标准 HTTP 状态码和 ProblemDetails；
- OpenAPI 生成数据类型和请求函数，再由 `uni.request` 传输适配器执行；
- H5 同源模式下 Access Token 保存在内存，Refresh Token 使用 `HttpOnly + Secure + SameSite` Cookie，并启用 CSRF 防护；
- 小程序使用平台登录 `code` 与后端交换 Full.NET 短期访问令牌，平台密钥只保存在后端；
- 客户端提交的租户标识、OpenID、用户标识和角色均不能作为可信授权依据；
- 微信和支付宝的登录、支付、回调签名由独立 Provider 完成，客户端只发起流程和展示结果。

H5、微信和支付宝构建产物必须分别测试，禁止以 H5 通过推断两个小程序也可用。

## 6. 原生 App 与 PC 桌面：Flutter / .NET MAUI

### 6.1 技术比较

| 维度 | Flutter | .NET MAUI |
|---|---|---|
| 语言 | Dart | C# |
| 官方移动端 | Android、iOS | Android、iOS |
| 官方桌面端 | Windows、macOS、Linux | Windows、macOS（Mac Catalyst） |
| Linux 桌面 | 官方支持 | 无官方目标 |
| 团队复用 | 需要掌握 Dart/Flutter | 可复用 .NET/C# 能力 |
| UI 一致性 | 自绘体系，一致性较强 | 更接近平台原生控件和平台差异 |
| 适用重点 | 移动 + 多桌面统一产品 | C# 团队、Windows/企业原生项目 |

### 6.2 推荐结论

默认建设 `clients/flutter`，承担 Android、iOS、Windows、macOS 和 Linux 原生客户端。用户明确要求 PC 桌面覆盖时，Flutter 的官方平台范围比 .NET MAUI 更完整，维护一套默认实现的综合成本更低。

.NET MAUI 保留为 Provider/Template 级选择。当实际客户满足以下任一条件时，才单独建立 `clients/maui-template` 规格和计划：

- 团队只接受 C#/.NET，且愿意放弃 Linux 桌面；
- Windows 原生集成深度明显高于跨平台诉求；
- 需要复用已有 MAUI 控件、设备能力或企业 SDK；
- 合同明确指定 .NET MAUI。

不同时维护 Flutter 和 MAUI 的全功能对等版本。

### 6.3 Flutter 架构边界

- 基线锁定 Flutter 3.44 稳定系列，默认开启 Material 3；
- iOS 使用 Cupertino 官方组件或自适应封装保留平台行为；
- Full.NET 颜色、字体、间距、圆角、状态和动效令牌映射为 `ThemeExtension`，业务页面不硬编码品牌样式；
- 不引入第三方整套 UI 框架；数据网格、图表、编辑器等能力按真实模块逐项审查，不反向决定应用主题；
- 采用按业务功能组织的轻量分层结构；
- 使用 OpenAPI 生成 Dart 数据模型和 API 客户端；
- 使用 OAuth 2.0/OIDC Authorization Code + PKCE 和系统浏览器登录；
- Token 只存入平台安全存储，禁止明文文件和普通首选项存储；
- 桌面专属的键鼠、窗口、多窗口、文件、打印、托盘、签名与更新能力封装为平台适配器；
- 首期不承诺离线优先，只实现有界缓存、断网提示和必要的请求重试；
- H5 不由 Flutter Web 承担，防止和 uni-app 重复。

iOS 和 macOS 构建、签名仍需要 macOS/Xcode 环境；Windows、macOS、Linux 的安装包签名和自动更新必须分别验收。

## 7. 跨客户端共享契约

### 7.1 OpenAPI 单一事实来源

后端 OpenAPI 文档是所有 HTTP 客户端的契约来源，生成结果按平台分开：

| 客户端 | 生成结果 | 传输适配 |
|---|---|---|
| Vue | TypeScript 类型和请求客户端 | 标准 Fetch 或经批准的 HTTP 客户端 |
| Layui | 原生 JavaScript 模块及 JSDoc 类型 | Fetch |
| uni-app | TypeScript 类型和请求描述 | `uni.request` |
| Flutter | Dart 模型和请求客户端 | Dart HTTP 传输 |
| MAUI（按需） | C# 客户端 | `HttpClient` |

生成器必须保留稳定错误 `code`、`traceId`、验证错误集合、分页模型和取消能力。客户端业务逻辑只能依赖错误 `code`，不得依赖本地化 `title`、`detail` 或提示文本。

### 7.2 认证、租户和权限

所有客户端使用同一权限标识和菜单契约。后端始终重新验证租户、用户和数据范围，客户端隐藏按钮只改善体验，不能代替服务端授权。

浏览器、小程序和原生应用使用不同的令牌获取与安全存储方式，不建立一个最低安全标准的“万能登录工具”。

### 7.3 Realtime

Realtime 模块落地前，不要求每个客户端预装实时通信依赖。启用后：

- Vue 和 Layui 使用官方 SignalR JavaScript 客户端；
- Flutter 使用经许可证和维护状态审查的 SignalR/Dart 适配器，若不满足要求则使用受控 WebSocket/SSE 协议适配；
- uni-app 对需要实时能力的平台使用 WebSocket 适配器，不能假设所有小程序均完整兼容浏览器 SignalR 客户端；
- 客户端只消费 Realtime Contracts，不感知服务端 `IHubContext`。

### 7.4 多语言与本地化

多语言采用“统一治理、平台原生实现”：仓库共享 BCP 47 语言清单、回退、术语、错误码和完整性检查，但 ASP.NET Core、Vue/Layui、uni-app、Flutter 与按需 MAUI 分别使用适合自身构建和运行时的资源机制。

当前 Vue/Layui 已在 `@fullnet/admin-i18n` 自有文案之上完成 Element Plus/Day.js、Layui 公开组件语言、逐请求 `Accept-Language` 和账号偏好原子同步；ASP.NET Core 已完成 `Content-Language`、本地化 ProblemDetails 与账号/租户偏好基础。后续仍必须分别交付 uni-app 的 Vue I18n 和平台资源、Flutter 的 `gen_l10n/ARB`，以及业务翻译表、通知、报表、Realtime 与 AI 的显式语言边界。完整决策与实施顺序见[全栈多语言与本地化设计](2026-07-17-full-stack-localization-design.md)。

## 8. 设计系统与代码生成

跨客户端共享颜色、间距、字体、状态色、权限标识和业务术语等语义令牌。Vue、Layui、uni-app、Flutter 分别把令牌编译或映射为自身格式，不共享组件实现。

CodeGeneration 把 Vue 与 Layui CRUD 同时作为完整后台生成目标。uni-app 和 Flutter 的生成能力分阶段增加：

1. 先为全部客户端生成 API 客户端和模型；
2. 同步生成 Vue 与 Layui 的列表、表单、详情、权限和测试骨架；
3. 再为 uni-app 与 Flutter 生成已经稳定的业务页面骨架；
4. 未形成稳定模式前不生成平台支付、设备能力或复杂交互代码。

## 9. 测试与发布矩阵

| 客户端 | 必须执行的验证 |
|---|---|
| Vue | 类型检查、单元测试、生产构建、核心 Playwright E2E、OpenAPI 契约漂移检查 |
| Layui | JavaScript 静态检查、单元测试、静态资源完整性、核心 Playwright E2E、CSP 验证 |
| uni-app | 类型检查、单元测试、H5/微信/支付宝分别构建、各平台登录与 API 冒烟测试 |
| Flutter | `flutter analyze`、单元/Widget 测试、Android/iOS/Windows 构建，macOS/Linux 按阶段补齐 |
| MAUI（按需） | .NET 构建/测试、Android/iOS/Windows/macOS 目标矩阵，不声明 Linux 支持 |

公共 API 发生破坏性变化时，必须先让全部在维护客户端的契约检查失败，再显式升级生成客户端。不能静默修改序列化属性、分页或 ProblemDetails 扩展字段。

## 10. 许可证和供应链

- Full.NET 最终发布仍为 MIT；
- Layui 官方为 MIT，可在保留其许可证和声明后随 JS/HTML 管理端分发；
- layuiAdmin 静态主题不是 Layui 核心库的 MIT 资产，只能作为公开页面的功能/交互参考，默认禁止复制和再分发；
- uni-app 核心仓库采用 Apache-2.0，但其仓库和插件市场可能包含不同许可证，必须逐项审计实际分发文件；
- Flutter、.NET MAUI、组件、图标、字体、SDK、生成器和插件必须登记到第三方清单；
- Admin.NET.Pro 的本地二开商用授权不等于允许把复制内容以 MIT 重新发布，Full.NET 只把它作为功能和交互验收参考；
- 所有客户端锁定依赖版本、提交锁文件并执行依赖和许可证扫描。

Art Design Pro 与 Tiptap Core 当前采用 MIT，选择性迁入/依赖使用时保留其版权与许可证；uni-ui 与 Apache ECharts 当前采用 Apache-2.0，发布物必须保留许可证、修改和 NOTICE 要求。Tiptap Pro 扩展需要商业订阅，不进入默认发布物。Flutter SDK 及其组件、图标、字体与任何后续插件仍须按实际锁定版本登记。规划选型不等于依赖已经进入发布物，只有真正引入后才更新 `THIRD-PARTY-NOTICES`。

## 11. 分阶段交付

| 阶段 | 交付内容 | 退出条件 |
|---|---|---|
| C0：契约底座 | OpenAPI 多客户端生成、ProblemDetails、认证/租户契约、设计令牌 | 至少 TypeScript、JavaScript、uni-app、Dart 四种客户端契约测试通过 |
| C1：双管理端底座 | Vue/Layui Shell、登录、租户、权限、共享契约测试 | 两端核心壳层 E2E 通过，Layui 无 SPA 运行时 |
| C2：双管理端模块对标 | 按后端模块同步实现 Vue 与 Layui 页面 | 每个纳入范围的模块双端 E2E 通过 |
| C3：uni-app | H5、微信、支付宝基础应用和平台登录适配 | 三目标分别构建并完成 API 冒烟 |
| C4：Flutter | 移动与桌面基础应用，先 Android/iOS/Windows，再 macOS/Linux | 对应平台安装包、登录、租户和 API 冒烟通过 |
| C5：增强能力 | Realtime、通知、支付、文件、双管理端代码生成和业务客户端 | 每项按独立规格验收 |
| C6：MAUI 决策门禁 | 只有真实合同/团队需求命中时建立模板 | 架构决策记录明确平台范围与维护责任 |

阶段编号独立于后端 M0-M5，用于表达同一后端里程碑内各客户端的成熟度。详细排期和依赖关系见 `docs/roadmap/client-delivery-roadmap.md`。

## 12. 验收标准

本设计完成实施后应满足：

1. Vue 与 Layui 覆盖同一后台功能清单并同步验收，uni-app 与 Flutter 不重复承担完整后台；
2. 每个客户端都从同一 OpenAPI 契约生成或验证 API 模型；
3. 所有客户端默认使用标准 HTTP 与 ProblemDetails，不依赖 Admin.NET 统一包络；
4. H5、微信、支付宝各自通过构建和登录/API 冒烟测试；
5. Flutter 的已声明移动/桌面平台分别完成构建和安装验证；
6. 服务端权限和租户隔离不依赖客户端行为；
7. 依赖、字体、图标、SDK 和复制资产全部完成许可证登记；
8. 每个后台模块同时记录 Vue/Layui 状态，不允许以单端完成代表双端完成；
9. MAUI 未命中决策门禁时不产生需要长期维护的第二套原生客户端。

## 13. 官方参考资料

- [Layui 2 官方文档](https://layui.dev/docs/2/)
- [layuiAdmin 静态主题说明与授权限制](https://dev.layuion.com/themes/layuiAdmin)
- [uni-app 官方文档](https://uniapp.dcloud.net.cn/)
- [uni-app 官方仓库](https://github.com/dcloudio/uni-app)
- [Flutter 官方支持平台](https://docs.flutter.dev/reference/supported-platforms)
- [.NET MAUI 官方支持平台](https://learn.microsoft.com/dotnet/maui/supported-platforms?view=net-maui-10.0)
