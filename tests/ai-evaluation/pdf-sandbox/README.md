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
node tests/ai-evaluation/pdf-sandbox/run-cli-native.mjs
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

## 本地子进程协议（R05e3）

`scripts/ai/pdf-sandbox-cli.mjs` 复用同一隔离控制器，供可信进程使用参数数组和标准流管道启动。
必填参数为 `--image-id sha256:<64 位小写十六进制>`、
`--content-hash <64 位小写 SHA-256>`、`--size-bytes <原始字节数>`；
可选 `--timeout-ms <1–30000，默认 30000>` 和 `--run-id <小写 UUID>`。
关联 ID 只帮助跟踪，容器所有权仍由控制器每次生成的独立 nonce 确认。
镜像、指纹和大小必须来自服务端可信配置/权威来源，不能直接使用公开请求指定的值。
不接受文件路径、命令、凭据或资源放宽参数。

stdin 只接收原始文件字节，以 EOF 结束；最长 30s 的预算从读取开始，并覆盖输出交付。
超过 5 MiB、实际大小或摘要不符在启动 Docker 前拒绝。
容器停止、清理、策略、退出状态及输出字节数全部确认后，成功 stdout 交付原有
v1 PDF JSON，stderr 为空，退出 0；解析/控制失败 stdout 为空，stderr 为一个固定
机器码，退出 2。停止和清理可能需要额外的有界控制等待，预算到期不能跳过清理。
管道写入中断可能已经发送部分字节，消费者必须同时取得完整 EOF、退出 0、空 stderr，
再通过 `AiKnowledgePdfOutputReader` 校验版本、原始摘要、分页和输出预算后消费。
CLI 只校验传输与原始字节指纹，不代替业务授权或输出结构验收。

输出使用固定 `pdf-sandbox-publish.mjs` 辅助进程，空环境、无参数，只搬运有界字节。
控制器按剩余预算/取消终止自己创建的辅助进程并等待退出，再报告交付结果。
[Node 24 标准 I/O 说明](https://nodejs.org/docs/latest-v24.x/api/process.html#process-io)
指出 Windows 管道写入为同步行为；本机实验还确认挂起的异步文件写入会阻塞退出。
直接给 `process.stdout` 或文件写入加计时器不能提供跨平台硬交付时限。
调用方必须独立观察控制器的进程 `exit`；遇到非零退出、突然死亡或自身超时时，停止接受字节并关闭标准流管道，
以释放辅助写入进程持有的管道；不能只等待 stdout EOF 后才观察控制器失败。

可捕获的 Unix SIGINT/SIGTERM 请求取消并等待清理。
[Node 官方信号说明](https://nodejs.org/api/process.html#signal-events)指出 Windows
`subprocess.kill()` 的 SIGINT/SIGTERM/SIGKILL 会直接终止目标；该方式不能证明清理成功。
控制器突然死亡时只有容器内 watchdog 保证解析器在剩余时限内被强杀，停止后的容器
仍需由持有原始所有权证据的协调方清理；不能将其报告为正常取消或清理完成。

`run-cli-native.mjs` 核对未变化工具源码、已发布原生二进制及不可变镜像后复用构建，
重新执行真实 CLI 的中文页、扫描件/坏文件拒绝、资源隔离、超时、输出洪泛及真实未读 stdout。
Windows 上运行中取消通过同一传输函数的 AbortSignal 观察真实容器停止和清理；
强杀 CLI 的独立实验确认 watchdog 终止解析器，并由验收协调方核对归属后清理。
新证据写入 `artifacts/ai-evaluation/r05e3/`，不覆盖旧原生构建证据。
`cli-publication-death.test.mjs` 另以真实进程确认：辅助进程开始写入后强杀控制器，
调用方先观察进程死亡并关闭继承管道，受阻辅助写入进程随之退出。

本切片未注册 Worker、配置生产 Docker socket 或启用 PDF 入队；后续还须接入
业务来源/会话复核、分页持久清单、代次一致性与实际 Worker 消费。
