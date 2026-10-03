# 独立日志消费者运维与部署本地验证

- 日期：2026-09-30；授权：用户继续推进日志开发，按全项目本地验收规则执行。
- 基线：`c609759576c4bf54ff9e743161ecb829ca8d4d4a`；任务快照：`logging-consumer-operational-delivery`。工作区已有其他开发改动，本记录仅覆盖本切片。
- 环境：Windows PowerShell、.NET 10、Docker Desktop 29.6.2；Linux SDK 容器、真实 TLS Kafka/Elasticsearch 测试容器；本地 kind 三节点共享一台主机。
- 结论：独立消费者健康检查、固定指标、SIGTERM 生命周期、镜像及默认关闭的 Helm 交付载体通过本地验证。消费者仍为 experimental，未执行生产部署或切流。

## 实现与确认边界

健康端口默认 0（无 HTTP 监听），配置为 1024..65535 时开放内部运维端点。`/health/live` 表示进程未停止，`/health/ready` 仅在 Kafka 实际分配分区后返回 200；不表示 ES 可写或无积压。四个指标只记录分区数、就绪、提交和延后次数，不携带 Topic、ID、载荷或秘密标签。

Generic Host 接收 SIGTERM，消费循环最多等待 250ms 后观察退出令牌。ES/DLQ 确认后再提交 Offset 的串行规则不变；未确认事件保持可重放。镜像使用 JIT 独立宿主，不接入业务 Worker。Helm 必须显式启用实验选项、固定镜像标签及配置 Secret；非 root、只读根文件系统、固定 CA 挂载、资源限制、内部 Service 和探针均受合约测试约束。

Deployment 使用 Recreate，避免单分区场景下新 Pod 因旧 Pod 持有分区而无法 ready 的滚动更新等待。更新期间允许消费间隙，由 Broker 保留事件；增加副本数量应结合分区数量。NetworkPolicy 渲染通过不代表本地 CNI 已验证实际拦截。

## 实际验证

| 命令或实验 | 结果 |
| --- | --- |
| `dotnet build tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --nologo -clp:ErrorsOnly` | 退出 0，0 警告/错误 |
| 独立进程 `IndependentConsumerReadinessRejectsUnassignedBroker`（Windows） | 1/1，0 跳过 |
| Linux SDK 容器运行四组 Kafka 日志测试，`--minimum-expected-tests 14 --timeout 15m --report-trx` | 14/14，0 失败/跳过，退出 0，6m16s；包含 SIGTERM 退出 0、DLQ 失败保留 Offset、重启重放及既有 ES/TLS/崩溃/重平衡回归 |
| `pnpm test:helm` | 25/25，0 失败/跳过 |
| `pnpm test:integration:tooling` | 53/53，0 失败/跳过 |
| `pnpm test:governance` | 57/57，0 失败/跳过 |
| `pnpm test:integration:partitions` | 1097 项，无遗漏/重复 |
| `docker build -f deploy/containers/log-consumer.Dockerfile -t fullnet-log-consumer:local-20260930 .` | 退出 0；运行用户 1654，镜像 140242855 字节 |
| kind 加载镜像并安装独立 Helm release | Pod Running，重启 0；UID/GID 1654、只读根文件系统、100m/128Mi 请求与 500m/256Mi 限额 |
| 实际 Pod 端口转发 HTTP 检查（故意配置不可达 Broker） | live=200，ready=503；分配、提交、延后计数均为 0 |

Linux 完整命令：

```powershell
docker run --rm --network host -v /var/run/docker.sock:/var/run/docker.sock -v /g/wwwroot/github_fork/Full.NET:/src -w /src -e DOCKER_HOST=unix:///var/run/docker.sock -e TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal -e FULLNET_TESTCONTAINERS_REUSE=0 fullnet-native-aot-publish-sdk:10.0 dotnet tests/Full.NET.IntegrationTests/bin/Release/net10.0/Full.NET.IntegrationTests.dll --no-ansi --progress off --filter 'FullyQualifiedName~KafkaLogDeliveryTests|FullyQualifiedName~KafkaLogConsumerDeliveryTests|FullyQualifiedName~KafkaLogConsumerElasticsearchReplayTests|FullyQualifiedName~KafkaLogConsumerElasticsearchTlsTests' --minimum-expected-tests 14 --timeout 15m --results-directory artifacts/log-consumer-operational/test-results --report-trx --report-trx-filename linux-logging-kafka.trx
```

最终 TRX：`artifacts/log-consumer-operational/test-results/linux-logging-kafka.trx`。镜像 ID：`sha256:a4d14a108cf92d1ee2a46325be9e9cc492e28ca94df3206b007c76d4363f6c8a`。kind 测试只验证部署启动及未分配分区的负向探针，未在 Kubernetes 中连接真实 Kafka/ES；测试专用 release、Secret 及命名空间已清理。

## 失败证据与修正

健康用例先在旧宿主运行失败（无 HTTP 响应），实现后通过。初次构建缺少 `Microsoft.AspNetCore.Hosting` 导入，修正后构建通过。新增 Recreate 合约先失败，增加策略后通过。

首次 Linux 回归发现 ES 测试地址硬编码 127.0.0.1，跨 Docker Desktop 容器无法连接；该次 Docker 后续停止且没有完整 TRX，不计通过。修正测试夹具为容器 Hostname，并为测试 TLS 证书添加 host.docker.internal SAN；同进程代理仍使用 loopback，生产 TLS 校验未放宽。重建后完整 14 项通过。

独立代码审查未发现阻塞项。本切片未覆盖生产 CA/CRL/OCSP 实际可达性、完整 Kubernetes Kafka→ES/DLQ 部署链路、消费 lag/故障分类告警闭环或容量与请求 P99；这些任务保持未完成，不通过改变验收环境要求自动勾选。
