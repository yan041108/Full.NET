# 临时输出清理被命令策略拒绝

请求原文见 [完整命令](cleanup-rejected-command.txt)。这是执行记录，不是新的执行入口。

工具返回：`exec_command failed: CreateProcess ... rejected: blocked by policy`。拒绝发生在创建 PowerShell 进程之前；命令中的校验和删除均未执行，没有 shell 退出码。此拒绝不是 Auto-review。

本轮测试、独立复算、逐项归档校验均已通过；容器、构建卷和验收资源已实际归还。2026-10-18 21:59:05 UTC 的独立只读检查确认以下两个存储目录仍存在：

- `C:\Users\ADMINI~1\AppData\Local\Temp\fullnet-cache-capacity-18127590-7c2b-4d97-b729-c525453fe7f3`：4,600,121,074 字节。
- `C:\Users\Administrator\AppData\Local\Temp\fullnet-cache-integration-402-f4b1ecf3e28f\capacity-k6-resume\raw-evidence-stage`：16,214,270 字节。

未改用其他解释器、等价删除命令或扩大路径重试。当前会话为 Never 审批模式，没有可用的批准升级入口。旧失败批次、旧拒删归档及共享 Native 生产者也保持原状。验证完成与存储清理未完成分别记录。
