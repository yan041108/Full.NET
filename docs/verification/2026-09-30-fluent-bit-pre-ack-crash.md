# Collector Forward ACK 前采集器崩溃本地验证

- 日期：2026-09-30；代码基线：`c609759576c4bf54ff9e743161ecb829ca8d4d4a` 加当前未提交日志切片。
- 环境：Windows Docker Desktop 29.6.2，固定 Fluent Bit 4.1.1 镜像及固定 Node 24.21.0 Alpine 无 ACK 接收端镜像；候选输出取自 `deploy/observability/fluent-bit-values.yaml`，本地 Docker 网络测试时仅替换目的地地址和 TLS 传输。发送端使用文件系统缓冲与候选 `storage.sync normal`，接收端持久缓冲阶段使用 `storage.sync full`。
- 命令：`node eng/testing/fluent-bit-forward-ack-smoke.mjs --docker-desktop`，退出码 0，输出 `Forward ACK, pre-ACK sender SIGKILL recovery, and ACKed receiver SIGKILL recovery passed.`。专项 GitHub Actions 工作流运行同一脚本，但此工作区变更尚无精确提交的 CI 终态。
- 故障顺序：先确认普通/优先输出均收到 Forward ACK 且各计一次成功；改用只读取 TCP 数据但不回复 ACK 的接收端，注入一个优先级 ID，确认它到达接收端、发送端出现重试而成功计数未增加。此时 SIGKILL 发送端并删除两份源日志，保留 Tail DB 与发送缓冲；替换为带文件系统缓冲的 Fluent Bit 接收端并重启发送端。新发送端对该 ID 计一次成功、无丢弃，普通通道没有额外重放。再在接收端已 ACK 且下游不可达时 SIGKILL 接收端，保留接收缓冲并改接文件输出，原 ID 恢复一次。
- 结论范围：这个单 ID 样本证实候选配置在一次采集器进程崩溃前未收到 ACK 时可从本地缓冲恢复；删除源日志排除了 Tail 重读形成的假阳性。测试没有证明每个 fsync 竞争窗口、满缓冲/满盘、Pod 或节点与卷丢失、TLS、生产接收端的持久 ACK、Kafka/ES 最终确认或负载容量。生产 `Collector` 门禁继续关闭，`Capacity-not-verified`。
