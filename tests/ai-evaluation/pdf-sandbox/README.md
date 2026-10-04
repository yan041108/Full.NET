# 独立 PDF 解析器本地验收

本目录执行 ADR-0013 的 R05e2 硬隔离验收。工具只解析文本 PDF，真实输出由
`AiKnowledgePdfOutputReader` 与获批模型的分页分块器消费。业务 PDF 导入、Worker、
文档/模型派发前权威复核及索引状态推进仍待后续切片接入。

## 固定边界

- stdin 原始 PDF，stdout 完整版本 1 JSON；错误只有固定机器码，失败不提供部分页。
- 输入 5 MiB、128 页、2 Mi UTF-16 字符、PdfPig 嵌套 64、累计解码 32 MiB。
- 仅支持 FlateDecode/Fl、ASCIIHexDecode/AHx；预测器只能缺省或为 1。
  不可解析的间接 DecodeParms、其他编码和预测器明确拒绝。
- 原创 RFC 1951 帧校验确认最终块、回引用与展开长度，再由 BCL 解压并核对长度和 Adler32。
- 驱动只接受可信调用者提供的完整镜像 ID；关联任务 ID 不代表容器所有权。
- 每次调用独立容器：禁网、只读、非 root、禁提权、去全部能力、无挂载、无凭据、禁用容器日志。
- 512 MiB 内存、零 swap、2 CPU、32 PID、最多 30s；容器内 watchdog 独立于协调进程。
  超时、取消、洪泛或错误时强杀本次已核对所有权的容器，确认停止并清理后才返回。
  Docker 控制/停止/清理失败保持失败，不返回成功输出。

## 执行

在仓库根目录使用现有 Docker Desktop Linux 引擎，不停止既有服务：

```powershell
node --test tests/ai-evaluation/pdf-sandbox/*.test.mjs
node tests/ai-evaluation/pdf-sandbox/build-native.mjs
node tests/ai-evaluation/pdf-sandbox/run-native.mjs
```

构建脚本使用固定 SDK/运行时 digest、2 CPU/2 GiB 编译上限、独立输出与锁定还原。
仅编译容器挂载只读源码、包缓存与证据目录；运行容器没有这些挂载。
固定版本 PdfPig 的完整许可和外部组件通知随原生工具镜像发布。
无需构建完整 Host.Api 原生闭包。

原始 publish 日志、锁文件、工具摘要、镜像 ID、源码摘要及每项实际退出/隔离观测写入
忽略目录 `artifacts/ai-evaluation/r05e2/`。验收驱动包含真实压缩炸弹、间接预测器、
缺失最终块但伪造正确校验和、扫描件、重复关联 ID、OOM、取消、超时和输出洪泛，
并杀掉协调进程验证 watchdog 仍终止解析器。

本地通过只证明此编码子集与已测预算的独立工具；不证明 Kubernetes 调度部署、
完整 RAG、真实 Embedding 或 10K 容量已接入。容量保持 `Capacity-not-verified`。
`lifecycle.test.mjs` 使用独立测试进程中的模拟 Docker 控制故障验证启动中取消、
强杀拒绝、清理失败与 create 响应丢失；这些模拟不代替真实原生隔离观测。
