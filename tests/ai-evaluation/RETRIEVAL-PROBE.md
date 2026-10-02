# R03 检索与解析选型实验

决策见 [ADR-0013](../../docs/architecture/adr/ADR-0013-ai-retrieval-provider.md)。本目录是独立实验，不加入生产解决方案。候选 NuGet 包只有 `probe/Full.NET.AiRetrieval.Probe.csproj` 引用，测试项目只链接不含第三方依赖的向量算法源文件。生产仍按自有 Dapper 边界接入；实验的管理员连接不适用于生产。

## 输入与锁定版本

使用 R02 `rag-cases.json` 的 12 问题/13 片段，CRLF 转 LF 后 SHA-256 为 `3380bf8d0b8352df48093b1c0dee7d4216b0eeae30733cc37f04cd65e778ec6b`。维度 128，`synthetic-featurehash-v1` 为固定 FNV-1a 字符二元特征，不是模型 Embedding。候选均用相同输入、来源允许集、scope/model/generation 过滤和精确余弦；不修改 R02 语料提高分数。

- SqlClient **7.0.2**、MySqlConnector **2.6.1**：复用正式驱动版本；PdfPig **0.1.16**：仅实验。
- Windows/JIT 图锁定在 `probe/packages.lock.json`；Linux/AOT 图锁定在 `probe/linux.packages.lock.json`，脚本复制后 `--locked-mode` restore，变化必须显式更新并重验。
- SQL Server 2022 CU14、MySQL 8.0、Qdrant 1.19.1 与 Linux SDK 镜像采用脚本内的固定 SHA-256，禁止自动跟随可变 tag。SDK 镜像为现有 `eng/docker/Dockerfile.api-native-aot-linux-sdk` 产物，实验 SDK 10.0.400/ILCompiler 10.0.11。
- Qdrant 采用静态 REST/JSON `/points/query`，没有引入 Qdrant SDK。PdfPig/Qdrant 许可及用途在根 `THIRD-PARTY-NOTICES` 登记。

## 执行

需要 Node 24、Docker Linux 容器与脚本列出的本地固定镜像。首次环境须准备这些镜像；自建 SDK 的新摘要需要显式修改实验记录并重新验证，不静默替换。脚本缺少固定镜像时失败，不操作已有业务库或集群。

```powershell
node tests/ai-evaluation/run-retrieval-probe.mjs
pnpm test:dotnet:unit -- --selection ai-retrieval-candidates
```

脚本只创建随机唯一命名、带本次归属标签的三个服务容器和网络；运行结束或失败清理自己的容器、匿名数据卷及网络，并探测本次匿名卷已消失。随机密码通过环境传递，不写参数/报告，错误输出仅类型、错误码和堆栈。连接使用实验网络别名；Qdrant readiness 端口只绑定本机临时端口。SQL readiness 执行真实 `SELECT 1`，不把容器启动当可用。

发布和运行分离：源码只读挂载，Linux 构建输出 `/tmp`，原生程序和证据写到忽略的 `artifacts/ai-evaluation/r03/`，不污染 Windows `bin/obj`。原生程序各模式限制 512 MiB、2 CPU、60 秒，服务容器分别限制 SQL Server 2 GiB、其余 1 GiB。超时会终止 Docker CLI，`finally` 再删除仍存活的本次容器；不是对用户容器的通配清理。

证据包含 `publish.log`、四份 `*-native.json`、逐模式日志及 `manifest.json`。manifest 开始为 running，失败写 failed；所有模式和资源清理都成功后才写 passed/`resourcesRemoved=true`，记录镜像摘要、基线 HEAD、开始/结束一致的源码摘要和批准告警；**只看旧报告文件不构成通过**，须检查本次命令退出 0 与 manifest passed。证据目录被 Git 忽略，长期的决策和结果范围维护于本文，不提交原生产物。

Native AOT 门禁复用 `api-native-aot-publish-warnings.mjs` 和 ADR-0008 的既有程序集级精确白名单。SqlClient 的 IL2104/IL3053、其 Internal.Logging 与 ConfigurationManager 的 IL2104 已登记；不增加 suppression/NoWarn，不接受 PdfPig、自有代码或其他未登记告警。`IlcTreatWarningsAsErrors=false` 只让现有登记告警进入脚本门禁，退出码和其余告警仍须通过。

## 2026-10-02 实际结果

本地基线 `4a748409af6fcddd23b441a182ee3e7e8fe2b47f`，任务快照 `ai-retrieval-provider-r03`。Windows 主机 i7-12700H（14 核/20 逻辑处理器）、约 64 GiB 内存；Docker 29.6.2 Linux VM 20 CPU、约 31.2 GiB 内存。此资源记录描述实验环境，不是目标部署规格。

| 原生模式 | 观察结果 | 范围 |
| --- | --- | --- |
| SQL Server | 22/22；合成 Recall@5=1 | UUID v7 写入、12 个允许集过滤、编号及宽范围排序探针、删除/重复删除、新代次重建、命令超时、在途取消、连接恢复；另记录全文组件安装状态为未安装 |
| MySQL | 21/21；合成 Recall@5=1 | 与 SQL Server 相同的检索/删除/代次及超时取消恢复语义 |
| Qdrant | 23/23；合成 Recall@5=1 | 真实 payload 索引/写查删与集合重建；同一 HTTP 适配器慢响应头/正文的超时和取消（正文场景等待客户端已进入读取）；真实服务器连接恢复 |
| PdfPig/文本 | 11/11 | 中文 ToUnicode、Flate 压缩文本、页码、MD 表格/TXT；拒绝 DOCX、无文本页、非法 UTF-8、超文件/页数/输出量 |

所有模式报告 `runtime=NativeAOT`、`embeddingStatus=not_measured`。解析模式不做检索，`recallAt5=0` 只是该模式占位字段，不应作为检索指标。两个数据库与 Qdrant 的 Recall 只表示小型合成特征相似度一致，未生成答案、引用或执行工具，也未提交 R02 端到端评估结果。

冻结样例过滤后仅有 1–2 个候选，Recall@5=1 主要证明允许集保留正确片段，不能验证排名。另加独立编号排序探针：在同租户的全部合成片段范围（多于 K）读取 Top-5，三个实现 Top-1 均与受控代码的精确余弦及 `ticket-1` 一致。该探针临时扩大合成允许集，不修改 R02 样例，也不代表正式授权允许访问失效文档；不能据此证明真实模型的中文/编号判别力。

取消实验实际揭示两处差异：SqlClient 在受控 WAITFOR 取消时返回 `SqlException(Number=0, State=0, Class=11)`，探针只在令牌已取消且 3 秒内返回时接纳，不泛化为生产写入未完成；MySQL 单独 SLEEP/常量投影中断可能正常返回，改为真实行谓词才验证超时/取消的驱动异常。参见 [SqlClient 问题记录](https://github.com/dotnet/SqlClient/issues/2424)和 [MySQL SLEEP 官方行为](https://dev.mysql.com/doc/refman/8.0/en/miscellaneous-functions.html#function_sleep)。

`dotnet list tests/ai-evaluation/probe/Full.NET.AiRetrieval.Probe.csproj package --vulnerable --include-transitive --format json` 已执行，当前图未报告已知漏洞；这不是未来漏洞或生产接入审计豁免。最终回归命令与结果同时记录在 AI 活动计划 R03。

## 不能据此推导的结论

真实模型、生产语料、4096 候选的容量、真实模型维度、ANN 收益、并发延迟、生产 TLS/ACL、文件 Claim 竞态与用户权限 API、Qdrant 持久卷/集群灾难恢复、正式 Host.Api/Worker AOT 均未由本实验验收。Qdrant 的取消/超时由受控慢 TCP peer 验证客户端传输，不证明服务器计算立即停止；集合重建仅证明从原始合成片段恢复。

PDF 文件/页数/输出检查不能限制单页解压的瞬时分配。实验进程整体的外部预算不替代 R05 的无凭据、无网络解析进程和恶意文件/超时/取消预算测试；空 PDF 夹具也不代表 OCR 已实现。R05/R06 需在真实调用和受控边界上重新关闭各自门禁。正式知识库保持 Planned，全链容量保持 `Capacity-not-verified`。
