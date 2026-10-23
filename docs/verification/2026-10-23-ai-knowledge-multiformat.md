# AI 知识库统一解析与多格式验收（2026-10-23）

## 范围与输入

用户授权将 F01 剩余内容与关联 F02–F05 多格式功能合批实现、集中测试，通过后提交本地 main，不推送。Git 基线为 `426e27442687478272342fcd85ee2f501f0a8211`，任务快照为 `ai-multiformat-20261023`。K01 之后的知识库产品完善队列不在本批范围。

本批实现严格完整的解析协议、真实格式工具和离线 OCR、原生来源坐标、策略冻结、完整计划及向量证明、队列恢复/激活/召回/答案依赖，以及上传和来源展示。新答案统一写入 v3，保存全部来源依赖；历史 v1/v2 继续按原版本读取。双库迁移 277 保留原载荷与费用，将旧 DOCX 清单表改为统一文档清单表；旧线格式需要显式重建，不能猜测解码或报告就绪。

最终解析镜像为 `sha256:7e7dadcf1aa44bba500e0a311bbc145c5b3f87025fdc73c0b4782f7013caafa4`。运行时身份直接进入计划摘要，不使用可变标签。运行边界为非 root、只读、无网络/宿主挂载、2 CPU、3 GiB、128 PID、私有 IPC 和有界 tmpfs。超限、低置信度、取消或不完整解析不产生可用部分索引。

## 已获得的实际证据

环境为 Windows、.NET SDK 10.0.402、Docker Linux 容器；主机有 20 个逻辑处理器，测试显式限定 .NET 并行度为 2。托管构建申请 1 CPU / 1.5 GiB；解析容器 2 CPU / 3 GiB；最终 Native 编译 SDK 容器 2 CPU / 10 GiB，原生运行验收 SDK 预算 2 CPU / 6 GiB；Vue 构建 2 CPU / 2 GiB。所有临时输出放在系统临时目录，不复制整仓，不创建根目录 artifacts。以下结果不代表生产容量或通用文档识别质量，容量状态保持 `Capacity-not-verified`。

| 检查 | 实际结果 |
| --- | --- |
| AI 单元：受控 Release 构建后运行 `Full.NET.UnitTests.dll --filter FullyQualifiedName~Full.NET.UnitTests.Ai. --minimum-expected-tests 5216` | 5216 成功，零失败/跳过；构建零警告/错误，测试 3m13s |
| v3 混合来源依赖回归：`AiKnowledgeAnswerLineageTests` | 最终夹具 35 成功；证明只引用 TXT 时，未被引用的多格式来源仍进入依赖 |
| `tests/ai-parsing/run-native.mjs`，最终镜像 | 50 个真实格式/策略场景全部成功，逐项核对中文、英文、表格、原生坐标与帧 |
| `NativeDocumentProbe.cs.fixture` 编译的 linux-x64 Native AOT 消费端 | 最终镜像的 50 场景全部通过生产解析/分块/清单/向量/坐标/答案依赖/公开 JSON 链路；断言禁止动态代码，原生程序 SHA-256 为 `5635fac2aee022af3efa786e54c9a20f3f45df045ac169fc5ff8419b579e17c7` |
| Python 结构回归 `test_document_parser.py` | 18 成功 |
| Node 隔离控制器边界回归 | 22 成功 |
| 实际隔离/失败探针 `run-native-failures.mjs` | 10 成功：迟发 CSV 错误、空 HTML、主动 SVG、像素炸弹、权限/根文件系统/网络隔离、输出/错误超限、超时、取消、真实 OOM 与停止清理；失败没有部分输出 |
| 旧 PPT 隐藏形状和隐藏分组 | 旧实现实测暴露隐藏文字；修复后 default/include_hidden 两种策略均成功 |
| SQL Server / MySQL OpenAPI 快照 | 两库均成功；包括全部迁移、34 种精确 MIME、解析策略参数和统一来源 DTO |
| 迁移 277 中断恢复 | SQL Server / MySQL 共 2 项成功，验证旧载荷保留、未记账重放、禁用守卫恢复、9 MiB 完整清单及向量格式边界 |
| 双库 AI 业务回归（含 50 个真实格式/策略样本的 Host/Tenant 上传、来源、持久化、激活及恢复） | 106 项全部成功，零跳过；同轮连同迁移 246 的 6 项共 112 项，用时 50m56s，其中两项旧迁移测试 SQL 适配失败，已按下行修复复测，不将该轮整体记为通过 |
| 迁移 246 修复复测：`--filter FullyQualifiedName~Migration246AiKnowledgeIndexTaskRecoveryTests --minimum-expected-tests 6` | 修复测试中策略谓词移除与投影替换的顺序，6 项全部成功、零跳过，47.711 秒；重新构建零警告/错误。该修复未改变生产代码，106 项 AI 业务通过结果继续有效 |
| `argumentsFor('smoke')` 的迁移幂等、登录、当前租户及租户原子开通影响集 | 8 项全部成功，零失败/跳过，5m31s |
| 标准 OpenAPI 与 HEAD 的兼容比较 | 96 份基线/当前契约通过；扩展门禁仅接纳知识库固定上传端点的已批准媒体，保留原媒体、原二进制绑定、认证、必填性和响应，含 Stream 引用被改成对象时必须拒绝的回归 |
| `node --test --test-concurrency=2`：governance、naming、UUID、openapi、localization、testing、ai-parsing | 517 项全部成功，零失败/跳过，含新增上传媒体扩容正反回归 |
| `verify-integration-shards.mjs` | 1348 项发现形成互斥完整分区：205 SQL Server API、205 MySQL API、564 迁移、294 基础设施、80 消息重型；迁移拆分 250/314，无遗漏或重复。这里只验证发现与划分，不声称执行了全部 1348 项 |
| 架构聚焦 `AiDependencyBoundaryTests|NativeAot|MemoryPackControlledProtocol` | 82 项全部成功、零跳过；Release 构建零警告/错误，测试 7.431 秒 |
| 共享客户端契约 | 68 个文件、299 测试成功，TypeScript 构建成功 |
| Vue 知识库/聊天组件 | 13 个文件、166 测试成功，含图片 canvas、坐标及来源授权交互 |
| Playwright：`ai-knowledge-pdf`、`ai-knowledge-index-changes`、`ai-knowledge-index-reconciliation` | Edge 真实浏览器 15 项全部成功，35.8 秒，2 个并发 Worker，输出位于系统临时目录 |
| 弹窗修复后的 Playwright 复测 | 同一相关组 15 项全部成功，36.9 秒，零失败/跳过 |
| Vue 类型检查与生产构建 | 均成功；生产产物写入系统临时目录 |
| 弹窗内来源选择器修复后的 Vue 验证 | 类型检查、15 文件 204 项知识库/聊天/API 组件测试、生产构建均成功；修复原生 dialog 内下拉弹层挂到 body 导致无法交互的问题，弹窗外默认挂载方式不变 |
| 固定 Python/Maven 资产 OSV 查询 | 72 个 Python 分发包、18 个 Maven 包，共 90 项查询未返回漏洞记录；不覆盖系统包，也不证明没有漏洞 |
| API/Worker AOT 分析 | 两个 Host 均以 `FullNetAotAnalysis=true`、`UseArtifactsOutput=true`、独立系统临时 `ArtifactsPath` 构建，均零警告/错误；没有覆盖工作区 JIT 还原图或输出 |
| 完整 API/Worker linux-x64 Native AOT 发布 | 两个 Host 的 `dotnet publish -c Release -r linux-x64 --self-contained true -p:FullNetPublishMode=NativeAot -p:RuntimeFrameworkVersion=10.0.13` 均成功；API 9 条、Worker 7 条第三方警告全部通过仓库 `validatePublishWarnings`，未登记警告为零 |
| 实际 Linux Native API/Worker 端到端影响集 | `--filter FullyQualifiedName~NativeApiAiE2ETests\|FullyQualifiedName~NativeWorkerPdfE2ETests --minimum-expected-tests 6`，发现与 TRX UID 完整核对，6 项成功、零失败/跳过，20m33.388s；覆盖双库原生 API 与双库 Worker PDF/统一文档已结算代次恢复；前置 Release 构建零警告/错误，3m48.11s |

完整真实样本涵盖 CSV/GB18030、静态 HTML、DOC/DOCX、XLS/XLSX、PPT/PPTX、扫描与混合 PDF、常见和扩展位图、HEIC/HEIF、AVIF、JPEG 2000、JPEG XL、PSD、SVG/SVGZ、WMF/EMF、方向、倾斜、有线矩形表格及 GIF/TIFF/APNG 多帧。PPT 隐藏形状及隐藏分组使用真实二进制 PPT 验证。所有样本均为任务自行生成，无生产文档、真实用户数据或付费模型调用。

这 50 个小型自制样本的原始文件最大 1,320,288 字节、单份输出最多 8 块；逐次运行总耗时 250.438 秒，中位数 4.7195 秒、最近秩 P95 为 10.169 秒、最长 12.832 秒。94 个 OCR 块的最低分数为 0.965366、平均分数为 0.989842；分数不是通用准确率。测试逐项断言预期中文、英文和表格文字，不能把本批固定样本结果外推到任意扫描件、照片或生产负载。

最小生产资产采用 POI 5.5.1、Java 21、Pillow 13、pypdfium2 5.14、PaddleOCR 3.7 / PaddlePaddle 3.3.1、固定 PP-OCRv5 mobile 权重及必要的隔离图像转换器。Tika 3.3.3 只用于选型及测试夹具，不进入最终解析镜像。Python、Java、模型文件分别锁定版本和摘要；镜像保留系统包、资产摘要和许可信息。详见 `THIRD-PARTY-NOTICES` 与解析工具锁文件。

选型使用按格式关闭的 POI 类型化入口，保留单元格、公式缓存和幻灯片结构；不以通用自动检测输出替代来源坐标。PDFium 提供原生文字与扫描栅格，Pillow/固定扩展 codec 负责图像，Draw 仅处理 WMF/EMF 所需转换，Paddle 只执行镜像内离线模型。第三方解析器全部留在工具容器，API/Worker 的 .NET 闭包通过严格静态协议消费结果。

Python 结构和控制器故障证据复用镜像 `sha256:390d5ccdb5b5a8379bb86fbcb32300492f094ccda9a686d025457c0274b6bd61` 及其专用故障探针 `sha256:1136c7fed4020a2fa627273915397a217cf71c09646630ba56198f3069e8d2e3`。其后仅修正 Java 的旧 PPT 隐藏形状/分组过滤；Python、模型、控制器和资源策略没有变化。最终镜像已重跑全部 50 个真实场景及 Native 消费链路，不能把故障探针本身当作生产镜像。

复现入口为 `tests/ai-parsing/generate_fixtures.py`、`OfficeFixtures.java`、`PptVisibilityFixtures.java`、`run-native.mjs`、`run-native-failures.mjs` 及 `NativeDocumentProbe.cs.fixture`。夹具使用自制文字和图形；生成旧 DOC、WMF/EMF 的夹具环境可包含 Writer/Draw，生产镜像只含必要的 Draw。`run-native.mjs` 接收实际镜像 ID、原始样本绝对目录及输出绝对目录，输出 `manifest.json` 和完整线格式。双库真实格式测试必须显式设置 `FULLNET_DOCUMENT_ACCEPTANCE_MANIFEST` 指向该清单；缺失时是未验收，不能记为成功。托管测试须使用仓库资源入口完成新鲜构建冻结后运行，本批固定 `DOTNET_PROCESSOR_COUNT=2`、`FULLNET_TESTCONTAINERS_REUSE=0`。

## 验收结果与问题处理

本批 F01–F05 的上述影响集均已实际通过；保留各轮失败、修复及复测范围，不把发现列表或跳过项计为通过。原始记录位于 `C:\Users\Administrator\AppData\Local\Temp\fullnet-multiformat-selection-20261023`：包括 `ai-unit-v3-final.log`、`integration-final.log`、`migration-smoke-final.log`、`quality-final.log`、`vue-dialog-fixed.log`、`browser-dialog-fixed.log`、`native-parsing-50.log`、`native-managed-probe-50.log`、`aot-analysis-final.log`、`native-hosts-singleheap.log`、`native-runtime-final.log` 及 `native-output/runtime-results/native-ai.trx`。最终临时数据库、运行 SDK 和 Ryuk 已退出，资源台账确认本批容器/网络/卷均已回收；共享 SQL Server/Redis 未改动。

Native API 首轮 6 GiB 编译预算在 ILC 对象输出阶段出现 `OutOfMemoryException`，未产生可验收原生产物，不能记为发布通过。失败对象文件未生成，保留现有中间构建以复用；核实 Linux VM 可用内存约 12.2 GiB 后，保持 2 CPU、无额外 swap，把本批 SDK 编译容器上限调整为 10 GiB 重试，未改变生产编译配置或解析容器的 3 GiB 限额。

10 GiB 轮次在 `StringTableBuilder.FlushReservedStrings` 的 `MemoryStream` 扩容再次内存不足，不能把上限增加当作修复。核对 [ILC 默认 Server GC 配置](https://github.com/dotnet/runtime/blob/v10.0.13/src/coreclr/tools/aot/AotCompilerCommon.props)、[字符串表实现](https://github.com/dotnet/runtime/blob/v10.0.13/src/coreclr/tools/aot/ILCompiler.Compiler/Compiler/ObjectWriter/StringTableBuilder.cs)及 [GC 堆配置](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/garbage-collector)后，仅在编译 SDK 进程中设置 `DOTNET_GCHeapCount=1`、`DOTNET_GCHeapHardLimit=0x200000000`（8 GiB）；容器仍为 10 GiB，目标程序运行配置、分析门禁与符号选项不变。该轮两个 Host 均实际发布成功，支持编译器堆配置与本次失败相关，但不外推为所有 ILC 内存问题的通用修复。原生中间文件单独进入有界子目录，保留失败证据。

API 可执行文件 SHA-256 为 `69fc16c987c65cc0eb1527821627723d66de89f18dd12708faef5dc9f9a97bd7`，Worker 为 `6dfdd70c93aeb6bf4023a80ee77f507c33cd845738af01a9a4514ad591d2c3c6`。完整发布、警告核证与原生运行分别记录，编译器堆环境变量不传入运行验收。

真实 Edge CLI 使用生产前端产物及隔离的本地 API 夹具：CSV 选择 GB18030/分号/表头后，实际请求携带 `csv_gb18030_semicolon_header`；历史图片引用经手动选择当前授权知识库后可读取来源，EXIF 方向正确，canvas 为 1000×220，实测区域 (27,37,327×54) 与中文对齐，关闭后移除。夹具未模拟通知 Hub，产生的通知协商 502 不属于这些场景的通过证据。

临时目录删除命令被命令策略以 `blocked by policy` 拒绝，没有执行，也没有换解释器或扩大范围重试。旧产物保留，前端通过 `emptyOutDir:false` 非删除式重建；原始命令及拒绝记录保存在本批系统临时目录的 `cleanup-policy-rejection.txt`，后续清理受此限制。

验收成功后，另对已预览、确认位于任务临时根目录内的 Native/AOT 编译输出发起有界 PowerShell 清理，仍被 `blocked by policy` 拒绝，未执行。命令核对原生可执行文件摘要、6 项 TRX 及实际容器退出状态后才允许删除固定的六个生成目录；完整命令与工具截断的原始拒绝保存在 `native-cleanup-policy-rejection.txt`。未再次重试，该部分可重新生成的二进制与前端输出保留于系统临时目录；这不代表已完成产物清理。

选型阶段的 3 个停用 Docker 容器按实际完整 ID、停止状态和 `fullnet.task=ai-multiformat-20261023` 标签复验后已移除；解析运行镜像仍保留，共享 SQL Server/Redis 实例未接管或清理。

未取得独立代码审查。本次未获多代理授权，现有审查工具也不提供本地工作区独立审查；开发者自行检查不能计为独立审查。

## 限定边界

原生证据分层核对：50 个场景使用实际隔离解析器和 Native AOT C# 消费端；完整 Host 影响集验证原生 API 协议与来源链路、Worker 已结算 PDF/统一文档代次恢复。真实格式的双库新代次记账与持久化由托管业务影响集验证，不能据此宣称已完成“Native Worker 从真实解析到受控模型首次派发、记账与激活”的整段新生成联动；原计划 AI-A05 的这一剩余条件继续保留，不随 F01–F05 格式批次关闭。

原生 Worker 的过程日志另外出现 `DataApproval request recovery worker iteration failed` / `InvalidOperationException`。本批没有修改 DataApproval、Workflow 或 Worker Host 源码，脱敏日志不足以确定该异常根因；本次 AI 聚焦结果不能用来认证所有后台处理器健康。最小原始日志保留在 `native-output/runtime-results/process-logs`，该现象需要另行专项定位，不把它记为已修复。

上传白名单不保证格式的任意变体均可解析。宏、外链、主动内容、裁剪图片及不支持的复杂结构明确拒绝；OCR 任一文字低于 0.8 拒绝整份。图片表格只承诺本批实际验证的完整矩形有线表格，不声明无边框、任意合并、图表或照片语义能力。部署默认关闭新解析档，需先执行配套迁移，再设置可信绝对路径和实际验收镜像摘要。配置、恢复及安全附件展示见[运维说明](../operations/ai-knowledge-bases.md#多格式解析与离线-ocr2026-10-23本地限定范围验收通过)。
