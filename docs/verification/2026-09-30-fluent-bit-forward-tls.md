# Fluent Bit Forward TLS 局部验证（2026-09-30）

- 范围：`deploy/observability/fluent-bit-values.yaml` 的两路 Forward 输出；基线提交 `c609759576c4bf54ff9e743161ecb829ca8d4d4a`，叠加本工作区候选改动。
- 环境：Windows Docker Desktop；固定摘要的 Fluent Bit 4.1.1 与 Alpine 3.20。测试临时生成 CA、接收端证书和带 `DNS:receiver` 的 SAN，结束后删除临时目录与容器。
- 命令：`pnpm test:observability-forward-tls:live`，退出码 0，输出 `TLS Forward ACK, untrusted CA rejection, and hostname rejection passed.`；静态契约 `pnpm test:observability-deploy` 为 8/8。
- 结果：从候选提取的两路输出保留 `Require_ack_response On`、`tls On`、`tls.verify On`、`tls.verify_hostname On`、`tls.ca_file /fluent-bit/tls/ca.crt` 及原重试预算。测试只替换 Docker 内的接收端地址，通过与候选一致的只读挂载路径切换临时 CA。可信 CA 与匹配 SAN 时两路 ID 各接收一次，发送端各计一次成功；错误 CA 和证书主机名不匹配时，两路分别写入独立 ID，两个输出均报连接不可用，接收端未出现四个负例 ID。
- 边界：这是本机有限样本和测试 CA，不证明平台 Secret 挂载、真实服务证书、轮换、双向认证、目标接收端的持久 ACK 边界、节点/卷故障、满盘或下游可查询。专项 GitHub Actions 还未取得精确提交终态。生产 `Collector` 门禁维持关闭；容量仍为 `Capacity-not-verified`。
