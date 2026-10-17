# Vue 加载边界与报表导出验收修复

本批属于冻结清单 FA-01/FA-04 的缺陷修复，不增加开发范围。开发基线为 `ddc6508174e3ae625d4101b1323afcc482602218`；干净私有验证投影为 `f4b722df73daaabdd8b6459d67a3ce89aa50b371`，目录 `G:/.fullnet-tests/fa-package-ui-YmxxEN`。冻结开发余 0；FA-03—FA-06 尚未关闭。

## 问题与修改

- 真实 CI job 117233153732 已越过 OpenAPI 兼容门禁，在初始 JS 预算处失败：1,480,687 字节超过原 1,368,052 字节基线的 5% 上限。后台布局改为按需加载；概览用轻量路由入口在实际渲染时加载，避免会话恢复期间仅解析根路由就下载完整概览。
- 原初始 JS、图表、可选设计器预算均不放宽。新增“登录后概览”完整静态依赖闭包预算，合并入口、布局、概览入口及内容并去重共享依赖，防止将首屏体积转移到按需资产后隐藏总增长。
- 独立数据交付样例在 SQL Server/MySQL 均创建成功的导出任务，但详情抽屉遮挡列表下载按钮。验收改为点击可见、可用且绑定本次任务 ID 的详情按钮，继续核对该任务下载路径、HTTP 200、真实下载事件、文件名及工作簿数据；权限与任务身份校验保留。

## 生产资产测量

Windows 10/i7-12700H，Node 24.12.0、Vite 8.1.5；原冻结构建和当前生产构建使用相同工具版本与配置。表中为静态依赖图逐文件压缩后求和；Brotli 是观察指标，现有预算门禁检查 minified/gzip。

| 场景 | minified 字节 | gzip 字节 | Brotli 字节 |
| --- | ---: | ---: | ---: |
| 原初始 / 登录后概览 | 1480687 | 394836 | 327613 |
| 当前初始 | 1233437 | 309912 | 251477 |
| 当前登录后完整概览 | 1485108 | 398511 | 331357 |

初始 minified 减少约 16.7%；登录后完整概览 minified 增长约 0.30%，gzip 增长约 0.93%，均在新增 5% 门禁内。四个预算目标均实际通过。未做请求延迟、吞吐或容量对照，不将字节减少解释为页面时间同比提速；容量仍为 `Capacity-not-verified`。

## 实际验证

- 在上述独立副本 `ui/admin` 运行 `node node_modules/vitest/vitest.mjs run src/App.test.ts src/App.oidc-callback.test.ts src/App.auth-callback-flow.test.ts src/router/index.performance.test.ts src/router/index.auth-guard.test.ts src/views/OverviewView.test.ts src/views/ReportingExportTasksView.recovery.test.ts --maxWorkers=1 --no-file-parallelism`：48/48、零失败/跳过、exit 0。
- 同目录 `node node_modules/vue-tsc/bin/vue-tsc.js --noEmit -p tsconfig.json` 和 `node node_modules/vite/bin/vite.js build` 均 exit 0；副本根目录 `node scripts/testing/check-frontend-bundle-budget.mjs` 四目标通过、exit 0。
- 开发工作区 `node --test tests/performance/frontend-bundle-budget.test.mjs tests/templates/application-printing-browser-lifecycle.test.mjs`：8/8、零失败/跳过、exit 0。覆盖多入口合并、共享依赖去重、缺失资产拒绝和浏览器取消/拒绝处理。
- 使用生产 `vite preview`、真实 Edge 和受控 HTTP 重跑既有壳层两个场景：动态导航/可信租户范围，以及刷新失败后登录、进入控制台并退出；2/2、零失败/跳过、exit 0。记录匿名根地址无 OverviewView/ArtAdminShell 请求、登录后两者均请求的实际请求列表。它不替代 FA-03 真实后端验证。
- 最小回归先实际失败；生产浏览器首轮亦真实暴露路由提前加载，失败日志保留。组件测试等待真实嵌套动态导入与 Vue 刷新，不用空占位 DOM 代替页面通过。有限只读复审未发现本批 P1/P2。

原始命令、参数、退出码、输入摘要、日志及报告位于本轮 `.tmp/foundation-freeze-20261016/`：`bundle-printing-repair.json`、`bundle-printing-ui-green4.json`、`bundle-production-browser4.json`、`frontend-bundle-before.json`、`frontend-bundle-after4.json`。该目录为本地运行证据，永久结论以本报告真实范围为准。

## 浏览器夹具与本轮样例结果

CI job `117243450739` 已通过客户端测试、OpenAPI 兼容、构建及全部包预算，浏览器阶段为 145 passed、1 failed、1 flaky、4 skipped，不能计整批成功。保留原始日志和 trace，修正两个已定位的夹具缺陷：文件名只在真实目录表内断言，删除使用当前确认按钮的正常点击；Host PDF 离页改为相同 Host 作用域的文件页，并等待目标页面专属标题，避免匹配旧页标题。

使用独立生成应用捕获的生产资产和前端源码，在自有副本运行既有两个受控 HTTP 场景：每个场景在 production/development 各重复 3 次，单 worker、retries 0，两模式各 6/6，零失败/跳过/flaky，命令及实际 exit 0 见 `ci-browser2-fixture-repair.json`。这是 2 个独立场景的 12 次执行，不是 12 个不同业务场景，也不代替真实后端验收。

Minimal 导航探针补充有界模块请求/响应、路由事件、失败阶段及脱敏错误；不改原导航、超时或业务断言。只读复审发现辅助 evaluate 可能挂起后，统一加 2 秒截止，诊断失败后仍保留原 Error 并继续清理。`node --test tests/templates/application-crud-browser.test.mjs` 实际 6/6、零失败/跳过、exit 0，覆盖原租户落地与辅助诊断成功、拒绝、同步异常、永久 pending 后清理。复审未再发现 P1/P2。新增诊断尚未在真实失败样例中执行，不能据此声称导航问题已修复。

`parent15` 已自然结束，实际 exit 1：独立数据交付 SQL Server 的导入、独立 Worker、报表及真实下载/打印边界通过；MySQL 在 API 构建时 ETIMEDOUT，未进入业务验收。Minimal 双库已越过准备期，在产品导航断言失败；现有 mock 诊断未复现，根因仍待真实探针定位。有效审批双库 2 场景结果仍按输入范围复用。后续只续验失败/未执行范围，不重跑已通过业务场景。

原资源观察器查询超时失败与一次因 FILETIME 精度不匹配被拒绝的交接决定均保留；未将它们计作清理通过。使用精确十进制 birth 重新执行完整物理核对后，发布纯资源回执 `artifacts/foundation-freeze-20261016-resource-continuation1-final.json`（主工作区，SHA-256 `75324907112addc889a8468f64930dd5e48c599fe0bf5bc5a9152f29eb202b16`）。该回执 acceptance=false，仅证明本窗进程、测试锁、容器/卷已释放；历史及未知资源保留。按既定协调顺序交给 AI 专项做有界生成准备后再续验。

## 仍待关闭

开发剩余 0、验收组仍剩 4：FA-03 真实后端双库浏览器、FA-04 失败/未执行样例、FA-05 更新依赖后的 API/Worker 分析及 Linux Native 实跑、FA-06 汇总。未合并、未发布。
