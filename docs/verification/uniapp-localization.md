# uni-app 多语言验证记录

验证时间：2026-07-26 19:24（Asia/Shanghai）

## 状态

| 范围 | 状态 | 证据边界 |
|---|---|---|
| C3 uni-app 基础客户端 | Implementing / Build-verified | uni-ui/easycom/主题令牌、真实设置页、H5 自动冒烟与三目标 CLI 构建通过；真实登录、租户、会话失效和两个小程序开发者工具仍未验收 |
| L3 uni-app 多语言 | Implementing / Build-verified | 规范语言、静态资源、账号偏好原子提交、ProblemDetails 与 H5 浏览器链路已验证；微信/支付宝开发者工具未执行 |

该状态不是 `Verified`。uni-app CLI 构建只能证明产物可生成，不能代替微信开发者工具、支付宝小程序开发者工具或真机运行。

## 工具链

| 工具 | 实际版本 |
|---|---|
| Node.js | 24.12.0 |
| pnpm | 10.26.0 |
| uni-app/DCloud npm 包 | 3.0.0-5010520260709002（精确锁定） |
| uni-ui / Sass Embedded | 1.5.12 / 1.100.0（精确锁定） |
| uni-app 编译器 | 5.15（Vue 3，三目标构建输出） |
| Vue / Vue Compiler SFC | 3.4.21 |
| Vue I18n | 9.14.5 |
| Vite / Vitest | 5.4.21 / 3.2.6 |
| Playwright | 1.61.1（独立 H5 E2E 开发依赖） |

## 自动验证

| 命令 | 结果 |
|---|---|
| `pnpm --filter @fullnet/uniapp test` | 12 个测试文件、103 项测试通过 |
| `pnpm --filter @fullnet/uniapp typecheck` | 通过，标准 SFC `vue-tsc --noEmit` |
| `pnpm --filter @fullnet/uniapp build:h5` | 通过，`clients/uniapp/dist/build/h5`，11 个文件，437931 bytes |
| `pnpm --filter @fullnet/uniapp build:mp-weixin` | 通过，`clients/uniapp/dist/build/mp-weixin`，62 个文件，252662 bytes |
| `pnpm --filter @fullnet/uniapp build:mp-alipay` | 通过，`clients/uniapp/dist/build/mp-alipay`，63 个文件，369398 bytes |
| `pnpm test:e2e:uniapp` | Edge、6 项 H5 Playwright 场景通过 |

H5 场景覆盖中文启动、匿名切换英文、Enter 键提交、刷新保持、`html lang`、导航标题、核心文案、匿名请求规范 `Accept-Language`、认证 PUT 使用切换前已提交语言、服务端确认后提交语言与版本、409 回滚、本地化稳定错误码、安全服务端标题和 `traceId`；新增 uni-ui 冒烟覆盖双语、输入焦点、必填错误、弹层开关和 320 CSS px 无横向溢出。测试通过真实页面交互驱动状态；仅通过 DEV-only 端口注入认证快照和发起正式 `HttpClient` 请求，不实现假登录、假 Token 或生产认证入口。

生产 H5 产物在三目标重建后扫描 `__FULLNET_UNIAPP_E2E__` 与 `fullnet-uniapp-e2e-fixture`，均为零命中；DEV bridge 未进入发布物。三份产物对远程 locale/i18n/messages/translation 资源模式扫描为零命中，双语消息均编译进本地产物。

## 平台工具

| 平台 | 结果 |
|---|---|
| H5 / Windows Edge | 6 项自动浏览器冒烟通过 |
| 微信开发者工具 | Not executed — required tool not installed |
| 支付宝小程序开发者工具 | Not executed — required tool not installed |

已只读检查 PATH、Program Files 与当前用户 LocalAppData 常见安装路径，未找到两个开发者工具。未安装、未启动、未导入产物，也未执行真机验证。

## 依赖与许可证

`pnpm audit:clients` 使用官方 npm registry 完成审计，未发现未审查的 Critical/High 漏洞。当前接受门禁中已登记的 `GHSA-fx2h-pf6j-xcff`（`clients__uniapp>vite`）与 `GHSA-mh99-v99m-4gvg`（`clients__uniapp>vue-tsc>@vue/language-core>minimatch>brace-expansion`）开发工具链例外，路径或期限变化会自动使门禁失败。DCloud 插件仍声明精确 Vite 5.2.8 peer；当前 5.4.21 仅表示 Full.NET 三端构建和 H5 E2E 兼容验证通过，不代表上游正式支持。Playwright 1.61.1 与 Sass Embedded 1.100.0 是开发依赖，不进入生产业务代码；仓库 `THIRD-PARTY-NOTICES` 已登记 uni-ui 1.5.12 Apache-2.0。`pnpm licenses list --prod --json` 成功枚举 13 个许可证分组。

三目标构建仍会显示 DCloud/uni-ui 1.5.12 上游 Sass legacy JS API 与 `mix()` 全局函数弃用警告；Full.NET 自有样式已使用 `@use`，没有修改 `node_modules`。警告当前不阻止构建，后续升级 DCloud/uni-ui 时必须重新验证并移除。

## 后续人工验收

1. 安装受控版本的微信开发者工具，导入 `clients/uniapp/dist/build/mp-weixin`，验证启动、切换、重启、导航标题、真实登录/API Header、错误和会话失效。
2. 安装受控版本的支付宝小程序开发者工具，导入 `clients/uniapp/dist/build/mp-alipay`，执行同一场景。
3. 两个平台分别记录工具版本、设备/模拟器信息、结果和缺陷；完成前保持 `Implementing / Build-verified`。

## 官方参考

- [uni-app CLI 工程](https://uniapp.dcloud.net.cn/quickstart-cli.html)
- [uni-app 国际化](https://uniapp.dcloud.net.cn/tutorial/i18n.html)
- [uni-app Locale API](https://uniapp.dcloud.net.cn/api/ui/locale)
