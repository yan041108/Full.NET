# 日志关联与时间线开发计划

2026-10-17 状态补充：整体验收的排队进程已因等待共享窗口超时退出，原生接续未启动，不能再视为自动验收中。当前转入用户授权的[日志模块整改批次](2026-10-17-logging-remediation.md)；本文件保留时间线实现范围与历史证据，最终验收按整改后的影响集合并安排，旧通过结果不得外推到新修订。

依据：2026-10-16 用户授权完善日志模块，所有日志尽量记录可获取的 TraceId、时间、会话、用户和 IP 指纹。保持 ADR-0012 的投递路线与 B0/B1/B2 可靠性边界。

## 交付契约

关联信息在产生事件时捕获，不能在队列消费时读取原请求。TraceId 是 W3C 链路标识，RequestId 是独立请求标识；无 Activity 时不得混填。用户、租户和会话只来自已验证上下文；登录前未知值为空，认证审计自身已确认的用户/会话可补充。后台任务保留实际可用的 Activity、任务或消息关联，不推测用户身份，不将凭据加入 baggage。IP 指纹沿用现有 SHA-256 契约，属于可关联的假名化数据，不能宣称不可逆匿名化。

统一 UTC 发生时间；异步落库不改写发生时间。跨实例时钟不构成因果顺序证明，使用 SpanId/ParentSpanId 表达已知父子关系。

## 完整功能批次

- [x] 采集：共享不可变关联快照、HTTP 可信上下文、B2 所有出口、审计 DB 写入、认证和领域日志覆盖清单；防止请求退出后的上下文泄漏。
- [x] 存储：SQL Server/MySQL 成对增量迁移，保留旧记录可空语义、中文元数据、AOT 参数与物化。队列条数/字节/参数预算同步更新。
- [x] 检索：TraceId 或时间范围加会话/用户/IP 指纹；有界查询和稳定排序，数据所有者负责读取，沿用每种来源的权限与租户边界。
- [x] 诊断展示：跨来源结果明确来源、覆盖范围和截断/不可用状态；文本只读结构化 JSON 行，不无界遍历服务器文件。ES 连接必须显式配置，不能默认已启用或已覆盖。
- [x] 文档：更新 code-wiki 与日志模块手册，区分已实现采集、历史缺失、当前可检索来源及尚未接入来源。
- [ ] 集中验证：上下文隔离/异步快照/匿名与认证/脱敏/时间顺序单测；双库写入读取和迁移重跑；权限与查询限制；相关客户端与 Native AOT 验证。

## 验证与状态

起始代码基线：`d2e40bfa625c32d1a6890779664d23a72f3a961e`，共享工作区任务快照 `logging-correlation-timeline-20261016`。保护快照前已有修改，不提交或部署。

状态：功能实现与文档已补齐，核心本地专项通过，扩展整体验收尚未完成。上方勾选表示实现完成；不由功能测试推导容量或全量日志无损保证。

已执行：

- 后台 TypeScript 类型检查通过；客户端契约/导航测试 7/7、后台时间线/权限/导航测试 10/10、详情抽屉与时间线回归 11/11（集合有重叠，不相加）。
- SQL 命名、注释与安全结构检查 20/20，通知查询修复后重新执行通过。
- UnitTests 与 IntegrationTests Release 构建均为 0 警告、0 错误；新增 Native AOT 时间线断言也已编译。
- Host.Api AOT 静态分析通过，0 警告、0 错误；随后恢复默认 JIT 还原图。
- 架构测试 235/235 通过；Integration 分片发现 1273 项，无遗漏或重复。
- 日志专项单测：发现 356 项，355 通过、0 失败、1 项 Linux FIFO 专属测试在 Windows 跳过。
- 审计 API 双库与迁移恢复：16/16 通过、0 跳过；包含全部 16 个数据库来源的真实 SQL、时间线权限/交集筛选/排序、显式 TraceId 兼容，以及 262–271 迁移重复执行、部分列已存在时恢复和历史数据保留。

后端使用官方 .NET SDK 10.0.402，在 `G:/.fullnet-tests/log-correlation-20261016-batch1` 冻结副本验证；`.tmp/log-correlation-input.json` 记录与源工作区对应的 SHA-256。核心证据为 `.tmp/logging-unit-validation3.txt`、`.tmp/logging-integration-runtime3.txt`、`.tmp/logging-native-contract-build.txt` 和 `.tmp/logging-static-validation3.txt`。

首轮只读复核修复已知问题后，续审另外发现 AI 后台已验证会话未进入日志快照；本轮继续补齐，见下方记录。只读复核不代替运行验收。

复核与测试修复：敏感模板分支保留安全关联字段、历史认证会话回退、文本枚举/读取预算、JSON 快照与旧 TraceId 查询一致性；通知尝试通过所属意图确认 Host 范围；保留出站/认证显式 TraceId 旧契约。首轮双库的 4 项失败均已修复并通过同组 16 项重跑，不把旧失败记录计为通过。

剩余验收：受影响模块完整回归、Linux Native AOT 外部进程（含新增时间线断言）、Linux FIFO 用例、当前 Windows JIT 通过不能替代原生项；完成后再勾选集中验证。测试分片已按实际发现的 1273 项更新登记，其中本任务新增 4 项，其余差异来自工作区既有测试，未删减测试。架构检查要求 SQL 声明可唯一定位，因此 10 个来源声明类已统一增加所属模块前缀；此项仅重命名，235 项架构重跑通过。

`git diff --check -- <本任务148个变更路径>` 通过；仍在 `main`，未提交或部署。

可复现的专项检查：

```powershell
pnpm --filter @fullnet/admin typecheck
pnpm --filter @fullnet/client-contracts test -- tests/log-timeline.test.ts tests/navigation-catalog.test.ts
pnpm --filter @fullnet/admin test -- src/views/LogTimelineView.test.ts src/views/components/AuditLogDetailDrawer.permissions.test.ts src/navigation/catalog.test.ts
pnpm --filter @fullnet/admin test -- src/views/components/AuditLogDetailDrawer.test.ts src/views/components/AuditLogDetailDrawer.permissions.test.ts src/views/LogTimelineView.test.ts
node --test tests/naming/sql-comments.test.mjs tests/naming/sql-naming.test.mjs tests/sql/sql-safety.test.mjs
node scripts/testing/run-dotnet-test-suite.mjs unit --selection logging-delivery --reuse-build
```

本次后端实际通过冻结目录 `.tmp/run-log-validation.mjs unit` 执行同一矩阵选择器，并将构建并行限制为 `-m:2`；日志在 `.tmp/logging-unit-validation3.txt`。首次常规入口构建失败的记录不作为通过证据。

## 后续验收任务

| 顺序 | 任务 | 通过条件 |
| --- | --- | --- |
| 1 | 运行本次影响集完整回归：`pnpm test:slice -- --snapshot logging-correlation-timeline-20261016`，按资源预算分组集中执行 | 涉及模块的正式双库写入路径均通过；不能用已通过的 16 项审计专项代替全部模块回归 |
| 2 | Linux Native AOT 发布与外部进程门禁，运行新增时间线断言；补跑 Linux FIFO 用例 | 两种数据库下，时间线序列化、所有者查询和关联字段均在真实原生进程通过；Windows discovery/skip 不算通过 |
| 3 | 已完成：真实 HTTPS ES 查询与传输边界回归 | 真实 ES 集成 1/1 通过；只读 Key 拒绝写入和越权索引，三类会话、时间范围、条件交集、截断、无效 Key 与正文隔离通过。另有 6 个传输边界单测通过，覆盖 ES 部分响应、响应大小上限、失败与取消 |

上述任务不需要重新设计或再次确认功能范围；结果直接回填本计划。ES 读取保持显式配置且默认关闭，不因本次 JIT 专项通过而自动启用。未执行完整影响集和原生验收前，不宣称本次扩展全部收尾。

## 本轮续验收记录

- 新增真实 ES 夹具与正式 API 端到端用例，未修改生产 TLS 校验。临时测试 CA 仅注入测试具名客户端，校验信任链和主机名，不安装系统根证书。
- 当前日志专项单测为 362 项：361 通过、1 项 Linux FIFO 用例在 Windows 跳过；UnitTests/IntegrationTests 编译均为 0 警告、0 错误。证据：冻结目录 `.tmp/logging-acceptance-unit.txt` 与 `.tmp/logging-acceptance-integration-build.txt`。
- 真实 HTTPS ES 集成 1/1 通过、0 跳过；源码指纹 `3cdf90a819b01f87ba801971565211cbae82709fd20e51d3c79d038f10685e34`，证据在 `artifacts/logging-correlation-20261016/acceptance2/es-https.log`、后续验收目录的 `es-https-reused.trx` 与 `reuse.json`。本轮新增 1 项 Integration，正式分片已实际发现 1274 项并验证无遗漏/重复。
- 验证使用 SDK 10.0.402；两个解析工具锁文件中的 SDK 隐式 ILLink.Tasks 已同步为 10.0.13。Linux 验证的临时锁文件写入独立验证目录，源码前后按 SHA-256 核对。
- 首次 ES 执行遇到机器提交内存不足，主动停止本任务进程，容器与锁已释放；没有计为通过。随后重试才取得上述真实 ES 通过结果。
- 治理检查曾因独立验证副本缺少历史提交 `ec6eae92` 而失败；已仅恢复该副本的只读 Git 对象引用与私有索引，未重置源码。最小复现的两项冻结检查已重跑通过，完整治理检查继续执行。
- `acceptance3` 在执行测试前停止排队，以先补齐续审确认的后台漏采；未计为通过。后续使用新封存输入继续集中验收，只管理本任务进程，不停止其他任务或清理其他任务的锁。
- 后台补采：Agent 在会话验证成功后建立关联作用域，并保留到异常记录结束；文本/PDF/DOCX 索引在完整授权后记录快照，按任务独立 DI 作用域传回外层失败日志。Worker 专用入口显式注册快照载体，避免仅 API 注册造成已领取任务失败。刷新会话与应用会话分别映射，保留实际 Worker Activity，不采集安全戳、不猜测 IP 或中心会话。
- 最小缺陷复现 6 项先失败、修复后 6 项通过；新增 12 项回归覆盖执行、失败、拒绝和上下文恢复，另增强 Worker 专用注册与作用域隔离断言。扩展回归首次因验证脚本覆盖 Windows PATH 导致 Node 子进程不可用，已定位并修复脚本环境；失败记录保留，未计为通过。

## 集中补验检查点

- 日志及相关 AI 回归共发现 1217 项，1216 通过、0 失败、1 项 Linux FIFO 用例在 Windows 跳过。新增后台补采用例全部通过；当前仍不能用 Windows 结果代替 Linux 用例。
- 修复影响集选路遗漏：ObservabilityAdmin 的筛选包含模块命名空间，新增 ES 时间线集成用例可以被正式影响集发现。该缺陷先有失败复现，随后选路测试 43 项通过，完整工具链与治理 202 项通过、无跳过。
- 本轮冻结副本的集成构建为 0 警告、0 错误；重新构建后的 857 个运行产物与中断前逐字节一致。修改的两处选路脚本不改变应用运行代码。
- 模块组原发现 395 项，因测试进程 1.5 GiB 堆上限及随后宿主提交内存不足中断，没有完整 TRX 终态。使用匹配版本的官方 MTP 序列化器校验流文件，恢复 107 条完整记录：88 通过、17 失败、2 项外部发送配置缺失而跳过；288 项未取得可采信结果。恢复逐项核对 UID、命名空间、类型、方法、时间与时长，原始失败及流文件保留，未把中断整窗记作通过。
- 后续只补跑失败及未观察到的测试和新增 ES 用例。每个新进程最多选择 24 个方法，托管堆预算调整为 4 GiB，继续使用工作区互斥、同机重型锁及内存保护；已完成的 88 项只在源码和运行产物身份核验一致时复用。两项外部短信/SMTP 真实发送保持未验证，不计入日志能力通过数。
- 证据入口：`artifacts/logging-correlation-20261016/acceptance5/recovery-proof.json`、`resource-handoff.json`，以及 `acceptance6/reused-evidence.json`、`runtime-pin-recheck.json`、`state.json`。尚未完成的模块、真实日志链路和 Linux 原生验收继续保持待验收状态；没有新增已确认但未修复的日志功能缺口。
- 新冻结输入已重新取得真实 HTTPS ES 1/1 通过、无跳过，选路补验有效。Linux SDK 与源码前后指纹核验通过，但 API 原生发布在 ILCompiler 扫描阶段因原 2 GiB 托管堆预算不足失败；Worker、Linux FIFO 和原生运行未执行，不能记为通过。失败与已清理资源的回执分别保留在 `acceptance6/native/publish-api.log` 和 `resource-handoff-final.json`。
- 原生重试已准备使用 8 GiB 验证进程堆预算、12 GiB 容器上限及原 2 核限制，复用已核验源码和编译缓存；不修改应用代码或警告门禁。该准备不代表执行通过，后续按共享测试资源队列运行。
- 2026-10-17 续验：上述原生重试实际进入编译，但宿主可用提交内存降到约 1.03 GiB，触发本任务内存保护，随后 Docker 引擎也不可用。未取得 API 发布终态或容器内最终源码校验，不能登记为通过，也不能据此认定应用代码存在编译缺陷。引擎恢复后已核对自有进程、容器、卷挂载和测试锁全部释放，纯资源回执为 `acceptance7/resource-handoff-final.json`，SHA-256 `68470dd751c5f1e1e99a4635be3bc8223537eef3969ddf2e6348905dd8f2b150`。
- `acceptance8` 的请求链路分片达到原 8 分钟应用上限后取消，正式输出为 3 项通过、0 项失败，未形成整组 TRX，不能登记分片通过。匹配版本的官方 MTP 序列化器从原始流完整恢复并逐项校验 3 条 Passed 记录：Collector 实时投递、Collector CRI 回放链路和应用直发经独立消费者写入 HTTPS ES。流文件无残尾，UID、类型、方法及时间均与正式发现一致；证据为 `acceptance8/request-shard-recovery-proof.json`。其余 2 项请求场景未完成。
- `acceptance8/resource-handoff-final.json` 确认本任务进程、容器和测试锁均已释放，后续缓存窗口已通过正式锁接续。`acceptance9` 已准备并排队：先补 305 项模块余项，再执行剩余请求场景、路线比较、秘密边界和 smoke；慢速日志场景逐项运行，保留原分片超时上限，不修改产品行为或放宽断言。仅复用源码、运行产物与官方完整记录身份均核验一致的结果，不重复已通过的构建、单测及 ES 用例。完整终态取得前保持待验收。
- 排队期间复核确认新的误归属缺陷：普通 B2 日志在可信关联字段为空时，会保留调用方同名属性。隔离程序实际复现 `trustedUserId=null` 且 `callerUserIdRetained=true`；新增 4 项回归先取得 3 失败、1 通过。已将关联键改为“可信值覆盖、缺失值删除”，避免匿名或后台日志通过同名属性被归属到错误用户、会话或链路；业务被操作对象使用独立字段。定向代码审查未发现新的实际问题。
- `acceptance9` 在取得重型锁或执行测试前已停止，仅释放本任务排队所持普通锁，其他任务窗口保持不变。上述修复改变了运行产物；旧修订的模块及链路结果保留为历史证据，不冒充新修订验收。新修订先执行日志与后台关联组合回归，再集中执行受影响模块及链路。
- 最新修订组合回归已实际正常退出：1221 项中 1220 通过、0 失败、1 项 Linux FIFO 在 Windows 跳过，4 项新增误归属回归均通过；构建为 0 警告、0 错误。命令为 `node .tmp/logging-metadata-regression.mjs verified`，证据在 `metadata-verified/build.log`、`tests.log`、`unit.trx`。此前 `metadata-green` 虽为同样的 1220 通过/1 跳过，但验证脚本误将跳过项计入最低通过数而退出 9；该记录保留，不作为命令通过证据。修正的只是验证脚本计数，Linux FIFO 仍须真实 Linux 执行。
- 新修订集中集成批登记为 `acceptance10`：重新发现受影响模块，按每进程最多 24 个方法执行 394 项本地模块用例；两项需要外部 SMTP/短信真实发送配置的用例不执行，不计入通过。请求 Kafka、路线比较和秘密边界逐项运行并保留原超时上限，另执行 smoke。此批不复用旧修订的集成通过数；Native 与 Linux FIFO 另行验收。
- `acceptance10` 最新取得工具链与治理 202/202、集成构建 0 警告/0 错误，以及 5 项真实模块用例通过。原按测试类分批会启动 109 个进程并重复初始化数据库，因此在测试进程自然退出 0、夹具已清理后停止调度器，核对源码、TRX 和运行产物并释放自有锁；未中断测试，也未将整批记为通过。纯资源回执为 `acceptance10/resource-handoff-final.json`，SHA-256 `3042a092216148404fef435acd215bf203b6cfe6c0c180f2df5f95231921f9d1`。
- 接续批为 `acceptance11`：源码及运行产物不变，复用的仅为本修订逐项核验的 5 项通过记录；其余 389 项跨测试类按每批最多 24 项合并为 17 批，随后执行原有日志场景和 smoke，不删除用例或放宽断言。接续进程已启动，当前在所有测试锁之外等待已有共享验收整窗结束及资源清理，不能把等待状态当作通过。整个时间线扩展仍待完整集成、Native 与 Linux FIFO 验收。
- 已准备并启动 `acceptance12` 接续等待：只有 `acceptance11` 同一源码验收通过且释放锁后才进入原生资源队列；失败不会继续发布。Linux Unit 构建及 FIFO 用例安排在 API/Worker 发布之前，随后执行完整 AOT 分析、正式 Native 架构选择集、发布与核心原生运行门禁。下一次编译尝试使用 4 GiB 托管堆、8 GiB 容器、2 核，要求宿主可用提交内存至少 12 GiB、可用物理内存至少 8 GiB；原警告、用例和运行断言不变。旧缓存先按原源码清单校验，再覆盖当前封存源码，并在结束时重新核验；不得与其他容器同时挂载该缓存卷。脚本语法和封存输入检查见 `acceptance12/preflight.json`，准备及排队状态不计为执行通过。
