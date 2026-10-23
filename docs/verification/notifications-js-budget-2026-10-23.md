# Notifications 首屏与延迟 JavaScript 预算验收

2026-10-23，`main@5093b6f4` 加未提交工作区；Node 24.20.0、Vite 8.1.5、Windows/i9-14900K/64 GiB。原任务与失败历史继续使用 [实施计划](../superpowers/plans/2026-08-30-notifications-platform-extension.md)。

匿名页不使用 SignalR，将传输延迟到已认证状态机建立连接时加载。异步加载失败按既有退避重试；加载完成后重新核对会话上下文和释放状态。现有首页、Chart、VForm 阈值不变，增加 SignalR 独立预算。

| 范围 | Minified | gzip | Brotli | 预算 |
| --- | ---: | ---: | ---: | --- |
| 首页静态图 | 1,467,584 | 382,260 | 316,942 | 通过 |
| SignalR 延迟包 | 55,116 | 14,040 | 12,449 | 通过 |
| Chart | 496,497 | 166,668 | 141,757 | 通过 |
| VForm | 8,501 | 3,235 | 2,785 | 通过 |

续办前首页为 1,521,901/gzip 395,887，均超出既定 5% 门槛。任务开始快照全部 JS 4,691,654/gzip 1,399,652，当前 4,717,131/1,406,320，增长 0.54%/0.48%，包含前述功能批次；不把延迟加载表述为总体积下降。

执行：资源池包装的 Vite 生产构建与 `vue-tsc --noEmit -p tsconfig.json`；仓库 `measureStaticJavaScriptGraph`、`measureJavaScriptAsset`、`evaluateBudget` 直接读取该生产输出；`node --test tests/performance/frontend-bundle-budget.test.mjs` 3/3；客户端 `vitest run` 295/295；Vue App/实时状态 15/15。App 曾一次超时，原失败保留，未提高超时或删除断言，重跑通过。

Edge headless 实际加载上述生产构建：匿名登录页面可见，首屏 SignalR 请求零；随后实际加载延迟模块成功，页面脚本异常零。匿名 API 使用测试夹具，此证据不替代真实双库业务 E2E，也不证明认证后的网络连接及厂商送达。哈希资源继续由 Vite 输出，没有改动部署缓存配置。

原始结果位于系统临时目录 `notifications-bundle-closed-20261023.json`、`notifications-lazy-browser-retry.log`、`notifications-lazy-vue-tests.log`、`notifications-frontend-lazy-20261023.log`。完整测试及失败边界见同一实施计划；容量保持 `Capacity-not-verified`。
