# Full.NET 客户端 UI 框架设计

- 日期：2026-07-18
- 状态：已批准
- 决策来源：项目所有者确认 Vue 采用 Art Design Pro 基线、uni-app 采用 uni-ui、Flutter 采用官方 Material 3 + Cupertino
- 当前实现边界：本文是选型与迁移规格，不代表三个 UI 基线均已集成

## 1. 最终选择

| 客户端 | 默认 UI 基线 | 使用方式 | 当前状态 |
|---|---|---|---|
| Vue 主管理端 | Art Design Pro + Element Plus | 固定 MIT 上游提交，选择性迁入壳层、主题、布局和通用交互 | 已选型，尚未迁入 |
| Vue 图表 | Apache ECharts 6.1 | `echarts/core` 模块化注册、异步加载和 Full.NET 主题 | 已选型，尚未引入 |
| 双管理端富文本 | Tiptap Core 3.28 | Vue/Core 双 Adapter + 服务端 HTML 净化 | 已选型，尚未引入 |
| Layui 管理端 | Layui 2 | Full.NET clean-room 独立实现，功能与 Vue 对等 | 已实现壳层 |
| uni-app | DCloud uni-ui | npm 依赖 + easycom，作为唯一默认基础组件库 | 已选型，尚未引入 |
| Flutter | Flutter 3.44 Material 3 + Cupertino | 官方组件 + `ThemeExtension` + 自适应封装 | 已选型，工程尚未创建 |

## 2. Vue：采用 Art Design Pro，但不替换 Full.NET 协议层

### 2.1 采用范围

Art Design Pro 作为 Vue 管理端真正的框架基线，而不只是截图参考。允许迁入：

- 应用布局、侧边栏、顶栏、标签页和面包屑；
- 主题切换、暗色模式、响应式断点和页面转场；
- 与业务无关的表格、表单、搜索、状态和反馈交互；
- 经可访问性、许可证和依赖检查通过的通用组件。

### 2.2 Full.NET 必须继续拥有的能力

以下能力不得使用模板内置实现覆盖：

- `packages/client-contracts` 中的 ProblemDetails、Identity、Tenancy 和权限契约；
- 内存 Access Token、HttpOnly Refresh Cookie、CSRF、精确 CORS 和单次 Refresh 协调；
- 服务端导航到本地组件/路由的精确白名单；
- `/api/v1/me`、账号语言偏好、租户上下文和标准 HTTP 状态码；
- Vue/Layui 同场景 E2E、真实后端安全 E2E 和本地化资源治理。

Art Design Pro 的 Mock、axios 封装、认证、持久化 Token、演示接口、动态路径组件加载和后端响应包络不得进入上述边界。

### 2.3 依赖控制

首个审计基线固定为上游提交 `f3aaf58eec1a0e988f162352c33862327a484f95`。不整包复制其依赖表；ECharts 作为批准的标准图表引擎单独锁定，Art Design Pro 自带的 wangEditor 不随模板迁入。`axios`、xlsx、视频、二维码、拖拽、压缩和开发工具等依赖只有在真实模块规格命中时单独评审。现有 Vue、Element Plus、Pinia、Vue Router、Vite、Vitest 和 TypeScript 版本继续由 Full.NET 锁文件治理。

导入代码保留上游 MIT 版权与许可证，并登记来源提交、原始路径、目标路径和修改说明。演示图片、Logo、品牌名称、字体和第三方图标不因源码 MIT 自动获得可分发结论，必须单独审计或替换。

### 2.4 图表：Apache ECharts

Vue 管理端默认采用 `echarts@6.1.0`。只通过 `echarts/core` 注册实际使用的 Chart、Component 和 Renderer，图表所在路由/组件异步加载；禁止为了一个首页统计图直接导入完整 `echarts` 包。`FullNetChart` 统一消费设计令牌、语言、时区、减弱动画和 ResizeObserver，并为关键数据提供表格或文本摘要。图表配置不得接受服务端可执行 JavaScript 字符串。

Apache ECharts 为 Apache-2.0，实际引入时登记许可证、NOTICE 和内含第三方子组件声明。

### 2.5 富文本：Tiptap Core

默认富文本引擎采用 `@tiptap/vue-3@3.28.0`、`@tiptap/starter-kit@3.28.0` 和按需 MIT 扩展，不采用 Art Design Pro 自带的 wangEditor 作为隐式默认。Tiptap Core 为 Headless，Vue 使用 Vue Adapter，Layui 使用 Core/DOM Adapter；两端共享允许的格式、链接、图片和 HTML 契约，不共享框架 UI。

首期持久化服务端白名单净化后的 HTML，避免把编辑器私有 JSON 变成公共永久契约。服务端以自有 `IRichTextSanitizer` 隔离实现，首个候选实现固定为 MIT `HtmlSanitizer@9.0.892`，并采用显式收紧的标签、属性和协议白名单，不依赖其默认集合。图片、附件和视频引用必须先进入 Files API，内容只保存稳定资源标记，不保存临时签名 URL；禁止 Base64/Data URL 和未经授权的远程资源。服务端必须再次净化标签、属性、URL 协议和 CSS；客户端净化只能改善体验，不能成为安全边界。Tiptap Pro 扩展需要订阅，默认禁止进入 MIT 框架；协作、评论、版本和 AI 编辑另建规格。

富文本能力不属于 C1 管理壳层退出条件，必须等 Files 与 Notifications 的公告/站内通知真实切片进入开发时纵向落地。只有服务端净化、媒体租户授权、Vue/Layui Adapter、双数据库和真实后端 E2E 全部通过后才可标记为 `Verified`。

## 3. uni-app：uni-ui 作为唯一默认基础库

### 3.1 选择理由

uni-ui 与 DCloud/uni-app 工具链同源，覆盖 H5、微信小程序和支付宝小程序，适合 Full.NET 当前 Vue 3 CLI 项目。首个依赖版本锁定为 `@dcloudio/uni-ui@1.5.12`，使用 npm 包和 easycom，不复制组件源码。

### 3.2 边界

- uni-ui 负责表单、列表、弹层、反馈和基础数据展示；
- `uni.request`、ProblemDetails、语言偏好和平台登录继续由现有 Full.NET 适配层负责；
- Full.NET 设计令牌通过 `src/uni.scss` 和轻量适配样式映射，不修改 uni-ui 源文件；
- 原版 uView 2 不进入默认依赖；禁止 uni-ui 与另一整套组件库同时成为基础层。

只有两个以上真实业务模块都存在相同组件缺口，且候选组件明确通过 Vue 3、H5、微信、支付宝、许可证、包体积和可访问性验证时，才能通过 ADR 引入补充库。优先引入单一组件，不引入第二套完整主题体系。

uni-ui 当前为 Apache-2.0。实现时必须保留许可证、修改说明和适用 NOTICE；选型记录本身不表示依赖已经进入发布物。

## 4. Flutter：官方自适应 UI 基础

Flutter 基线锁定 3.44.0，不使用第三方整套 UI 框架：

- Material 3 是 Android、Windows、Linux 和默认桌面/移动视觉基础；
- Cupertino 用于 iOS/macOS 需要保留原生行为的导航、弹层、开关和日期时间交互；
- `FullNetTokens` 与 `ThemeExtension` 承载颜色、字体、间距、圆角、状态和动效；
- `AdaptiveScaffold`、`AdaptiveDialog`、`AdaptiveSwitch` 等 Full.NET 包装只统一业务语义，不强行让所有平台像素一致；
- 数据网格、图表、富文本和编辑器等重型控件按真实模块单独选择，不得反向接管全局主题和路由。

Flutter Web 不承担 Full.NET H5；移动/桌面 UI 不复制完整管理后台，只服务终端用户和现场业务。

## 5. 跨端设计令牌

`packages/design-tokens` 保持语义来源，分别输出或映射：

- Vue：CSS Variables，供 Art Design Pro/Element Plus 覆盖层消费；
- Layui：CSS Variables，保持独立组件实现；
- uni-app：`uni.scss` 变量和跨端安全 CSS；
- Flutter：Dart 常量、`ColorScheme`、`TextTheme` 和 `ThemeExtension`。

共享的是语义和验证样例，不共享 Vue、Layui、uni-ui 或 Flutter 组件源码。

## 6. 状态与验收

选型进入文档后只能标记为 `Designing` 或“已选定、未集成”。只有满足以下条件才可提升：

1. 依赖或选择性源码已进入锁文件/仓库并完成许可证登记；
2. 原有认证、租户、权限、ProblemDetails 和多语言契约没有回退；
3. 对应目标完成类型检查、单测、生产构建和平台 E2E/冒烟；
4. Vue/Layui 后台功能继续满足双端门禁；
5. 包体积、静态资源来源、可访问性和高危漏洞检查通过。

## 7. 实施计划

- [Vue Art Design Pro 迁移](../plans/2026-07-18-vue-art-design-pro-adoption.md)
- [富文本编辑器基础](../plans/2026-07-18-rich-text-editor-foundation.md)
- [uni-app uni-ui 引入](../plans/2026-07-18-uniapp-uni-ui-adoption.md)
- [Flutter UI 基础](../plans/2026-07-18-flutter-ui-foundation.md)

## 8. 上游参考

- [Art Design Pro](https://github.com/Daymychen/art-design-pro)——MIT
- [Apache ECharts](https://github.com/apache/echarts)——Apache-2.0
- [Tiptap Core](https://github.com/ueberdosis/tiptap)——MIT；Pro 扩展不属于默认范围
- [HtmlSanitizer](https://www.nuget.org/packages/HtmlSanitizer/9.0.892)——MIT；服务端净化候选实现
- [uni-ui](https://github.com/dcloudio/uni-ui)——Apache-2.0
- [Flutter Material 3](https://docs.flutter.dev/ui/design/material)
- [Flutter Cupertino](https://docs.flutter.dev/ui/widgets/cupertino)
