# Full.NET 客户端与前端规则

## 1. 状态、范围与目标

- 状态：强制
- 来源：项目所有者确认 Full.NET 后台管理与多端客户端的框架、UI 基线、许可与验收边界；此前这些约束仅分散在 `AGENTS.md` 基线段与 `docs/superpowers/specs/`，缺少 `rules/` 层的可执行细则与验证入口
- 适用范围：Vue 主管理端（`ui/admin`）、Layui 管理端（`ui/admin-layui`）、uni-app 客户端（`clients/uniapp`）、Flutter 原生与桌面端、可选 .NET MAUI，以及共享契约（`packages/client-contracts`）与设计令牌（`packages/design-tokens`）
- 目标：统一各端框架与 UI 基线、保护 Full.NET 自有认证/租户/权限/契约边界、约束第三方许可与资产来源，并为“双端同步”与 `Verified` 提供一致的验证方式

本文是客户端基线的唯一权威源。`AGENTS.md` 只保留单行不变量并链接本文；细节修改必须在此进行，不得在 `AGENTS.md` 重新内联。

## 2. 双管理端同步与 `Verified` 门槛

1. 后台管理功能必须在 Vue 主管理端与 Layui JS/HTML 管理端按同一模块同步开发，两端遵循同一权限、租户、错误处理与关键流程契约。
2. 只有两端的权限、租户、错误处理、关键业务流程与 E2E 全部通过后，客户端功能才可标记为 `Verified`；任一端缺失或未验证时，状态最多为 `Implemented`。
3. 动态导航、菜单与按钮可见性只负责体验，管理端点仍必须执行服务端权限策略，未知导航标识必须拒绝（见 [`development-quality.md`](development-quality.md) R-20260717-client-navigation-boundary）。
4. 与 API 跨 Origin 且携带凭据的管理端必须验证精确 CORS 与认证写端点限流（见 [`development-quality.md`](development-quality.md) R-20260717-credentialed-cors）。

5. 真实栈 E2E 断言当前租户上下文时，必须定位各管理端实际可见的当前上下文节点，或复用统一的双端辅助函数；禁止依赖全页同名文本、`.first()` 或 DOM 顺序绕过隐藏元素，因为 Vue 组件的隐藏选项与 Layui 当前上下文可能具有相同文本但不同可见语义。

验证：`pnpm test:e2e` 运行 Vue/Layui 同场景 parity E2E；`pnpm test:e2e:provisioner` 扫描真实栈 spec，阻止直接使用 `getByText('Full.NET Host')`；运行期继续由 Vue/Layui 真实栈场景验证辅助函数的双端行为；能力状态记录于 [`capability-status`](../docs/roadmap/capability-status.md)。

## 3. Vue 主管理端

1. 技术栈固定为 Vue 3 + TypeScript + Vite + Element Plus，管理壳层与交互基线采用 MIT 许可的 Art Design Pro。
2. 默认图表引擎为 Apache-2.0 的 ECharts，必须模块化注册与按需加载，禁止全量引入。
3. 富文本默认采用 MIT 的 Tiptap Core，由 Vue 与 Layui 分别适配；禁止默认引入付费 Pro 扩展。
4. 只引入经许可证与资产来源审计的代码；Full.NET 自有认证、租户、权限、ProblemDetails、路由白名单与 OpenAPI 契约禁止被模板内置 Mock 或请求层替代。

验证：`pnpm --filter @fullnet/admin test`、`pnpm audit:clients`、`pnpm licenses list --prod --json`；设计背景见 [`client-ui-framework-design`](../docs/superpowers/specs/2026-07-18-client-ui-framework-design.md)、[`vue-art-design-pro-adoption`](../docs/superpowers/plans/2026-07-18-vue-art-design-pro-adoption.md)、[`rich-text-editor-foundation`](../docs/superpowers/plans/2026-07-18-rich-text-editor-foundation.md)。

## 4. Layui 管理端

1. Layui 管理端与 Vue 主管理端 **长期并行**（项目所有者 2026-07-21 确认）：不是过渡兼容层，不设默认退役窗口；后台模块必须双端同步交付，适用第 2 节 `Verified` 门槛。
2. 只依赖 MIT 的 Layui 核心库并独立实现，不绑定第三方整套后台框架。
3. layuiAdmin 仅可作为公开页面的功能/交互参考；未获得“公开源码并以 MIT 再发布”的明确书面授权前，禁止复制或提交其源码及产品资产。
4. 多词文件与 HTML 路径使用 kebab-case，导出函数使用 camelCase（见 [`naming-conventions.md`](naming-conventions.md) 第 9 节）。

验证：`pnpm --filter @fullnet/admin-layui test`；许可来源记录于 [`THIRD-PARTY-NOTICES`](../THIRD-PARTY-NOTICES)；决策见 [`external-review-2026-07-21`](../docs/verification/external-review-2026-07-21.md) 与 [`client-delivery-roadmap`](../docs/roadmap/client-delivery-roadmap.md) §3.1。

## 5. uni-app（H5、微信、支付宝小程序）

1. H5、微信小程序与支付宝小程序统一采用 uni-app Vue 3，默认 UI 组件库为官方 uni-ui。
2. 原版 uView 2 不进入默认依赖；其他组件库只有在能力缺口、Vue 3 与三目标兼容、许可证与体积门禁全部通过后才可按需引入。
3. 三目标必须分别构建验证，H5 还须通过真实浏览器 E2E。

验证：`pnpm --filter @fullnet/uniapp build:h5`、`build:mp-weixin`、`build:mp-alipay` 与 `pnpm test:e2e:uniapp`；策略见 [`uniapp-uni-ui-adoption`](../docs/superpowers/plans/2026-07-18-uniapp-uni-ui-adoption.md)、[`multi-client-frontend-strategy-design`](../docs/superpowers/specs/2026-07-17-multi-client-frontend-strategy-design.md)。

## 6. 原生与桌面端（Flutter，可选 MAUI）

1. 原生 Android/iOS 与 Windows/macOS/Linux 桌面端默认采用 Flutter 3.44 的 Material 3 + Cupertino 官方组件与 Full.NET 设计令牌，不绑定第三方整套 UI 框架。
2. .NET MAUI 只有在真实 C#/Windows 企业需求命中决策门禁后才作为可选模板引入，并需独立 ADR。
3. Dart/Flutter 文件使用 snake_case，类型使用 UpperCamelCase，成员使用 lowerCamelCase（见 [`naming-conventions.md`](naming-conventions.md) 第 9 节）；设计令牌来源于 `packages/design-tokens`。

验证：Flutter 侧构建与组件测试；基线见 [`flutter-ui-foundation`](../docs/superpowers/plans/2026-07-18-flutter-ui-foundation.md)。

## 7. 跨端共同约束

1. JSON 字段、权限码、错误码、消息类型与 API 路径由服务端契约决定，任何客户端不得自行改名（见 [`naming-conventions.md`](naming-conventions.md) 第 8、9 节）。
2. 多语言遵循“统一治理、平台原生实现”，稳定机器码不可本地化（见 [`development-quality.md`](development-quality.md) R-20260717-full-stack-localization-boundary）。
3. 新增或升级第三方依赖前必须通过许可证与资产来源审计，并同步 [`THIRD-PARTY-NOTICES`](../THIRD-PARTY-NOTICES)。

验证：`pnpm test:workspace`、`pnpm audit:clients`、`pnpm build:clients`。

## 8. 验证命令汇总

```powershell
pnpm test:workspace
pnpm audit:clients
pnpm test:e2e
pnpm test:e2e:uniapp
pnpm build:clients
```
