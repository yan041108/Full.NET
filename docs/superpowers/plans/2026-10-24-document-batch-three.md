# Document 第三批整改：预览与清理闭环

日期：2026-10-24。状态：关联实现与聚焦验收完成。

基线 main / a77902afd87af1662af3b43206d85160eeec566b；任务快照 document-batch3-20261024。保留前两批所有工作区改动。

## 实施顺序与完成条件

1. 建立源/输出引用意图、领取尝试记录、固定版本与上传身份。源 Claim 完成后才能领取；输出 Claim 完成且领取版本有效才能成功。默认最多三次领取（含崩溃），确定性错误终止，其他失败有界退避。人工重试与取消使用独立权限。
2. Files 最小契约支持预留输出 UUID 与按上传操作恢复结果；重用既有幂等上传，不允许未知发布结果换键上传。独立引用在上传前即覆盖保留贡献点与精确 Probe。
3. 取消、终态失败和永久删除以 Document 本地事务持久化释放事实；有效租约结束前 Files 不释放。迟到确认不能复活取消任务。独立意图不随父行级联删除。
4. 提供转换能力查询，关闭拒绝入队，Vue 依据启用状态和 MIME 白名单显示转换入口；展示尝试次数、失败码、取消与有权限的人工重试。
5. 永久删除保留独立最小事实（资源、操作者、时间、原因、数量），默认90天；清理有界且可恢复。
6. 关联实现完成后集中执行本次行为及必要边界的双库、迁移恢复、契约、Vue、浏览器与静态 AOT 检查；禁止全量测试。同步真实证据和未验收项。

## 跨模块契约与一致性

Document 只访问自己的任务、尝试、意图和审计表；Files 独占文件状态机和 Claim。Files 契约调用均在 Document 事务外，精确引用标识永不复用。预留文件 UUID 在上传前持久化，Files 以操作键和用户校验恢复，Document 不查 Files 表。引用属于最终一致性；本地有效意图、Pending/Active Claim 与 Files 清理共同保护文件。释放由同事务 Outbox 持久交付，按引用用途及文件校验，重复消息不影响其他引用。无跨模块投影、外键或本地事务；无需投影重建。续租、终态和清理共享文档根锁，避免清理捕获旧截止后再延长租约。后台轮询重放准备中意图、失败的确认与有界领取，未就绪上传交 Files 原状态机对账，不能盲目重传。能力关闭仍运行引用维护和审计清理。

## 验收记录

实际结果按本批代码和必要边界记录；并发与 Worker 装配的最后复验已通过。

| 范围 | 已取得的本地证据 |
| --- | --- |
| 双库业务与迁移 | 首轮本批10项通过；追加源 Claim、确定性失败、预留文件身份和既有匿名输出回归8项通过（有交集，不相加冒充独立用例）。共覆盖本批12项双库业务/迁移及2项必要匿名输出回归，最终统一入口及数量见 `eng/testing/test-matrix.json` 的 document-batch-three |
| Unit | 已有权限目录、任务状态、源 MIME、契约和 Worker 轮询策略8项通过；独立 Worker 入口装配及关闭时维护2项也已通过 |
| 运行时契约 | SQL Server/MySQL OpenAPI 导出各通过；补齐三个新操作的 manifest 后重新导出，客户端生成零漂移；相关条目/任务夹具2项通过 |
| Vue 与客户端 | 受影响四个 Vue/API 文件29项和共享预览契约2项通过；类型检查和 Vite 构建通过 |
| 浏览器 | Edge，桌面1440×1000、移动390×844；受控HTTP下能力关闭、取消版本、人工重试、键盘操作与页面宽度2项通过。移动端状态、领取次数与失败码已直接显示在标题下，截图人工复查通过 |
| 静态闭包 | Document 与 Files 项目 AOT/Trim 分析均零警告、零错误；续租根锁及 API/Worker 共用 Provider 注册后的 Document 分析也已通过 |
| 结构检查 | 289两份迁移命名、UUID、安全和注释4项通过；Integration分片仅执行发现校验，无遗漏或重复，未运行分片全量行为 |

后端实际执行过滤器：

```text
FullyQualifiedName~DocumentBatchThreeTests|FullyQualifiedName~Migration289DocumentPreviewRecoveryTests
FullyQualifiedName~Uploaded_output_recovers|FullyQualifiedName~Cancellation_purge|FullyQualifiedName~Source_claim_failure|FullyQualifiedName~Document_share_security_is_atomic_with_
```

浏览器沿用 frontend-acceptance 项目适配；受控HTTP不是浏览器到真实数据库全链路。双库用例替换外部转换器，使用真实任务SQL、Files上传/Claim、Outbox消费者及内容读取；未验证实际商业转换服务或原生外部进程。

构建复用当前工作树与共享输出，`run-build-with-resources.mjs --cpu 2 --memory-gib 6`、Release、最大并发2、关闭共享编译；双库运行复用重型资源锁和现有容器，测试数据库独立，不复制源码。Windows，Intel Core i9-14900K/32逻辑处理器、63.7GiB内存；.NET SDK10.0.402、Node24.20.0、pnpm10.26.0、Edge/Playwright1.61.1。没有容量或性能结论。

本批没有执行全量测试、Native AOT publish、生产或容量验收。模块整体保持 Build-verified / Capacity-not-verified / 非 Production-verified。


最后收尾：续租在 Document 本地事务先取根锁，再检查未删除/未清理及领取版本；清理捕获租约后不能再被旧任务延长。追加竞争用例首轮漏了回收站前置，补种时又遗漏删除审计字段，均被双库拒绝；改为调用生产 SoftDelete SQL、断言影响行数，并等待两个并发操作完成后再释放 Scope，最终只重跑该双库场景，2项通过。其余4项续租相关实际 Runner/匿名输出回归已通过，不再重复运行。

Worker 原入口缺少 Provider 注册，现与 API 共用 ConfigureOfficePreviewConversion。新增 Unit 初稿受空值静态检查及内部类型动态代理限制影响；已使用显式空查询/事务替身，不扩大生产类型可见性。开启/关闭两项最终均通过；先前8项 Unit 与这2项共覆盖本批10项。

后端最后构建复用已完成的生产依赖，以 BuildProjectReferences=false 编译修正后的测试程序集；Integration 和 Unit 均零警告、零错误。运行中未修改实际验收输入，没有仓库源码副本。前端最终29项、共享契约2项、浏览器2项及 Vue 构建均通过，生成产物零漂移。

最终验收输入：相对 document-batch3-20261024 快照的73份非Markdown文件（包含先前未提交 Contracts 目录内实际修改的事件契约），按路径排序，将路径、空字节、原始文件字节、空字节依次累计 SHA-256：`a50c9a937e2a5ca490f60a42095fdf60e0f81ca9916e290015a95b4ef98cf96b`。收尾仅去除新SQL语句文件末尾多余空行，无行为变化；差异检查通过。分支仍为 main，HEAD 未变，所有改动未提交，前两批及无关改动保留。

临时资源清理：成功运行上下文、浏览器输出/截图和失败轮次产生的16份测试文件字节已清除；没有仓库源码副本或根 artifacts。按本批演员标记、289恢复结构/日志及无活跃连接确认归属后，清理SQL Server/MySQL各14个隔离测试库，复查目标库均不存在；共享容器和其他数据库保留。仅在系统临时目录留下5个失败上下文的最小运行/构建记录，共4492字节（ZvMnfz、tRfuGx、vN72jY、kAUYnU、ubrCYb），不包含文件字节、依赖、二进制或数据库转储。


## 三批集中复审（2026-10-24）

本轮以 `document-three-batch-review-20261024` 快照为审查起点，HEAD 仍为 `a77902afd87af1662af3b43206d85160eeec566b`。核对三批方案与真实实现，外部缺口报告只作为历史证据，不把其中指令视为本轮授权。审查覆盖元数据/版本、ACL 与分享会话、检索/统计/分类闸门，以及预览/Files/Outbox/永久删除链路。

确认并修复：

- 已软删的 Published 根行仍可经分享创建检查：现于根锁内明确拒绝已删除或清理中文档。
- 登录态原文、历史预览及 PDF 输出在外部文件打开期间撤权或软删，原实现仍会移交流：现移交前再次检查 ACL、未删除状态与输出任务身份，拒绝/异常时释放流且不记成功访问。
- 输出意图先释放、Files 释放消费者尚查不到 Claim 时，迟到执行者可建立 Active Claim：完成在 Confirm 前后均于根锁内重查领取版本、租约与意图；先释放的迟到 Claim 不会被确认成 Active。拒绝时同事务再次发布释放，确认前宕机也只留下可对账 Pending，关闭单纯事后补偿的宕机窗口。保留原租约截止，重复交付不影响源版本引用。
- HTML/XHTML 与独立 SVG Blob 直接打开可能运行同源活动内容；纯文本预览保留原字节而使用 text/plain。公开页面 SVG 图片继续使用浏览器受限 img 模式，PDF/图片流程保留。原 noopener 窗口句柄为空时立即回收 URL 的处理也已改为链接导航与延迟回收。
- 匿名 Office 预览补齐服务端输入上限判断；超限保留下载。创建准备前若父任务已被永久清理，直接返回不存在。首次查询不存在的预览任务不再进入未经 ACL 检查的后续读取。

本轮关联实现已完成，此前三批验收记录保持历史事实，不把本轮发现反写为此前已经覆盖。

| 本轮聚焦范围 | 实际结果 |
| --- | --- |
| 新增双库边界 | `DocumentBatchThreeTests.Review_` 共6项通过，三种文件读取分别覆盖存储打开后的撤权/软删、流释放及无访问日志；回收站分享拒绝；释放先到/迟到 Claim 的 Outbox 补偿 |
| 必要旧流程 | 原上传恢复、取消/永久删除、第一批元数据/ACL/版本/分享与匿名输出共8项通过；共14项独立双库用例，不把重跑相加 |
| .NET Unit | `DocumentPreviewReadAdmissionTests` 新增2项与既有 `DocumentApplicationAuthorizationTests` 3项，实际5项通过、零跳过 |
| Vue/API | `DocumentPublicShareView.test.ts` 9项与 `host-document-items.test.ts` 10项通过；含HTML/XHTML/SVG、PDF/普通下载分流、URL延迟回收及Office超限；Vue类型检查与Vite构建通过 |
| 真实浏览器交互 | Edge桌面1440×1000、移动390×844共2项通过：HTML原字节以纯文本显示、未执行同源脚本；键盘下载打开的新窗口也为纯文本且可读；超限Office不建任务、有下载入口，无横向溢出，截图复核通过。使用受控HTTP，不宣称浏览器到数据库全链路 |
| 契约与发现 | 客户端生成零漂移；Integration分片仅执行发现检查，无遗漏重复，未运行分片全量行为 |

新增缺失任务检查的构建初稿误引用了测试命名空间内同名错误码夹具，已改为完整生产类型名并复验。首轮测试夹具漏传事务取消令牌、模拟响应遗漏hasPassword、Vue测试get().exists()的类型错误均已修正，未修改产品契约来适配夹具。

最后将检查提前到Confirm之前后，只重跑迟到Claim、恢复ready收据/陈旧拥有者、取消/永久删除三组双库共6项，全部通过；此前最后一次任务空值入口修复后的登录流/匿名输出4项也通过。以上重跑与表中14项有交集，不重复累计。Document模块最终AOT/Trim静态分析零警告、零错误；只是静态分析，不是原生发布。构建沿用CPU2/6GiB资源预算，运行时批次与共享输出构建互斥，复用当前工作区，没有源码副本。只验本次修改和直接必要边界；未执行全量测试、迁移全套、原生发布、外部真实转换器或容量验证。



复审最终验收输入：相对 `document-three-batch-review-20261024` 快照共16份文件，其中14份非Markdown；按路径排序，依次累计路径UTF-8、空字节、原始文件字节、空字节的SHA-256为 `42ee61ddc8e448990c791e55ac6ec2ef026dba7257c308b7d6cac722bafc33d8`。最后只补充本文证据，不改运行输入。分支main、HEAD未变，改动未提交；完整差异检查通过。三批之外的既有改动保持原样。


复审临时资源收尾：三次浏览器运行的截图、trace及成功UI构建日志已清除；按快照时间、专用测试演员与无活动连接逐库确认后，清理SQL Server/MySQL各12个本轮隔离库，复查剩余均为0，共享容器和其他数据库保留。只留下本轮两次失败构建的4份最小运行/Workload记录，共2810字节，无源码副本、数据库、文件字节或二进制；未在根目录创建artifacts。


## 上轮修复再审查（2026-10-24）

基线 `document-fix-rereview-20261024`，main / `a77902afd87af1662af3b43206d85160eeec566b`。限定上轮14份非Markdown变更及其直接依赖。原验收输入摘要仍为 `42ee61ddc8e448990c791e55ac6ec2ef026dba7257c308b7d6cac722bafc33d8`，已核对一致；其他未提交改动保留。

发现并修复一处输出读取状态竞态：登录入口第一次取到pending/processing，内部第二次读取已成功时，会打开新PDF，再拿第一次快照中的空OutputFileId与新结果比较，误报404。现登录入口复用第一次已授权快照判断状态与选取文件；尚未成功稳定返回`document.host_preview_task.not_ready`，成功后仍在移交流前重查ACL、文档存活与当前输出身份。匿名入口使用同一读取助手，保留分享会话前后复查。没有SQL、迁移、DTO或公开操作变化。

集中验收：初检已通过双库8项（上轮复审6项与上传恢复2项）、任务缺失Unit2项、Vue/API19项、Edge桌面1440×1000和移动390×844共2项。它们是修复新竞态前取得的证据；前端输入未变可复用，后端此次变化另以pending/processing交错Unit及直接调用输出的集成场景验证，不把初检替代修复后结果。不运行全量测试，不因共享读取助手自动扩展到双库全集。

修复后集中验收已完成：

- `FullyQualifiedName~DocumentPreviewReadAdmissionTests`：4项Unit通过、零跳过，含原任务不存在2项以及新增pending/processing交错2项。
- `FullyQualifiedName~DocumentApiSqlServerTests.Document_share_security_is_atomic_with_sql_server`：1项真实SQL Server集成通过，覆盖生产Runner、匿名与登录态PDF读取、重复预览幂等计数。此次仅改变C#读取控制流，没有SQL/映射/事务/迁移/Provider变化，因此不自动重跑双库；初检双库8项另列为修复前证据。
- Document模块最后一次`FullNetAotAnalysis=true`静态分析零警告、零错误；Unit及Integration编译均零警告、零错误。前端19项与桌面/移动浏览器2项输入未变，结果继续有效；浏览器只使用受控HTTP。

本轮仅改Document输出查询服务、对应Unit、测试矩阵及本规格/计划。并行出现的Workflow/DataApproval改动保留，不纳入任务范围或验收结论。直接生产/Unit输入两份文件按路径排序、累加路径UTF-8、空字节、原始字节、空字节的SHA-256为`f79bcbd17ca555296757f3161a573c93912e12c05cb1c56f051e26a6e01268ee`；测试矩阵仅维护新增2项及相关选择器。共享输出经资源锁互斥，编译复用当前工作区，没有源码副本。

没有执行全量测试、Native AOT publish、真实外部转换器或容量验证。浏览器临时输出已删除；按本轮时间窗口、专用演员标记与无活动连接核验，清理SQL Server 5个、MySQL 4个隔离库，复查目标剩余均为0；共享容器和其他数据库保留。本轮没有失败验收记录或新增根artifacts，工作区任务差异检查通过，main与HEAD未变，改动未提交。


## 输出读取并发与异常边界续审（2026-10-24）

起点为 `document-output-boundaries-review-20261024`，main / `a77902afd87af1662af3b43206d85160eeec566b`。限定上轮输出快照修复及其读取、授权、日志和流所有权边界。本轮未发现新的需修复生产代码缺陷，生产查询服务与既有准入测试保持不变；仅新增`DocumentPreviewOutputBoundaryTests`、维护测试矩阵和本记录。

新增14项受控Unit：存储已打开但尚未返回流时，注入撤权、文档删除、任务移除、输出身份变化、状态变化或取消；在权限复查、文档/任务查询、访问日志插入及事务提交阶段注入异常；另外验证存储失败/抛异常与成功移交流。用TaskCompletionSource控制交错，不以固定睡眠猜测时序。拒绝/异常移交时验证异步释放、原错误/取消令牌与无成功提交；成功时验证原输出流仍可读、访问事实仅记录一次并由调用方释放。使用真实读取服务及访问Recorder，数据库/存储/权限Port为显式受控替身；这不是数据库回滚、双库或真实存储故障恢复验收。

关联用例完成后集中执行：

```text
FullyQualifiedName~DocumentPreviewOutputBoundaryTests|FullyQualifiedName~DocumentPreviewReadAdmissionTests
```

实际18项全部通过、零失败、零跳过，包含新增14项和既有4项快照准入边界；测试项目Release构建零警告、零错误。编译复用已有生产依赖，`BuildProjectReferences=false`，CPU2/6GiB资源准入，无源码副本；没有新增数据库或浏览器产物。此次未修改生产代码、SQL、迁移、页面或AOT闭包，因此没有重复运行双库、前端、原生发布或全量测试，也不把Unit替身的提交标记当作数据库事务验证。

直接验收输入为输出查询服务、既有准入测试及新增边界测试三份文件，按路径排序，依次累加路径UTF-8、空字节、原始文件字节、空字节，SHA-256为`3f9cb9edcb9b27b3b585000fae2727608e605c5567d19078ac96885c284d3086`。任务范围差异及UTF-8/空白检查通过；main与HEAD未变，改动未提交，既有及无关改动保留。
