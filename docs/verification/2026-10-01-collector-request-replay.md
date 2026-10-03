# Collector 真实 HTTP 请求快照回放

任务基线 `c609759576c4bf54ff9e743161ecb829ca8d4d4a`，快照 `collector-request-replay-20261001`。2026-10-01 Windows/Docker Desktop 本地运行；没有修改生产采集配置或切流。

显式命令 `pnpm test:observability:request-replay:live` 导入此前持续请求实测的 `artifacts/logging-request-sustained/Projected-1.jsonl`。最多 8MiB，要求 5200 条 HTTP Operation、520 条 Priority 和 5200 组请求/返回投影。同一份已读取字节用于长度复验、解析与 SHA-256，避免回放期间源文件被覆盖造成证据错配。

复用固定 Fluent Bit 4.1.1 镜像及正式 CRI Tail、解析器、可信 Kubernetes 元数据与路由过滤。随机临时夹具模拟两个 Pod：Collector Pod 写入真实快照，ApplicationKafka Pod 写入 5200 个不同的合法 UUID v7 镜像，并在正文伪造 Collector 标签。只替换正式 Forward 出口为分路文件出口；没有接通本实验的 Kafka、ES 或 Forward ACK。

最终实际回放退出码 0：

- 520 条在 Priority、4680 条在 B2，总计 5200 个唯一 LogEventId，与源 RequestId、状态、字段和请求/返回投影逐条一致。
- 5200 个 ApplicationKafka 镜像全部被排除，没有重复或额外事件。
- `DiagnosticGroup` 等正式剔除字段未泄露；来源字段须逐项相等，额外字段只接受受控传输字段。CRI `_p` 只允许完整行 `F`，拒绝部分行 `P` 与未知字段。
- Docker 精确容器名查询成功且结果为空，临时目录移除成功之后才写 `passed` / `cleanupVerified` 报告。

首次源预检发现真实 `DiagnosticGroup` 在正式过滤中应移除，修正预检预期；首次 Docker 输出对账发现 CRI `_p=F`，按实际输出收窄允许名单并补负例。独立审查发现摘要二次读取和容器清理未确认问题，修复后重新运行完整回放通过。

原始本地证据在忽略目录 `artifacts/collector-request-replay`：`result.json` 保存源路径/摘要、固定镜像、字节数和范围，`priority.jsonl` 与 `b2.jsonl` 保存实际输出。采集固定运行 15 秒，报告时间还包括容器启动/关闭、读文件和对账；不是精确排空时间，不能用事件数除报告时间推断吞吐。

验证：

- 新验收判定占位实现 RED：3/3 失败；实现及负例补齐后 GREEN：3/3 通过。
- `node --test tests/deployment/collector-request-replay-proof.test.mjs tests/deployment/observability-contract.test.mjs`：12/12 通过。
- `pnpm test:observability:request-replay:live`：最终修正版实际 Docker 回放通过，退出码 0。
- `node --check eng/testing/fluent-bit-collector-route-smoke.mjs` 与原 `--prepare-only` 分支：通过；未重跑原完整多故障 smoke。
- `pnpm test:integration:tooling`：53/53 通过；`pnpm test:governance`：57/57 通过。
- 任务影响集为 `integration-tooling`；没有 .NET、SQL、Native AOT 可达路径改动，无需重复这些构建。

本次只验收真实请求字段经过 Collector 的有限预装回放与混合 Pod 防重复。它不验证持续请求期间采集延迟、Kafka Broker ACK、消费 ES 确认、长期容量或两路线同负载优劣；对应任务保持待验证。
