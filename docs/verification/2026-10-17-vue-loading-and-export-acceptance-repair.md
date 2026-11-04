# 冻结验收收口：加载边界、真实双库与交付限制

截至 2026-10-18，本窗口冻结开发余 **0**、规定范围待运行验收余 **0**。业务验收 FA-01—FA-05 已通过，FA-06 的证据与限制已汇总；**6 个临时输出目录及 26 个失败 trace 文件因命令策略拒绝而未清理**，不报告全部清理完成。本结论仅覆盖开发分支的冻结候选，不代表 Main 中其他专项完成。未合并、未发布，容量仍为 `Capacity-not-verified`。

| 冻结组 | 当前结论 | 覆盖边界 |
| --- | --- | --- |
| FA-01 | 通过，分阶段证据 | 组件、类型/构建、契约/治理、Unit/架构及既定客户端范围；保留最初失败与聚焦修复记录 |
| FA-02 | 通过，324 个唯一 UID | 双库固定范围逐 UID 覆盖；不把重叠作业数量相加 |
| FA-03 | 通过，双库 65+65 | 每库 29 文件、65 个原冻结 UID；SQL Server 为有效 58 项加本轮 7 项，MySQL 本轮一次 65/65，零失败/跳过/flaky |
| FA-04 | 通过，业务 6/6 | 历史 3 项与当前 3 项分阶段覆盖，包含独立 Minimal 双库与企业数据交付 MySQL；不称六项同源单次全绿 |
| FA-05 | 通过，52 项 | API/Worker 原生范围 50 项与企业双库原生链路 2 项；原消费者与原生生产者分别绑定 |
| FA-06 | 报告完成，清理受限 | 范围、复用、原失败、资源归还及以下六个未清理目录和26个trace文件均已明确登记 |

## 2026-10-18 本轮修复与实际验证

运行基线为 `93010d9668cf0cbfeccd4a606332dbf8141e6826`，最终浏览器输入为该基线加精确补丁 SHA-256 `11d0fe5db55e7ec1a7285532cb94db3d297049e9ad871d44ca9e4e1b3baa29f3`，39 个原始文件哈希和完整 Git 差异共同冻结。验证目录复用 `G:/.fullnet-tests/fa-HG4DPX`，没有复制新整仓。永久源码增量仅涉及四个 Vue 页面、两处组件回归及原 E2E 场景；生产 .NET、SQL、依赖和公共 API 均未修改。

- GoView 新建按钮改用真实工具栏的 `left` 插槽，保留精确权限门禁；GoView、报表定义和报表导出补回既有样式的可访问标题。
- Host 文档上传及补读期间禁用版本文件选择，与已有处理函数的忙碌门禁一致；完成后才允许选下一份文件。真实组件回归先失败，再修复通过。
- E2E 使用现行创建弹窗、稳定确认按钮标识和当前会话令牌；按“当前版本”列验证版本值，区分可选择文件行与保留的上传/删除回执。批量删除同时核对真实响应中的两个文件 ID 和服务器逐项 404，没有强制点击、扩大超时或允许失败状态集合。
- API 校验按具体实现核对 `Validation → 400` 及稳定机器码；未获授租户与 Host 越界创建导出均保持 403，工作流附件仍验证审批前 403、审批后 200。报表未发布场景自行创建测试元数据，不再因缺少预种数据而跳过。

实际命令及结果（Vue 命令工作目录为 `ui/admin`，完整参数、输出与退出码保存在下述系统 Temp 控制目录）：

| 实际执行 | 结果 |
| --- | --- |
| `node .../vitest/vitest.mjs run src/views/GoViewProjectsView.test.ts src/views/ReportingDefinitionsView src/views/ReportingExportTasksView --maxWorkers=1 --no-file-parallelism` | 8 文件、122/122；真实工具栏新增回归先 RED 1，再 GREEN |
| `node .../vitest/vitest.mjs run src/views/HostDocumentItemsView --maxWorkers=1 --no-file-parallelism` | 3 文件、53/53；忙碌选择新增回归先 RED 1，再 GREEN |
| `node .../vue-tsc/bin/vue-tsc.js --noEmit -p tsconfig.json`；`node .../vite/bin/vite.js build --outDir <本任务系统Temp>/admin-dist` | 两个对应客户端批次均实际 exit 0；后续仅测试代码变化时复用，不重复全量 Vue |
| 原 `frontend-bundle-budgets.json` 四项检查，通过现有测量/判定函数消费上述产物 | 四项均通过；初始闭包 gzip 311597、登录后概览 400176、图表 166664、可选设计器 3234 字节，原阈值未放宽 |
| `node .../@playwright/test/cli.js test --config <本任务系统Temp>/version-column-retry/playwright.config.mjs`，SQL Server 附原 UID 筛选，MySQL 不附额外筛选 | SQL Server 当前 7/7、2m12.894s，加有效复用 58 项构成 65/65；MySQL 65/65、5m48.837s。每个结果要求实际 Passed、只执行一次、无 skip/flaky，逐 UID 与原发现集合相等 |
| `node .../version-column-retry/verify-frozen-scope.mjs` | 实际 exit 0，双库 130 个规定场景完整覆盖；不称 SQL Server 单次全绿 |

原始 SQL Server 首轮 51 通过/13 失败/1 跳过、后续 7/7、7/4、2/2 的通过/失败记录均保留；只复验失败及实际受影响消费者，没有用修后的预期覆盖原失败。最后 SQL Server 7 项及 MySQL 65 项由同一 actor1944/birth134367800070012716 顺序执行，held exit 0、Job active 0/total270，执行会话实际 exit 0；原绝对执行截止 07:26:49Z、清理截止 07:31:49Z 未延长。API、Worker、Migrator 三份 DLL 在所有提供程序和重试前后哈希完全相同，确实复用同一 Release 构建。客户端 122 与 53 项是两个互不重叠的受影响组，不冒充当前完整 Vue 一次全绿。

证据根目录：`C:/Users/Administrator/AppData/Local/Temp/fullnet-frozen-acceptance-1792232794104/fa03-preparation/release-entry-93010d9`。`version-column-retry/frozen-scope-proof.json` SHA-256 `1e1f3cd518a03d13d4399c3e734125d7f039bc454dbce5c9c5045c7f96c68a19` 保存逐 UID 来源与原始报告摘要；`physical-return.json` 保存实际归还，`shared-binary-reuse-final.json` 保存 DLL 摘要。Windows x64、20 逻辑核、Node24.12.0、SDK10.0.402，真实聚合 Job8GiB/6CPU；专用 MySQL1.5GiB/1CPU、Redis0.25GiB/0.25CPU，SQL Server 原预算3GiB/1CPU。Ryuk 没有硬配额，不宣称全部 Docker 资源均受限。

中途发现 Docker Desktop/后台已停止、LinuxEngine 管道不存在，启动已安装 Desktop 后恢复引擎，没有停止达梦或修改启动项。两个共享容器原 ID/配额保留，但引擎中断后实际处于 Exited255；未手工启动或删除。最终与恢复后的共享状态基线一致，自有容器和双锁为 0，两把既存外来锁保留。07:13:57Z 鲜查归还后，已把下一完整测试窗口协调给日志专项；本窗口后续只在私有目录做报告、清理与 Git 交付。

入口修复提交 `93010d9` 的 [主 CI](https://github.com/yan041108/Full.NET/actions/runs/39099204913)、[API Native](https://github.com/yan041108/Full.NET/actions/runs/39099204957)、[Worker Native](https://github.com/yan041108/Full.NET/actions/runs/39099204967) 均为成功终态：合计 7 个成功作业、8 个跳过作业；跳过不计通过。主日志 SHA-256 `c51992440e67f9e1ada8a95e961dc4773ecf067bf7b410171a9757cbdcaf54d7`。这些 CI 绑定 93010d9，界面增量另由上述本地 175 项、类型/构建及真实双库证据覆盖，不冒充后续提交的 CI 已通过。

## 已知清理限制（6 个目录及 26 个 trace）

以下递归清理均在执行前被 `blocked by policy` 拒绝，**不是 Auto-review**，没有删除、改解释器、重命名或借生命周期清理绕过。当前新增三目录已确认由本批构建生成、无 reparse point、相关 Job0，合计 637225940 字节；预览和拒绝原命令分别保存 `cleanup-preview.json`、`cleanup-command-policy-rejected.txt`，`cleanup-limits.json` 明确 `cleanupPerformed=false`。拒绝命令中的只读哈希步骤同样未执行，之后单独完成只读哈希核验，不是删除重试。

| 目录 | 当前状态 |
| --- | --- |
| `C:/Users/Administrator/AppData/Local/Temp/fullnet-created-app-rs-aiZqS0` | 早先拒绝记录保留，未重试 |
| `C:/Users/Administrator/AppData/Local/Temp/fullnet-template-hive-FYzcWi` | 早先拒绝记录保留，未重试 |
| `G:/.fullnet-tests/fa-HG4DPX/.tmp/e2e-real-stack/build` | 首次误入 Debug 的失败输出，919 文件/51676182 字节，未重试 |
| `<证据根目录>/shared-build` | 已成功验收的 Release 输出，3840 文件/627573909 字节，清理被拒绝 |
| `<证据根目录>/ui-repair-checks/admin-dist` | 已成功验收的前端输出，400 文件/4825997 字节，清理被拒绝 |
| `<证据根目录>/document-upload-checks/admin-dist` | 已成功验收的前端输出，400 文件/4826034 字节，清理被拒绝 |

另对上述六目录之外、本批四次失败浏览器结果目录中的 **26 个精确 `trace.zip` 文件**做了独立清理预览（合计274456708字节），拟保留原始 JSON 报告、错误上下文和日志，删除已完成诊断的大 trace。该单文件清理命令同样在执行前被策略拒绝，文件未删，未换方式重试；原命令/拒绝保存在 `failure-trace-cleanup-policy-rejected.txt`，精确名单在 `failure-trace-cleanup-preview.json`。这不是对已拒绝六目录的删除重试。

没有生成新整仓副本或 repo 根 artifacts。实际剩余仅为上述清理限制；没有待开发或待运行验收项，不再自动添加功能、自查或验收任务。

## 历史过程记录

下方状态和数量属于各自日期的过程快照；当前剩余清单以上表为准。最初开发基线 `ddc6508174e3ae625d4101b1323afcc482602218`、私有验证投影 `f4b722df73daaabdd8b6459d67a3ce89aa50b371` 及目录 `G:/.fullnet-tests/fa-package-ui-YmxxEN` 仅用于定位历史证据。

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

Minimal 导航探针补充有界模块请求/响应、路由事件、失败阶段及脱敏错误；不改原导航、超时或业务断言。只读复审发现辅助 evaluate 可能挂起后，统一加 2 秒截止，诊断失败后仍保留原 Error 并继续清理。`node --test tests/templates/application-crud-browser.test.mjs` 实际 6/6、零失败/跳过、exit 0，覆盖原租户落地与辅助诊断成功、拒绝、同步异常、永久 pending 后清理。复审未再发现 P1/P2。新增诊断已进入后续真实场景，执行进展见下节；尚不能据此声称导航问题已修复。

`parent15` 已自然结束，实际 exit 1：独立数据交付 SQL Server 的导入、独立 Worker、报表及真实下载/打印边界通过；MySQL 在 API 构建时 ETIMEDOUT，未进入业务验收。Minimal 双库已越过准备期，在产品导航断言失败；现有 mock 诊断未复现，根因仍待真实探针定位。有效审批双库 2 场景结果仍按输入范围复用。后续只续验失败/未执行范围，不重跑已通过业务场景。

原资源观察器查询超时失败与一次因 FILETIME 精度不匹配被拒绝的交接决定均保留；未将它们计作清理通过。使用精确十进制 birth 重新执行完整物理核对后，发布纯资源回执 `artifacts/foundation-freeze-20261016-resource-continuation1-final.json`（主工作区，SHA-256 `75324907112addc889a8468f64930dd5e48c599fe0bf5bc5a9152f29eb202b16`）。该回执 acceptance=false，仅证明本窗进程、测试锁、容器/卷已释放；历史及未知资源保留。按既定协调顺序交给 AI 专项做有界生成准备后再续验。

## 后续 CI 与取消夹具复验

提交 `512b4ad` 的客户端 CI job `117254426663` 已实际成功：Vue 324 个文件共 3024/3024，i18n 8 项，其余 23 个工具文件 144 项，uni-app H5 7 项；Vue E2E 为 147 passed、4 skipped、零失败/flaky。4 个跳过保留，不能写成零跳过或整个 CI 全绿。原始日志 `ci-client-512b4ad.log` 的 SHA-256 为 `4425189e3c94229fc3e3bed49b3bea8c2ab31fef5b80aeaaf71141e832eca574`。

同轮后端 job `117254426433` 的全量 Unit 为 7420 通过、1 失败、零跳过，实际失败记录保留。失败在取消夹具返回后立即判断后代存活，约 155 ms 即失败。既有生产契约是停止父进程并尝试终止其进程树；[Process.Kill 官方说明](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.kill?view=net-10.0)明确终止异步进行，等待父进程退出不能证明后代均已退出。本次仅修正测试：仍验证取消异常、父进程立即停止、借入输入流可读；用独立 5 秒观察截止等待相同 PID 与出生时间的自有后代退出。新增负对照证明观察超时不会终止仍存活的父子进程，不修改生产代码、产品超时或取消契约。原测试 UID 改名为 `Caller_cancellation_stops_parent_and_eventually_terminates_owned_descendant`，不是新增业务场景。

只编译该真实测试源文件，引用已核验的两份未变生产 DLL，避免重建整个后端。145 份生产输入与原编译来源内容一致，其中 16 份仅 LF/CRLF 原始字节不同，已显式记录；不声称当前输入逐字节全部相同。`dotnet build ProcessProviderFocused.csproj -c Release --disable-build-servers -m:1` 实际 exit 0、零警告/错误；执行 `dotnet Full.NET.UnitTests.dll --timeout 1m --minimum-expected-tests 12 --report-trx`，Windows 12/12、Linux 同一程序集连续 3 轮各 12/12，均零失败/跳过、实际 exit 0。Linux 为 12 个不同用例的 36 次执行，不是 36 个不同场景，也不是 Native AOT。各轮自有容器已核验不存在。有限只读复审未发现本次测试修改的 P1/P2。

证据为本轮忽略目录内 `process-provider-focused-source.json`、`process-provider-focused-win.json`、`process-provider-focused-linux.json` 及逐轮 TRX/命令/退出码；这是受影响测试类的复验，不能报告完整 Unit 7422 项已通过。复审另提到进程树部分终止失败时 `AggregateException` 的未复现候选；它不是此次 CI 失败根因，未修改生产行为，也不自动加入冻结开发队列。

`parent16` 已自然结束、实际 exit 1：SQL Server Minimal 的 102 条迁移与 Development 播种、API/Worker、客户端与 Vue 构建均越过，仍在原产品导航 5 秒断言失败。产品组件及两个生成模块请求约 90 ms 内均返回 200，无 pageerror；超时记录仍为根路径，但原错误采集时地址已是产品路径，页面 evaluate 与截图未完成。尚不能据此认定组件下载慢、路由权限错误或具体生产缺陷。独立应用有效业务证据仍为 3/6，开发余 0、验收余 4。

## 导航失败后的有界定位

捕获副本保持源码与生产资产摘要不变，进行了三次轻量回放：开启 Vite 控制台转发、重放本轮真实身份/租户/产品响应、改用正式 Vite CLI 启动。三次均在原 5 秒内完成产品导航并显示生成页面，监督实际 exit 0；这只能排除相应单独条件，不能当成真实应用通过。第 7 次诊断因跨副本重复加载 Playwright 在启动前失败，原日志保留；第 8—10 次为有效回放。证据为 `generated-ui-navigation-exact{8,9,10}.json` 与 `navigation-light-closeout.json`。

导航诊断记录白名单、有界、脱敏的真实导航树、前 16 条 console 错误/警告与可取得的产品导航 CPU 热点摘要。CPU 证据不保留 URL query/hash 或原始 profile；正常流程绝不终止页面。只有原业务断言已失败且停止采样也超时，才尝试中止该自有页面脚本以取回采样，并保留原错误。真实浏览器死循环回归先揭示直接 stop 无法结束采样，随后验证失败后协议中止可取回热点。只读复审又发现初始化超时丢失清理所有权，补充迟到 session、enable/start 挂起的 RED→GREEN，截止后立即或迟到 detach，禁止继续初始化。最终 `node --test tests/templates/application-crud-browser.test.mjs` 为 16/16、零失败/跳过、实际 exit 0；限定复查确认该 P2 已关闭、无新增明确 P1/P2。该回归仅证明辅助诊断与清理行为，不声称导航缺陷已修复；真实后端结果见下文。

窗口16及后续轻量回放结束后，按精确 birth、实际进程句柄终态和资源归属核对，发布主工作区纯资源回执 `artifacts/foundation-freeze-20261016-resource-continuation2-final.json`，SHA-256 `82cf900e9aa2ffd1937546a2ff9e7d1224be91ad004cc94420394a906a43282f`。2515 个登记 actor、59 容器、16 会话、12 卷均已释放，acceptance=false、actualParentExit=1，不把清理当业务通过；未知和共享资源保留。按用户长期安全协调授权在自然失败边界交 AI 专项接续，本窗不启动重型等待器。

远端 `fd08a57` 的后端 job `117262584761` 已实际成功，原始日志确认 Unit 7422/7422、Compatibility 12/12、Architecture 250/250、双库影响集 526/526、企业 API 样例 16/16，均零失败/跳过；PR 不适用的 Serialization baseline 步骤仍为 skipped，不称全部步骤均执行。日志 SHA-256 为 `e9948cfe27d53ba54b4558abff27518b56769214835245ecdad4ba314f13ab02`，正式计数记录为 `ci-backend-fd08a57-summary.json`。首次日志下载被 gh 的终端转义保护拒绝，空文件与退出码保留；第二次仅将原始日志写文件，实际下载 exit 0，未在终端解释转义内容。客户端、两个迁移分组及 API/Worker Native 工作流亦已成功；新诊断与导航修复等待本批受影响回归关闭后合批推送。

后端 quality artifact 的两个完整 TRX 已下载并验证 ZIP CRC/SHA，实际分别为 526 与 16 个 Passed。逐 UID 比较发现主影响集只覆盖 FA-02 原冻结 324 项中的 318 项，不能凭总数更大就外推全覆盖；其余 6 项均为迁移恢复。在 current/legacy 两个迁移作业的 TRX 中找到这 6 个完全相同的 UID，均 Passed，两个作业原始 checkout 日志亦绑定实际生产者 `ab5d4c7`。最终 `fa02-current-ci-complete-scope.json` 确认冻结 324 项由当前提交的 318+6 证据完整覆盖，补强更新依赖后的验证；原分阶段本地证据、最初 318/324 的不完整比较与原中断结果均保留。不将各作业总数相加当作冻结范围，不把企业 API 样例当成独立生成应用或 Native 企业链路。

## Native 当前提交结果复用

远端提交 `fd08a5768d722c988facbc0afa7e62ae2eb937bc` 的 API job `117262584481` 与 Worker job `117262583842` 均为成功终态，包含分析、Linux 原生发布、88 项 AOT 架构门禁和各自真实外部进程测试。Worker 正式组 27/27、零失败/跳过；已取得 TRX 并与冻结计划 27 个 UID 精确相等。API 正式组 23/23、零失败/跳过；NativeAot 测试源与正式 runner 相对冻结源未变，矩阵 filter/minimum 完全一致，官方入口读取新鲜 TRX 并强制拒绝跳过后才成功。API 成功工作流没有导出逐项 TRX，按“未变源码/筛选器与实际正式组日志”绑定，明确区别于 Worker 的逐 UID TRX 证据。其他 Provider 组不计入冻结的 52 项。

发布清单确认 linux-x64、NativeAot 和实际原生文件大小，日志中的构建/分析为零警告/错误；没有把这些文件声称为本地已下载运行。原 Worker 整包下载 120 秒超时，失败保留；后续认证 Range 读取只传输 75,506 字节取得清单与完整 TRX，逐条 ZIP CRC 与落盘 SHA-256 均核验，不声称已验证整个压缩包摘要。API 亦只提取发布清单。没有保存访问凭据或签名下载 URL。

补核 CI 生产者时，实际 checkout 为 PR 合并引用 `ab5d4c75e6a532a938e035a80a34933aa8fe4b70`，不能仅将 job.head_sha 当作构建提交。API/Worker 原始 checkout 日志均确认该引用；GitHub Git commit 对象确认其父包含 `fd08a57`，且二者完整 Git 树精确同为 `b8bf1e4ebdc63c585d81358f49db50059a8bb8ca`，现有 3984 份编译/资源输入比较因此可绑定实际生产者。证明 `ci-native-producer-merge-binding.json` 与原日志摘要保留，后续企业 2 项入口同步校验此证明；不据此增加已执行场景数。

证据 `native-ci-reuse50.json`、`ci-{api,worker}-fd08a57.json/.log`、两份 Range 证据与 Worker TRX 位于本轮忽略目录。冻结 52 项现覆盖 50，后续只续验 `nativeAotEnterpriseIntegration` 2 项，不再重跑这两个已通过组；FA-05 与 FA-06 仍未关闭。后端完整 CI job 现已成功，新诊断将与本批导航修复合并推送，不用旧提交结果证明尚未复验的新导航代码。

## 复用产物与本轮接续准备

已从相同提交的 CI artifact 以 4 路有界 Range 请求下载 API/Worker 原生文件，校验条目 CRC、SHA-256、ELF 标识与发布清单大小。API 为 137,127,744 字节，Worker 为 88,304,760 字节；所需最高 glibc 符号版本为 2.38，尚未在本地 Linux 执行或证明 ABI 可用。使用单次 `git cat-file --batch` 核对 3984 份编译/资源输入与冻结副本内容一致，其中 3976 份仅 LF/CRLF 原始字节不同。原生产者提交和本地冻结 overlay 分别绑定；不把原旧消费者标成已使用安全补丁的新构建。证据 `ci-native-binary-reuse-ready.json`、`ci-native-source-reuse-match.json`；已准备仅构建一次新消费者、仅发现企业 2 项的入口，未执行构建或测试。

AI 专项在真实失败安全边界交出资源后，已按最终 SHA、父/观察器终态、锁及卷占用核验；只复用资源交接，不消费其业务结果。窗口17的 parent107084 / birth `134366739464048093`、句柄 observer102472、资源 collector98584 执行一项 SQL Server Minimal 官方场景，输入 `52ff56b`，业务断言和原预算不变。该窗口已自然结束，实际 exit 1：生成、Host 接线、权限、迁移/重复迁移、API/Worker 与 Outbox、客户端及 Vue 构建均越过，仍在原产品导航 5 秒断言失败。新导航快照确认切租户后包含产品入口；三个产品模块请求均返回 200，未记录 pageerror。仅取得有界 console 警告，CPU profile、页面执行诊断和路由事件未成功取回，不能声称热点已定位或导航问题已修复。原始 `remaining-acceptance-chain-attempt17.json`、`chain17-minimal-sql-profile.log` 和私有投影的 `application-crud-browser/read.json` 保留；独立应用有效业务结果仍为 3/6。

清理观察曾因历史 PID 80096 被系统复用且 `OpenProcess` 拒绝查询而失败，未将查询失败当作进程不存在。业务终态后保留原观察源码与摘要，使用只读 `NtQuerySystemInformation` 的精确 CreateTime 证明该 PID 当前 birth 与历史身份不同；自身及父进程的两种 API 出生时间一致。历史登记未删除，补充证明见 `window17-process-query-recovery.json`。随后完成 3031 个 actor、62 容器、17 会话、13 卷及自有锁的物理核对，发布主工作区纯资源回执 `artifacts/foundation-freeze-20261016-resource-continuation3-final.json`，SHA-256 `802038e106c133c5fc21edf7059e571cc7d5bb5b0658fcc7aeb3566bbc6738c3`；acceptance=false、actualParentExit=1。已显式交回 AI 专项，不以旧回执释放新窗口。

真实导航树回放 `exact11` 在 8/4 GiB 内存准入失败、runs=[]，未启动浏览器，清理已核验；不是业务复现或通过。后续在明确的短资源窗口执行 `exact12`：独立 normal 锁、CPU 亲和性 0x3（2 个逻辑 CPU）、单浏览器、75 秒诊断执行截止，实际一轮约 33 秒；原 5 秒导航断言失败，随后导航成功且生成页可见，诊断监督 actual exit 0 不代表业务通过。该轮同时使用完整真实导航树与严格 CPU 亲和性，与先前三次简化回放存在两项差异，不能仅归因为导航树。实际记录同一次点击触发两次 `router.push('/catalog/products')`，其中一次取消（类型 8）；未发生产品页整文档 load 事件，暂不将开发服务器整页重载作为根因。源码摘要未变，资源回执 `artifacts/foundation-freeze-20261016-navigation-replay12-resource-final.json` 的 SHA-256 为 `0f81ee86687085cf4a10d9ec564408f5677df2bc85da1af4396764b02333a4e5`。观察器在主工作结束后附加，未捕获初始全部后代历史，已明确记载；实际进程句柄 exit 0、外层命令终态、root/Vite/observer 缺席、端口/锁释放及全局 Playwright 浏览器缺席共同作为资源证明，没有声称自建 Windows Job。

沿重复导航调用链新增真实 Vue Router/Element Plus 的 jsdom 回归，覆盖链接点击、detail=0 激活事件、菜单项自身点击、Ctrl/Meta 处理及路由守卫。最初事件夹具使用只读 detail 属性的构造错误单独保留；修正后正式 RED 为 6 项中 5 个预期行为失败、1 通过，actual exit 1、零跳过。现将链接 `@click.prevent` 改为 `@click.stop`，由 RouterLink 处理链接导航，保留父菜单项自身的点击入口；只修改事件归属，不改权限或原 5 秒预算。补丁已写但 GREEN、类型/构建和浏览器复验尚未执行，不能声称导航失败已修复。独立源码副本 `G:/.fullnet-tests/fa-sidebar-unit-IHtvF4` 保留原源码、测试输入摘要与 RED 日志，两个测试根进程、全局 Vitest/Tinypool 进程与自有锁已核验释放。影响集规划只识别前端两个文件、Integration 为 none；不重复后端已通过范围。

Native 企业 2 项的新消费者准备与复用运行入口也已备好并通过 `node --check`，尚未构建或实跑；仅续验 2 项、复用已通过 50 项，不重复原生发布。其他项目的 Vite 构建曾造成共享提交内存不足；该非自有构建已自然结束，本窗未停止它，但可用提交内存仍低于共享验收门槛。取消本地 GREEN 的资源排队，将候选补丁与 6 项回归合批推送既定开发分支，由 GitHub Actions 执行 GREEN、Vue 类型/构建；取得实际结果前保持待验证，不把旧 CI 结果外推至新导航代码。浏览器与本地重型验收仍按安全边界协调，不循环重试占用资源。


## 侧边栏后续验证补记

候选a566366的客户端CI job117286538383已实际成功：侧边栏DOM6/6、Vue325文件3030/3030、类型/构建/预算通过；mock E2E147 passed、4 skipped，跳过保留。实际检出合并引用6f69005b46f7aa13805e83806b7426f2fb19b33f，与候选完整Git树相同。完整导航树2CPU受控回放exact13在原5秒内通过，只有一次push；这是受控证据，FA-04真实后端子集仍为3/6，不能由mock关闭。此前“GREEN待执行”记录是当时状态，不是当前结论。

## Native 企业双库集中验收关闭（2026-10-17）

候选仍为 `a566366922c0b9fd3b06e07a5d905e24d1195a3a`。FA-05 已覆盖 **52/52**：原 API23/Worker27 共50项按未变输入继续复用；此次只实跑原企业双库2个UID，未重跑50项或重新原生发布。CI实际生产者 `ab5d4c75e6a532a938e035a80a34933aa8fe4b70` 与 `fd08a57` 完整Git树同为 `b8bf1e4ebdc63c585d81358f49db50059a8bb8ca`；源代码、样例、模板、契约及测试闭包均与候选匹配，明确为仓库换行过滤后的Git比较，不声称所有原始字节一致。

本机原恢复工作目录、SDK10和测试镜像已不在，复用现有冻结目录 `G:/.fullnet-tests/fa-HG4DPX`，没有再复制整仓。临时官方Windows SDK10.0.100按微软SHA512校验；Release完整消费者构建 actual exit0、零警告/错误、13m04，依赖明确为MessagePack2.5.303。两个CI ELF仍为原摘要：API `bd3b0a82d000825720b92f8eebdc796682233fb5bd89d4873a0456ff7064d7f2`、Worker `bf8b03bd59ad02648c8033e9a1b672a9bfdd6b8bfe10c2b7abbcac0692875b83`。Linux运行时实际.NET10.0.13/glibc2.39，两个ELF动态依赖解析实际exit0。

跨工具调用的正式复核暴露构建身份差异：两次探针的源码/SDK/输出相同，唯一变化环境项为 `CODEX_TOOL_CALL_ID`。守卫按规则拒绝复用。改用现有正式 `withPreparedTestRun` 的同进程准备、取得heavy后严格复核、运行；保留全部环境参与摘要，不篡改旧记录、不剔除此变量。必要的新身份增量构建53.58秒、actual0/零警告错误，严格复核随后通过；正式用例阶段不补编译。

首次Linux实跑2失败/0跳过：夹具向只读 `/work/artifacts` 写启动日志，业务断言尚未到达。原TRX与日志保留，不能计通过。只调整容器输入/输出映射：实际源与消费者程序集均只读，工作目录标识和Host配置绑定实际源，日志写独立系统Temp；最小验证实际0且两个UID一致，宿主仓库未创建artifacts。未改C#业务源、测试断言、筛选器或15分钟suite预算。随后只复验这2项：

| UID | 场景 | 结果 |
| --- | --- | --- |
| `165bc6ea-7962-8390-a53f-6f91d79ae3d4` | MySQL Native API/Worker企业审批、工作簿及绑定运行态 | Passed |
| `195cda46-d1af-8881-8261-d4971a4c6498` | SQL Server同场景 | Passed |

命令为 `dotnet Full.NET.IntegrationTests.dll --no-ansi --progress off --timeout 15m --filter FullyQualifiedName~NativeEnterpriseBusinessTests --minimum-expected-tests 2 --report-trx`，实际同时指定独立Temp的results-directory与report-trx-filename。Linux discovery与正式TRX的UID集合精确相等；MTP **2/2、零失败/跳过，2m44.618s**，Docker CLI实际0。held-handle Windows父60856/birth `134367121997711863` 实际0，Job active0/total33，observer36332原身份缺席，自有normal/heavy锁已释放；新增容器差集空，foreign baseline `a39c533bf6d9`/`c584bc64d003` 保留。未停止共享服务。

本次最小证据暂存系统Temp `C:/Users/Administrator/AppData/Local/Temp/fullnet-frozen-acceptance-1792232794104`，由本批最终汇总消费后按本地清理规则回收；不在repo artifacts输出或保存整仓副本。原始正式TRX SHA256 `8323fde5d774a6eed72ad5d63a316fa9544065aa0ca5dd48e8a7cda98ce82b8a`，业务log `bc15ed6925ad3b3bca1b740b2b24912ae7ded27788d7d4c1962c553297416e9a`，父终态 `6c0a88ba93545b7d0ec9f40458672c79f63373927e00899cb3da4f4427b3bc18`，容器归还 `de3f855723ced76039d23ff13f595063a639a07b9882d7f81d3d716f8646a3f5`。首次MCR大层传输最终正常完成；主机侧官方crane备用路径600秒超时，工具与部分tar已清理，未用失败记录计通过。SDK仍供本批剩余验收使用；容量保持Capacity-not-verified。

## 仍待关闭

开发剩余 0、验收组剩 3：FA-03 真实后端双库浏览器、FA-04 Minimal 双库与数据交付 MySQL、FA-06 最终汇总。FA-05已按50项复用加企业双库2项实跑关闭；其余组不因本组通过而升级。未合并、未发布。

## 2026-10-18 前置恢复与渲染阻断修复

本批基线为 `7eaeda2290a1cefd95217475d98129b96ebe143b`，沿用既有私有目录 `G:/.fullnet-tests/fa-HG4DPX`，没有新建整仓副本。内存释放后官方 NuGet audit index/base/update 实际 GET OK，SDK10.0.402 还原 exit0、零警告错误，审计与警告失败策略保持开启。

新轮 SQL Server Minimal 完成应用 Web 构建、101个框架迁移与1个应用迁移、重复迁移零新增和独立 Worker Outbox 后，商品菜单导航仍在原5秒断言失败，业务未通过；立即停止后续重复场景。实际诊断发现响应式图标组件告警及未注册的加载指令，CPU热点包含Vite控制台对象序列化。适配器保留静态图标身份，入口按需注册ElLoading指令；红/绿回归和受控生成页浏览器均保留原点击/断言。两类告警最终均0，不能据此宣告真实后端通过。

模板资源/临时报告路径直接检查13/13；渲染直接回归23/23。最终Vue在原用例时限下使用4worker（同一8GiB/6CPU聚合预算）发现并执行326文件/3032场景：整批3031通过，唯一新增入口测试把真实模块初始化计入5秒用例而超时；只把初始化移到beforeAll，原断言与用例时限不变，失败文件1/1复验通过。汇总3032覆盖、无剩余失败/跳过，**不是一次全量全绿**；原2worker的15分钟外层超时和4worker初轮失败日志保留。最终类型/Vite及原4项包预算均exit0；主入口静态闭包gzip311597字节，已认证概览闭包400176字节，均在原门槛内。held actor19312实际exit0、Job0。

系统Temp证据根 `fullnet-fa04-navigation-20261018` 内的 `frontend-verified-summary.json` 绑定源码摘要、整批及失败文件结果；失败真实栈位于 `fullnet-fa04-20261018-7eaeda2/network-restored`，实际退出保留为4294967295、Job0，自有SQL/Redis/Ryuk及双锁已核对释放。两处旧临时目录此前原生PowerShell清理被命令策略拒绝，未执行且没有更换解释器绕过；本轮复现复用既有临时应用，不将该清理债务写成已完成。

`c6e86e5` 后续正式独立应用：SQL Server Minimal原场景整项通过，耗时20m20.958s、嵌套真实执行7m24.472s，Node0、零失败/跳过。五种浏览器权限模式完成，页面异常0、两类原渲染告警0；101框架+1应用迁移及重复零新增、独立Worker业务投影、应用升级迁移、回执缺失恢复与再次重复零新增均按原断言通过。随后MySQL仅在 `dotnet new fullnet-app` 生成阶段触发原120秒预算的ETIMEDOUT，尚未启动数据库；后继企业MySQL自动停止。保留本次失败而不增加超时。聚合actor26928/birth134367656208953198 held exit1、Job0；三只自有容器消失，共享双库ID/StartedAt/配额不变、双锁释放。正式通过1项，失败1项，未执行1项，不能汇总为三项通过。

最小只读诊断复用已有模板包，SDK402真实安装1.218s、MySQL dry-run7.202s，均原120秒预算，未复制整仓、生成新应用或启动构建/数据库。Windows实际Job2GiB/CPU1，actor12756 held0、Job0，模板包清单摘要不变；只证明解析路径当前可用，不能排除输出写入或当时资源争用，更不替代失败用例。该诊断首次fresh memory gate在CreateProcess前拒绝；内存恢复后维持原16GiB提交/12GiB物理门槛再入，未降低门槛。原始证据根 `fullnet-fa04-render-repair-20261018`：`minimal-sqlserver.tap`、`minimal-mysql.tap`、`terminal.json`、`physical-return.json`、`generation-dry-run/result.json`。

续验同一 `c6e86e5`：MySQL Minimal整项实际通过，18m40.969s（嵌套真实执行6m19.385s）、Node0、零失败/跳过。生成命令在原120秒预算内74.785s完成，未改生成实现或增加时限；五种权限浏览器、租户隔离、Worker业务事件、升级/重复迁移及失败恢复均原断言通过，自有MySQL/Redis/Ryuk均已退场。累计独立应用5/6，剩企业数据交付MySQL已自动续验，聚合actor22048仍运行；未将阶段通过写成聚合通过。

当前提交 `c6e86e5` 的CI已完成，6个执行作业（客户端、后端双库、current/legacy迁移、API/Worker Native）及1个汇总检查均成功；8个跳过检查不计通过，真实应用/后台浏览器仍按本地冻结范围继续。源码绑定及检查链接保存系统Temp的 `ci-current-complete.json`。其中GitHub客户端job117570192548已成功，正式原始日志确认Vue326文件/3032场景一次全部通过；受控E2E147通过/4跳过，跳过不计通过。该日志SHA256 `56fb7909805deef5a05a656e4f1224f87f65ce736f0beb0d8f5e7ef798497099`，与本地分阶段3032证据分别记录，不互相冒充，也不替代真实双库应用验收。

企业数据交付MySQL最后一项实际通过，11m52.603s，嵌套11m52.582s、Node0、零失败/跳过；应用导入、独立Worker、跨库数据源/工作簿及真实打印浏览器的原断言均完成。实际查看打印截图，业务标题、单据号、状态和金额可见；只代表该功能样例，不是完整后台视觉审查。累计业务范围6/6为历史3项与当前c6e86e5实跑3项的分阶段覆盖，不能称六项同源单次全绿；业务后端/迁移/契约未新增修改，共享渲染增量由当前3032组件场景及生成应用代表性样例覆盖。

最终聚合actor22048/birth134367686547349744 held exit0、Job0/total1409，执行会话实际exit0。鲜查此前SQL Server、Minimal MySQL和企业MySQL观测的10只自有容器均不存在；共享a39c/c584的ID/StartedAt/内存/CPU完全保持，两个既存外来锁保留、自有normal/heavy锁0。企业实际MySQL1.5GiB/1CPU、外部SQL Server3GiB/1CPU、Redis0.25GiB/0.25CPU，MemorySwap为各自两倍；Ryuk没有硬配额，不宣称所有Docker资源均受限。`remaining-mysql/fa04-closeout.json` 保存TAP、终态和范围哈希。原生成超时未定位到确定根因，原预算复验通过不写成代码已修复；首次失败及117.785s的生成耗时保留。

FA-03首次真实准备于04:19:00Z开始，实际日志仍显示仓库`.tmp/e2e-real-stack/build/.../debug`，说明临时加载变换没有作用于Playwright实际启动入口；只有生成变换文本/直接模块导入的旧探测，不能证明跨进程真实执行。按Release与系统Temp前置要求立即停止自有actor3028/birth134367707407781413，held4294967295、Job0/90，04:25:19Z鲜查后仅按自身PID/token释放双锁，业务测试0，foreign双库及两旧锁保持。当前完整scope仍为每库29文件/65唯一UID，不因准备失败增加范围。

本次直接修正式bootstrap：构建参数和Migrator/API/Worker程序集共用Release布局，默认编译缓存稳定落系统Temp并按工作区隔离、跨Provider复用；日志按Provider分开，支持`FULLNET_E2E_BUILD_ROOT`/`FULLNET_E2E_LOG_ROOT`绝对覆盖。CI四个真实栈作业显式指向runner.temp并上传对应日志，不上传构建缓存。新增回归在独立Node进程调用真实bootstrap，仅替代Docker/.NET SDK边界：原缺少Release的双库2失败确认为RED，修复后2通过；另覆盖默认隔离/复用、空格路径、相对路径在创建容器前拒绝和未知Host/Provider。旧角色契约仍断言独立Worker启动/清理，已改为检查正确角色布局绑定，不再依赖被抽走的DLL字面量。最终Node聚焦48/48、零失败/跳过，原始`final-entry-checks.log`保留；四个CI作业路径经真实YAML解析检查通过。未运行.NET、Docker或浏览器业务，不将替身证据计入130真实场景。

失败Debug目录已预览并核对新建时间、仅bin/obj、无链接/使用者、持柄Job0，919文件共51,676,182字节。原生PowerShell精确清理命令在执行前被`blocked by policy`拒绝；这是命令策略，不是Auto-review。文件未删除，没有改解释器、重命名或借测试清理绕过；`cleanup-command-policy-rejected.txt`保存原命令和拒绝，原失败日志仍在。首次预览的DateTime时区比较错误已通过DateTimeOffset校正，校正前未执行删除。此项与此前两个被拒绝目录均作为清理债务保留，不称全部清理完成。

开发余0、验收余2：FA-03真实后端双库浏览器、FA-06最终汇总。FA-04按上述分阶段证据关闭；正式入口修复已轻量验证，真实65×2仍待固定资源窗口。C6的7项CI成功与本次入口修复分别记录，新输入CI只按实际终态追加，不将旧CI状态当作新输入通过。未合并、未发布，容量仍为Capacity-not-verified。
