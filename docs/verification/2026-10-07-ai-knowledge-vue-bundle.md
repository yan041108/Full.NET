# R07a Vue 知识库页面包体核对

日期：2026-10-07。代码基线：main `1173f95d756aab795370e3465f692bd016a3d3a9`。

本次交付知识库/文档目录、文件版本与导入操作、来源弹窗和知识问答。没有进行性能优化或改变拆包策略；两个目录路由继续动态导入。原首屏基线 1,368,052 字节、gzip 368,724 字节的 5% 门禁被候选超出 5,809 字节，因此先比较本批开始前的代码与候选，不能直接以候选覆盖旧基线。

## 环境与复现

Windows x64，同一工作区、Node.js 24.12.0、Vite 8.1.5、Vue 3.5.40、同一已安装锁定依赖。两次构建没有调用 API、数据库或模型。

- 候选：`pnpm --filter @fullnet/admin build`，包含 vue-tsc 与 Vite，退出 0。
- 基线：`git archive` 导出基线的 `ui/admin` 与 `packages` 到忽略目录 `artifacts/r07a/baseline`，使用同一安装依赖的绝对路径 junction。Vite 临时配置明确将 `@fullnet/admin-i18n` 和 `@fullnet/client-contracts` 指向导出的基线源码，保持相同 Vue 插件和其余生产构建设置；不切换工作区分支或源码。
- 基线构建：`pnpm --filter @fullnet/admin exec vite build --config ../../artifacts/r07a/baseline-vite.config.mjs`，最终退出 0，输出 `artifacts/r07a/baseline-dist-complete`。首次临时配置因 Windows ESM 路径、依赖解析失败，不计为有效样本。
- 测量：使用 `scripts/testing/check-frontend-bundle-budget.mjs` 的 `measureStaticJavaScriptGraph`；全部 JS 逐文件统计原始长度、Node gzip 与 Brotli 压缩长度，不把动态依赖排除在总体积外。原始逐 chunk 结果保存于 `artifacts/r07a/bundle-comparison.json`，构建日志同目录。

## 实际结果

压缩大小为逐文件相加，Brotli 使用 Node 默认参数，仅为产物比较。

| 范围 | 基线 minified | 候选 minified | 基线 gzip | 候选 gzip | 基线 Brotli | 候选 Brotli |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 首屏静态图（各 68 chunks） | 1,426,485 | 1,442,263 | 376,928 | 379,523 | 315,059 | 316,878 |
| 全部 JS（269 → 273 chunks） | 3,873,948 | 3,922,669 | 1,152,440 | 1,165,926 | 999,408 | 1,010,662 |

首屏新增 15,778 字节（1.11%），gzip 新增 2,595 字节（0.69%）；全部 JS 新增 48,721 字节（1.26%），gzip 新增 13,486 字节（1.17%）。已有基线相对原门禁已增长 4.27%，本批的共享语言资源、导航和生成客户端消费者继续消耗余量。新增目录共享 chunk 为 14,298 / 4,581 字节（minified/gzip），来源弹窗为 11,130 / 4,096 字节；两个路由包装分别 252 / 198、275 / 207 字节。

## 基线与停止条件

首屏门禁重新对齐**本批开始前的同环境实测基线** 1,426,485 / 376,928 字节，保留 `maxRegressionPercent: 5`；没有使用候选值或放宽比例。ECharts 与 VForm3 延迟预算不变。候选相对新基线仍需通过门禁，今后任一指标超过 5% 必须停止并重新定位原因，不能自动刷新基线。后续页面扩展继续核对全部 JS 总量，不能通过转移 chunk 掩盖增长。

本次只验证构建产物大小，未测量浏览器资源瀑布、加载延迟、P95/P99、移动端或真实页面交互，不宣称首屏体验改善。页面自动化与人工验收仍登记 R12，容量保持 `Capacity-not-verified`。
