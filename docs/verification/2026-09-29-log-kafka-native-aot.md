# 日志 Kafka：Host.Api Native AOT 本地验证

- 日期：2026-09-29。
- 代码基线：`c609759576c4bf54ff9e743161ecb829ca8d4d4a` 加本任务未提交工作区变更。
- 环境：Windows Docker Desktop 29.6.2；Linux SDK 10.0.400 容器；Kafka 4.1.2 Testcontainers、MySQL 8.0 Testcontainers。
- 发布：`pnpm test:aot:publish:linux` 退出码 0，告警门禁只含 17 条已准入第三方告警，产出 `linux-x64` 原生可执行文件 133,356,432 字节及发布清单。单次耗时 919,172 毫秒仅记录构建事实，不作为容量数据。
- 原生测试：在 Linux SDK 容器内挂载仓库与 Docker socket，使用宿主网络和 `TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal` 运行 `NativeApiKafkaLogMySqlE2ETests`，最终双通道用例 1/1 通过、0 跳过；强化断言后再次通过 1/1、0 跳过（3 分 25 秒）。测试启动真实 Native Host.Api 与隔离 MySQL，配置非生产 `ApplicationKafka`、临时 CA、SASL_SSL/PLAIN 受限 Producer 和两个预置 Topic；`/health/live` 返回 200，Broker 普通 Topic 可读回启动资源日志。测试将 HTTP 慢请求阈值设为零，再请求不存在的 `/api` 路由并收到 404；优先 Topic 可读回 `GET`、`<unmatched>` 路由、状态码 404 的 HTTP 操作日志。两条消息 JSON 的 `LogEventId` 均等于各自 Kafka key；宿主收到终止信号后在 30 秒内以退出码 0 停止。首次仅普通通道的用例也曾通过 1/1；最终结果以强化断言后的双通道复测为准。
- 网络诊断：首次嵌套 Docker 运行在建库前失败；容器内 `127.0.0.1:<映射端口>` 拒绝连接，`host.docker.internal:<映射端口>` 可连通。测试 TLS 夹具仅在显式提供 `TESTCONTAINERS_HOST_OVERRIDE` 时使用该主机作为 Broker 公布地址，并将其纳入临时证书 SAN；未修改生产连接配置。本地通过只证明所述 Docker Desktop 网络拓扑，不能代替正式 Linux CI 与目标集群网络验收。
- 复测命令（Windows PowerShell，仓库位于下列 `G:` 路径）：

  ```powershell
  docker run --rm --network host -v /var/run/docker.sock:/var/run/docker.sock -v /g/wwwroot/github_fork/Full.NET:/src -w /src -e DOCKER_HOST=unix:///var/run/docker.sock -e TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal -e FULLNET_TESTCONTAINERS_REUSE=0 fullnet-native-aot-publish-sdk:10.0 dotnet tests/Full.NET.IntegrationTests/bin/Release/net10.0/Full.NET.IntegrationTests.dll --no-ansi --progress off --filter FullyQualifiedName~NativeApiKafkaLogMySqlE2ETests --minimum-expected-tests 1 --timeout 15m
  ```

- 回归：`node scripts/testing/run-integration-shard.mjs logging-kafka` 6/6；`pnpm test:aot:analyzers` 0 警告/0 错误；`pnpm test:dotnet:architecture --selection api-native-aot` 73/73。架构命令首次与分析器并发还原时发生 NuGet 临时文件冲突，串行重跑通过；首次失败不计为代码缺陷或通过证据。
- CI 登记：核心 Native AOT 门禁在发现阶段明确要求 `NativeApiKafkaLogMySqlE2ETests`，防止仅凭聚合最小用例数遗漏这条日志链路；正式 Linux CI 执行终态仍待记录。
- 后续门禁：正式 Linux CI 终态、目标平台 Secret/证书轮换、Native Worker、跨进程崩溃对账、SDK/native 峰值与总停机耗时、最终 ES/归档可查询和容量对比尚未验证。生产 `ApplicationKafka` 门禁继续关闭，状态仍为 `Capacity-not-verified`。
