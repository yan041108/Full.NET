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

## 仍待关闭

开发剩余 0、验收组仍剩 4：FA-03 真实后端双库浏览器、FA-04 Minimal 双库与数据交付 MySQL、FA-05 Native 企业链路 2 项、FA-06 汇总。API/Worker 已通过的 50 项不再重复执行。未合并、未发布。
