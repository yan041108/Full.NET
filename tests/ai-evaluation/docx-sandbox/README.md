# DOCX 隔离执行验收

本目录验证 R08g 容器、进程与传输边界。正文解析器为独立 Native AOT 工具；原会话规划和付费消费仍是私有组件，未注册至 Worker 或公开上传。

快速测试在仓库根目录运行：

```powershell
node --test --test-timeout=30000 tests/ai-evaluation/docx-sandbox/*.test.mjs
```

真实 Linux 容器验收显式传入已验收的生产 DOCX 工具镜像 ID、独立 Native 对抗探针镜像 ID 和证据绝对目录：

```powershell
node tests/ai-evaluation/docx-sandbox/run-native.mjs <sha256:parser-id> <sha256:probe-id> <absolute-results-directory>
```

工具镜像构建沿用 `src/Tools/Full.NET.Ai.DocxParser/Dockerfile`，仅打包已验证的 linux-x64 原生二进制和许可文件。探针复用 `tests/ai-evaluation/pdf-sandbox/probe` 的独立原生二进制；它只测试隔离、资源预算及故障，不计为正文解析证据。两者都须使用完整不可变镜像 ID，不能使用标签。

测试在本机合成 OOXML 包，确认原始摘要、完整正文、空段落、表行列、表头和 Unicode。实际 probe 验证禁网、只读、非 root、禁提权、无凭据、内存/swap/CPU/PID 上限、OOM、总体时限、输出洪泛、取消以及相同关联 ID 下的独立资源归属。CLI 另验证 EOF 和权威长度帧模式；每次返回前确认所属容器已停止并删除，不停止其他开发服务。

`source.docx`、`output.json` 和 `native.json` 写入指定忽略目录。该范围未单独验证控制器异常失联时的 watchdog，也不能证明真实 HTTP 登录、双库费用结算、Native Worker 全代次 DOCX 索引、模型质量或容量；这些证据分别由活动开发计划记录。
