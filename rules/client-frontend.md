# Full.NET 客户端与前端规则

## 1. 状态、范围与目标

- 状态：强制
- 来源：项目所有者先后确认客户端框架、许可与验收边界，并于 2026-08-02 明确停止 Layui 新功能开发、全力推进 Vue 风格后台，同时要求后台页面和业务按钮端到端精确授权
- 适用范围：Vue 主管理端（`ui/admin`）、冻结的 Layui 存量管理端（`ui/admin-layui`）、uni-app 客户端（`clients/uniapp`）、Flutter 原生与桌面端、可选 .NET MAUI，以及共享契约（`packages/client-contracts`）与设计令牌（`packages/design-tokens`）
- 目标：以 Vue 作为唯一后台产品交付线，保留 Layui 历史成果但停止功能扩张；统一权限、租户、错误、许可与资产来源边界，并用可自动验证的页面/操作授权门禁保护前后端一致性

本文是客户端基线的唯一权威源。2026-08-02 之前的 Spec、Plan、路线图或验证记录中“Vue/Layui 长期并行”“双端同步”“双端 `Verified`”仅描述当时决策和历史证据；凡涉及后续开发与退出门槛，均由本文的新决策替代。

## 2. Vue 单一后台交付线与 `Verified` 门槛

1. `ui/admin` 是 Full.NET 后台管理产品唯一持续交付线。所有新增后台页面、业务按钮、交互、可视化、富文本和模块接入必须在 Vue 3 + TypeScript 管理端实现。
2. 新后台能力只有在服务端、共享客户端契约、Vue 权限/租户/错误处理、关键业务流程、可访问性和对应真实栈 E2E 全部通过后，才可标记 `Verified`；计划、接口、Vue 静态页面或局部构建通过最多标记为 `Implemented`/`Build-verified`。
3. 禁止把 Layui 页面、适配器、测试或 E2E 列为新增功能的完成条件，也禁止为追求历史双端对等延迟 Vue 主线。
4. 动态导航、页面和按钮可见性只负责体验，管理 Endpoint 仍必须执行服务端权限策略；未知导航、权限或操作标识必须失败关闭（见 [`development-quality.md`](development-quality.md) R-20260717-client-navigation-boundary 与 R-20260802-admin-action-authorization）。
5. 与 API 跨 Origin 且携带凭据的 Vue 管理端必须验证精确 CORS 与认证写 Endpoint 限流（见 [`development-quality.md`](development-quality.md) R-20260717-credentialed-cors）。

验证：`pnpm --filter @fullnet/admin test`、Vue 生产构建、受影响 Mock/真实栈 Playwright、`pnpm audit:clients` 和许可证检查。能力状态记录于 [`capability-status`](../docs/roadmap/capability-status.md)，不得继续用 Layui 缺口阻止 Vue 能力按真实证据更新状态。

## 3. 页面、操作与按钮权限

1. 服务端 Authorization Catalog 是权限唯一权威。权限码采用稳定业务语义，例如 `identity.users.read`、`identity.users.create`、`identity.users.reset_password`；禁止把 URL、HTTP Method、组件路径、显示文本或本地化键作为权限码。
2. 每个受保护后台页面绑定一个页面读取权限。每个调用受保护 API、读取敏感数据、导出数据或产生业务副作用的操作绑定一个独立操作权限；新增业务不得继续用一个 `*.write` 覆盖创建、更新、启禁用、授权、重置、删除等不同动作。
3. 角色授权页必须按“模块/目录 → 页面 → 操作”展示同一目录。勾选操作必须同时勾选页面权限；取消页面必须移除全部后代操作；服务端必须复验该不变量并拒绝未知、重复、越作用域或孤立权限。
4. Vue 必须在创建业务按钮、菜单项、批量操作、行操作或快捷入口之前调用统一的响应式权限门。无权限元素不进入 DOM，禁止只置灰、仅在点击时提示或在 CSS 中隐藏。
5. 页面访问由服务端导航投影和 Vue 路由守卫共同约束；操作访问由 Vue 权限门改善体验，由 Endpoint 精确权限承担安全边界。手工构造 URL、HTTP 请求或修改 DOM 均不能绕过服务端 `403`。
6. 纯本地的取消、关闭、分页、排序、布局切换和不读取新数据的折叠控件不进入授权目录；“查询/刷新”只要会调用受保护读取 API，就复用页面读取权限或按敏感性定义独立读取动作。
7. 超级管理员通过动态目录获得全部已登记权限，仍必须经过账号、会话、Host/租户作用域、Endpoint、审计和最后一名保护，不允许客户端或服务端短路绕过。

验证必须覆盖：页面有权/无权、单个操作授予/撤销、按钮不渲染、直接 API `403 authorization.permission_denied`、权限变更后的会话/缓存失效、未知权限失败关闭，以及 Authorization Catalog—Endpoint—Vue 绑定一致性。

## 4. Vue 主管理端

1. 技术栈固定为 Vue 3 + TypeScript + Vite + Element Plus，管理壳层与交互基线采用 MIT 许可的 Art Design Pro。
2. 默认图表引擎为 Apache-2.0 的 ECharts，必须模块化注册与按需加载，禁止全量引入。
3. 富文本默认采用 MIT 的 Tiptap Core；禁止默认引入付费 Pro 扩展。
4. 只引入经许可证与资产来源审计的代码；Full.NET 自有认证、租户、权限、ProblemDetails、路由白名单与 OpenAPI 契约禁止被模板内置 Mock 或请求层替代。
5. 页面不得维护手写权限白名单副本。可显示的页面和操作元数据来自经过运行时结构校验的服务端目录；组件与路由仍映射到本地可信白名单，权限变化通过统一 Session 快照响应式生效。

验证：`pnpm --filter @fullnet/admin test`、Vue 生产构建、受影响 Vue E2E、`pnpm audit:clients`、`pnpm licenses list --prod --json`；设计背景见 [`client-ui-framework-design`](../docs/superpowers/specs/2026-07-18-client-ui-framework-design.md)与 [`vue-art-design-pro-adoption`](../docs/superpowers/plans/2026-07-18-vue-art-design-pro-adoption.md)。

## 5. Layui 存量冻结与退役边界

1. `ui/admin-layui` 自 2026-08-02 起进入存量冻结。禁止新增后台模块、页面、业务按钮、API Adapter、共享契约消费、功能对等测试或 Playwright 场景，禁止把代码生成器继续扩展为新的 Layui 产物。
2. 既有 Layui 源码、历史测试和验证记录暂不删除，也不再代表 Full.NET 的官方后台产品承诺。现有能力可以作为迁移参考或短期兼容入口，但不得阻塞 Vue、服务端或公共契约演进。
3. 只有项目所有者明确授权的 Critical/High 安全修复、数据保护修复、许可证处置、迁移辅助或正式退役任务可以修改 `ui/admin-layui`。此类任务必须保持最小范围，不得借修复继续扩张功能。
4. CI 可在退役计划落地前保留冻结基线检查，用于发现意外污染；当公共契约的批准演进使冻结客户端不再兼容时，应记录为存量退役债务，不得要求新功能补写 Layui 实现。
5. layuiAdmin 只保留历史功能/交互参考意义；未经“公开源码并允许 MIT 再发布”的明确书面授权，始终禁止复制或提交其源码及产品资产。

验证：新增功能 diff 中 `ui/admin-layui/**`、Layui E2E 和 Layui 生成模板必须保持零新增；例外任务必须在计划、提交和验证记录中引用明确授权与退出条件。

## 6. uni-app（H5、微信、支付宝小程序）

1. H5、微信小程序与支付宝小程序统一采用 uni-app Vue 3，默认 UI 组件库为官方 uni-ui。
2. 原版 uView 2 不进入默认依赖；其他组件库只有在能力缺口、Vue 3 与三目标兼容、许可证与体积门禁全部通过后才可按需引入。
3. 三目标必须分别构建验证，H5 还须通过真实浏览器 E2E。

验证：`pnpm --filter @fullnet/uniapp build:h5`、`build:mp-weixin`、`build:mp-alipay` 与 `pnpm test:e2e:uniapp`。

## 7. 原生与桌面端（Flutter，可选 MAUI）

1. 原生 Android/iOS 与 Windows/macOS/Linux 桌面端默认采用 Flutter 3.44 的 Material 3 + Cupertino 官方组件与 Full.NET 设计令牌，不绑定第三方整套 UI 框架。
2. .NET MAUI 只有在真实 C#/Windows 企业需求命中决策门禁后才作为可选模板引入，并需独立 ADR。
3. Dart/Flutter 文件使用 snake_case，类型使用 UpperCamelCase，成员使用 lowerCamelCase；设计令牌来源于 `packages/design-tokens`。

## 8. 跨端共同约束

1. JSON 字段、权限码、错误码、消息类型与 API 路径由服务端契约决定，客户端不得自行改名。
2. 多语言遵循“统一治理、平台原生实现”，稳定机器码不可本地化。
3. 新增或升级第三方依赖前必须通过许可证与资产来源审计，并同步 [`THIRD-PARTY-NOTICES`](../THIRD-PARTY-NOTICES)。
4. 共享客户端包只能承载协议、运行时校验和无框架状态机，禁止反向依赖 Vue 组件或冻结的 Layui DOM 实现。

## 9. 验证命令汇总

```powershell
pnpm --filter @fullnet/admin test
pnpm audit:clients
pnpm test:e2e
pnpm test:e2e:uniapp
pnpm build:clients
```

`pnpm test:e2e` 和 `pnpm build:clients` 中若仍包含 Layui 历史目标，它们只表示冻结基线检查，不构成新增功能的双端交付要求；后续 CI 退役调整必须由独立计划实施。
