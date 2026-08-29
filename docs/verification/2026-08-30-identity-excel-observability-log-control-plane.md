# Identity Excel 与 Observability 日志控制面验证记录

日期：2026-08-30
状态：Build-verified

## 交付边界

- Identity 保留既有 JSON 兼容接口，新增固定结构 `.xlsx` 模板、文件导出和 multipart 文件导入。
- 工作簿上传限制为 1 MiB、1,000 数据行；拒绝公式、外部关系、未知表头和超限 ZIP/XML，导出文本防公式注入。
- 文件导入只负责解析，写入仍委托既有 `HostUserManagementService.ImportAsync`，保留逐行成功/失败结果和超级管理员保护。
- 新增官方 `ObservabilityAdmin` 模块，只枚举固定根目录顶层 `.log` 普通文件；客户端仅使用服务端生成的 SHA-256 文件 ID。
- 日志文件通过操作系统原子 no-follow 语义打开，并用已打开句柄的最终路径重新校验固定根目录；文件或根目录在枚举后被符号链接替换时失败关闭。
- 日志尾读默认 200 行/256 KiB，配置上限 5,000 行/1 MiB；支持取消以及 `FileShare.ReadWrite | FileShare.Delete` 活动文件共享读取。
- 日志列表/尾读与下载分别使用 `observability.log_files.read`、`observability.log_files.download`；Vue 页面不创建未授权下载入口。

## 新鲜验证证据

| 检查 | 结果 |
| --- | --- |
| `HostUserWorkbookCodecTests` | 7/7 通过；覆盖外部关系、恶意引用、大小与取消边界 |
| `LogFileControlPlaneTests` | Windows 10/10 通过并按平台跳过 Linux 专项；Linux SDK 容器内 FIFO 竞态专项 1/1 通过；覆盖轮转、静态/竞态符号链接、非普通文件、缺失根目录与配置硬上限 |
| Identity MySQL 用户合同（含 Excel 三端点） | 1/1 通过 |
| Observability Admin SQL Server/MySQL API 合同 | 双 Provider 各 1/1；覆盖匿名 401、读 200、越权下载 403、下载 200 |
| Vue 定向测试 | 34/34 通过 |
| Vue typecheck + production build | 通过；新增日志页独立异步 chunk |
| OpenAPI 生成器 `--check` | 零漂移 |
| 精确 Architecture 检查 | 2/2 通过 |
| `pnpm test:aot:analyzers` | 0 警告、0 错误 |
| `pnpm test:inner -- --snapshot identity-excel-observability-logs-20260830` | 18/18 通过；MySQL inner |
| `pnpm test:slice -- --snapshot identity-excel-observability-logs-20260830` | Smoke 8/8、Identity/ObservabilityAdmin 双 Provider 聚焦 32/32 通过 |
| `pnpm test:governance` | 52/52 通过 |
| `pnpm test:integration:partitions` | 651 项无遗漏/重复；api-sqlserver=63、api-mysql=63 |
| `pnpm test:aot:publish:linux` | 通过；72,469,120 字节；9 条均为 ADR 已登记第三方告警 |

## 未验证边界

- 当前 Windows 主机运行 `pnpm test:aot:native:e2e` 时发现 19 项，但全部按设计标记 Inconclusive；Linux 原生进程启动与 HTTP E2E 仍由 Linux CI 关闭。
- 本波没有执行 Vue 生产真实栈浏览器 E2E，也没有进行生产日志轮换器、超大活动日志或多副本共享卷容量认证。
- Observability Admin 的实例/运行时硬件信息未包含在本切片；通用 ImportExport/Reporting 也未因 Identity 小切片自动完成。
