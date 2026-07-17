# Full.NET 客户端交付路线图

- 建立日期：2026-07-17
- 状态：Implementing
- 详细设计：[`../superpowers/specs/2026-07-17-multi-client-frontend-strategy-design.md`](../superpowers/specs/2026-07-17-multi-client-frontend-strategy-design.md)
- 多语言设计：[`../superpowers/specs/2026-07-17-full-stack-localization-design.md`](../superpowers/specs/2026-07-17-full-stack-localization-design.md)
- 多语言计划：[`../superpowers/plans/2026-07-17-full-stack-localization.md`](../superpowers/plans/2026-07-17-full-stack-localization.md)
- 总功能矩阵：[`adminnet-feature-parity.md`](adminnet-feature-parity.md)

## 1. 交付目标

Full.NET 同时维护两套功能范围一致的后台管理端，并按平台职责提供业务客户端：

| 轨道 | 目录 | 交付责任 | 优先级 |
|---|---|---|---:|
| A | `ui/admin` | Vue 3 主管理端，完整后台功能 | P0 |
| B | `ui/admin-layui` | Layui JS/HTML 管理端，与 Vue 后台功能对等 | P0 |
| C | `clients/uniapp` | H5、微信小程序、支付宝小程序 | P1 |
| D | `clients/flutter` | Android、iOS、Windows、macOS、Linux | P2 |
| E | `clients/maui-template` | C#/Windows 企业项目按需模板 | 决策门禁 |

P0 的两套管理端必须按同一后台模块同步开发。P1/P2 不承担完整后台功能，只实现面向终端用户和现场业务的流程。

### 当前实现快照

| 范围 | 当前状态 | 已完成 | 尚未完成 |
|---|---|---|---|
| C0 浏览器契约 | Implemented | pnpm 工作区、共享 ProblemDetails、设计令牌、Vue/原生 JS 适配、许可证清单、全仓库语言治理清单、服务端资源本地化 | OpenAPI 漂移、分页/文件契约、uni-app 与 Dart 适配 |
| C1 双管理端壳层 | Implemented | Vue/Element Plus、clean-room Layui、本地资源、登录/刷新/退出、内存令牌、CSRF、当前用户、可信租户切换、Host 返回、动态权限导航、按钮可见性、Hash 状态页、错误码/TraceId、管理壳层 `zh-CN/en-US` 自有文案、WCAG 2.2 A/AA、键盘/焦点、320 CSS px 重排、减弱动画与同场景双端 E2E | Element Plus/Day.js 与 Layui 组件语言、Accept-Language、账号偏好、Windows Edge + NVDA、200% 缩放与强制颜色人工验收 |
| C2 业务模块 | Mapped | 功能波次和双端同步门禁 | 首个 Identity/Tenancy/Organization 纵向切片 |
| C3/C4 业务客户端 | Designing | 技术路线、平台边界和多语言统一设计 | uni-app、Flutter 工程、原生资源与平台构建验证 |

“C0 浏览器契约 Implemented”只表示本计划限定的浏览器部分已实现，不代表 C0 的四类客户端退出条件已经满足。C1 的真实认证、租户、权限、管理壳层自有文案国际化、服务端错误本地化与自动可访问性流程已经通过验证；组件库语言、真实辅助技术、200% 缩放和强制颜色仍待完成，因此保持 `Implemented`，不提前标记为 `Verified`。

### 横向多语言状态

多语言是跨 C0-C5 的横向轨道，不以任意单一客户端完成代替其他端：

| 阶段 | 当前状态 | 交付 |
|---|---|---|
| L0 统一语言治理 | Implemented | 已实现 BCP 47 清单、平台映射、术语、资源 Schema 与缺键门禁 |
| L1 ASP.NET Core | Implemented | 已实现 Accept-Language、规范别名、CultureScope、模块 .resx、响应头、本地化 ProblemDetails、结构化 violations 与 Admin.NET 兼容映射 |
| L2 双管理端补齐 | Designing | Element Plus/Day.js、Layui i18n、请求 Header、账号/租户偏好 |
| L3 uni-app | Designing | Vue I18n、zh-CN↔zh-Hans 映射、pages/manifest、H5/微信/支付宝构建 |
| L4 Flutter | Designing | gen_l10n/ARB、请求语言、平台资源、移动/桌面构建 |
| L5 业务内容与异步 | Mapped | 翻译表、通知/报表、Realtime、AI 输出语言 |
| L6 MAUI | Decision Gate | 命中既有门禁后使用 .resx 和平台资源 |

## 2. 完成状态

管理端功能分别记录两个状态：

| 状态 | 含义 |
|---|---|
| `Mapped` | 已确定归属，尚未建立客户端规格 |
| `Designing` | API、页面和双端验收场景正在设计 |
| `Implementing` | 至少一个管理端已开始实现，但双端尚未全部完成 |
| `Implemented` | Vue 与 Layui 功能均已实现，尚未通过完整验收 |
| `Verified` | 双端权限、租户、错误处理、关键流程和 E2E 均通过 |
| `Not Applicable` | 经设计评审确认某一端不适用，且已有等价替代交互 |

禁止将 Vue 单端完成标记为管理端 `Implemented` 或 `Verified`。

## 3. 双管理端同步开发门禁

每个后台模块使用同一纵向切片交付：

1. 后端 API、权限码、分页、ProblemDetails 和 OpenAPI 契约先进入可测试状态；
2. 同时写出 Vue 与 Layui 的关键 E2E 场景，场景名称和业务断言保持一致；
3. 分别实现两套页面、状态管理和框架适配；
4. 对比查询、详情、创建、修改、删除、批量操作、导入导出等适用流程；
5. 两端分别执行无权限、跨租户、验证失败、并发冲突和会话失效场景；
6. 双端 E2E、OpenAPI 漂移检查和许可证检查通过后更新矩阵。

允许同一短周期内先在 Vue 或 Layui 验证复杂交互，但功能不得带着另一端欠账跨越里程碑退出门禁。

## 4. 阶段计划

### C0：公共客户端契约底座

**目标：** 让所有客户端使用同一 API、安全和错误语义。

**交付：**

- 固定 OpenAPI 输出和破坏性变更检查；
- 定义分页、ProblemDetails 扩展、文件上传下载和取消契约；
- 建立 TypeScript、原生 JavaScript、uni-app 和 Dart 客户端生成/适配入口；
- 定义浏览器、小程序、原生应用各自的认证和令牌存储策略；
- 建立共享权限码、菜单元数据、业务术语和设计令牌源；
- 建立共享 BCP 47 语言清单、回退、平台映射、错误资源完整性和术语源；
- 建立客户端依赖、图标、字体、SDK 和生成器许可证清单。

**退出条件：** 四种目标客户端都能调用同一个测试 API，并一致解析成功、验证失败、未授权、禁止访问、并发冲突和服务器错误；相同语言请求返回相同稳定 code，展示文本与 Content-Language 符合语言契约。

### C1：Vue/Layui 双管理端壳层

**目标：** 两套管理端具备相同的会话、租户、权限和导航基础。

**交付：**

- Vue 3 + TypeScript + Vite + Element Plus 工程；
- Layui 2 + HTML/CSS/原生 JavaScript 工程，核心资源本地化；
- 基于公开页面体验形成 Full.NET 原创设计令牌和后台壳层，不复制 layuiAdmin 产品资产；
- 登录、退出、刷新会话、租户选择、菜单、按钮权限、403/404/500 页面；
- ProblemDetails 统一处理、请求取消、重复提交保护和 TraceId 展示；
- 两套管理端的响应式布局、主题令牌、国际化入口和可访问性基线；
- Element Plus/Day.js 与 Layui 组件语言、Accept-Language 和账号语言偏好同步；
- Vue/Layui 相同场景的 Playwright E2E。

**退出条件：** 两端都能完成登录、租户切换、权限菜单和退出流程；Layui 生产产物不依赖 Vue/React 等 SPA 运行时。

### C2：双管理端核心模块对标

后端模块按照下表逐波交付。每一行都要求 Vue 与 Layui 同步进入相同完成状态。

| 波次 | 后台能力 | Vue | Layui | 后端依赖 | 相对规模 |
|---|---|---|---|---|---|
| C2.1 | 用户、角色、菜单、按钮权限 | Mapped | Mapped | Identity | L |
| C2.1 | 租户、套餐、租户切换 | Implementing | Implementing | Tenancy | L |
| C2.1 | 组织、职位、数据范围 | Mapped | Mapped | Organization | L |
| C2.2 | 字典、系统配置、枚举元数据 | Mapped | Mapped | Settings | M |
| C2.2 | 访问、操作、异常和审计日志 | Mapped | Mapped | Auditing | M |
| C2.2 | 在线用户、公告、站内通知 | Mapped | Mapped | Realtime + Notifications | L |
| C2.3 | 文件、对象存储和预览 | Mapped | Mapped | Files + Storage Provider | L |
| C2.3 | 任务调度、执行记录和重试 | Mapped | Mapped | Jobs | L |
| C2.3 | 代码生成、模板和生成记录 | Mapped | Mapped | CodeGeneration | XL |
| C2.4 | 工作台、统计和监控入口 | Mapped | Mapped | Dashboard Contracts | L |

规模只表示相对拆分需要，不是工期承诺：`M` 应拆成至少一个可独立验收切片，`L` 至少两个，`XL` 必须先建立独立设计和多阶段计划。

**退出条件：** 纳入 Full.NET 1.0 的后台功能全部达到双端 `Verified`；后续官方模块继续复用同一门禁。

### C3：uni-app H5/微信/支付宝基础客户端

**目标：** 用一套 Vue 3 + TypeScript 业务代码覆盖三个目标。

**交付顺序：**

1. 工程、环境配置、路由、Pinia、`uni.request` 适配和 ProblemDetails；
2. Vue I18n、`uni.getLocale/setLocale/onLocaleChange`、`zh-CN ↔ zh-Hans` 映射、pages/manifest 资源和请求语言；
3. 启动页、普通登录、租户选择、首页、个人中心、账号安全、错误与离线页；
4. H5 Cookie/CSRF 流程；
5. 微信小程序 `code` 登录适配和后端 Provider 对接；
6. 支付宝小程序授权码登录适配和后端 Provider 对接；
7. 文件、扫码、分享、订阅消息和支付按独立 Provider 规格增加；
8. 三目标分别构建、真机/开发者工具多语言冒烟和发布清单。

**退出条件：** H5、微信小程序、支付宝小程序分别完成构建、登录、租户、核心 API、会话失效和错误展示验证。

### C4：Flutter 原生移动与 PC 桌面

**目标：** 一套 Dart 代码覆盖原生移动端和三类桌面系统。

**交付顺序：**

1. 工程、环境、路由、主题、OpenAPI Dart 客户端、ProblemDetails 和安全存储；
2. `gen_l10n/ARB`、`flutter_localizations`、请求语言、账号偏好和各平台应用元数据；
3. OAuth 2.0/OIDC Authorization Code + PKCE、租户选择、首页、个人中心和错误页；
4. Android 与 Windows 本地开发和打包基线；
5. 在 macOS 构建节点验证 iOS、macOS、签名和公证；
6. 在 Linux 构建节点验证 Linux 桌面包；
7. 键鼠、可调整窗口、文件、打印、托盘、深链和自动更新按平台适配；
8. 通知、Realtime、支付和设备能力按独立功能规格增加。

**退出条件：** 每个声明支持的平台都有对应构建节点、安装包、签名策略、登录/租户/API 冒烟和升级回滚说明。没有完成构建验证的平台不得在发布说明中宣称支持。

### C5：双管理端生成与高级能力

**目标：** 把双管理端维护成本控制在可持续范围。

**交付：**

- 同一 `FullNetSchema` 同时生成 Vue 与 Layui CRUD；
- 生成两端的 API 调用、列表、表单、详情、权限码和 E2E 骨架；
- 为 Realtime、文件、导入导出、打印、表单设计器、大屏和 AI/Agent 工作台分别建立双端适配；
- 生成器快照和编译/E2E 测试阻止模板漂移。

**退出条件：** 同一示例模块生成两套可运行管理页面，人工只补充业务特有交互，重新生成不会覆盖手写扩展。

### C6：.NET MAUI 决策门禁

只有实际合同、团队或设备集成需求明确命中以下条件时才执行：

- 必须使用 C#/.NET；
- 平台范围接受 Android、iOS、Windows、macOS，不要求官方 Linux 支持；
- Windows 原生或已有 MAUI 资产的收益足以覆盖第二套客户端维护成本；
- 已明确负责人、CI 构建节点和功能维护范围。

命中后先建立架构决策记录和独立实现计划，不能复制 Flutter 全量范围作为默认承诺。

## 5. 后端里程碑映射

| 后端里程碑 | 客户端必须完成 |
|---|---|
| M2 | C0 契约底座、C1 双管理端壳层、C2.1 对应核心模块 |
| M3 | C2.2/C2.3 双管理端、C3 uni-app 基础客户端、双管理端代码生成首个样例 |
| M4 | Full.NET 1.0 范围内双管理端全部验证、uni-app 三目标构建、客户端安全/许可证/E2E 加固 |
| M5+ | C4 Flutter、C5 高级能力、后续 Admin.NET 模块双管理端对标、按需 C6 MAUI |

Flutter 作为明确路线进入 M5+，但 C0 的 Dart 契约验证可以提前完成，避免后端 API 在 1.0 后才发现不适合原生客户端。

## 6. CI 与质量门禁

| 变更范围 | 必须运行 |
|---|---|
| OpenAPI/公共模型 | 全部在维护客户端的生成和契约测试 |
| 语言清单、错误码或资源 | 资源 Schema/缺键检查、后端双语言 API、全部在维护客户端语言契约 |
| 后台模块 | Vue + Layui 类型/静态检查、单元测试、生产构建、对应双端 E2E |
| Vue 独有基础设施 | Vue 检查 + 与共享契约相关的 Layui 冒烟 |
| Layui 独有基础设施 | Layui 检查 + 与共享契约相关的 Vue 冒烟 |
| uni-app | H5、`mp-weixin`、`mp-alipay` 分别构建和平台适配测试 |
| Flutter | `flutter analyze`、单元/Widget 测试、受影响平台构建 |
| 依赖与静态资源 | 锁文件、漏洞、许可证、资源来源和 CSP 检查 |

## 7. 主要风险与控制

| 风险 | 控制措施 |
|---|---|
| 双管理端进度逐渐分叉 | 同一模块双端切片、双状态列、双端 E2E 作为统一退出门禁 |
| Layui 代码退化为全局脚本 | 核心能力模块化、禁止内联业务脚本、静态检查和单元测试 |
| 为追求一致而复制 UI 组件 | 只共享契约、令牌和场景，不共享框架 UI 源码 |
| 多端认证采用最低共同标准 | 浏览器、小程序、原生应用分别采用适合平台的令牌策略 |
| uni-app 条件编译散落 | 平台差异集中在 `platform/`，共享页面禁止直接访问平台专属 API |
| Flutter 插件缺少桌面实现 | 引入前建立平台支持和许可证矩阵，允许平台适配替代 |
| 各端语言清单、错误文本或术语漂移 | 共享治理清单、稳定 code、平台原生资源和跨资源 CI；禁止业务逻辑比较翻译文本 |
| Worker/通知并行处理串语言 | 每项后台渲染显式 CultureScope/Locale，离开作用域恢复并执行并发测试 |
| MAUI 与 Flutter重复建设 | 决策门禁、独立维护责任和范围，不默认创建模板 |
| Admin.NET 或 layuiAdmin 参考代码污染 MIT 发布 | 采用洁净室独立实现，只参考通用体验；layuiAdmin 默认禁止复制源码/资产，任何直接复用都必须先取得允许公开 MIT 再发布的书面授权 |

## 8. 下一批可执行计划

1. 先执行 L0/L1：建立仓库语言治理、ASP.NET Core 请求本地化、本地化 ProblemDetails 与结构化 violations；
2. 执行 L2：补齐 Element Plus/Day.js、Layui 组件语言、Accept-Language 和账号/租户偏好；
3. 在 Windows Edge + NVDA、200% 缩放和强制颜色模式下完成人工验收，使 C1 具备进入 `Verified` 的剩余证据；
4. 为 Identity 用户/角色/菜单、Tenancy 租户/套餐 CRUD 和 Organization 建立首批双管理端业务切片计划；当前只把可信租户上下文切换视为已验证基础，不把完整 C2.1 误标为完成；
5. L1 契约稳定后执行 L3 uni-app 三目标计划；
6. OpenAPI Dart 契约验证完成且具备目标构建节点后执行 L4 Flutter 计划；
7. 每个计划结束时同步更新本路线图的 Vue/Layui 独立状态列和 L0-L6 状态。
