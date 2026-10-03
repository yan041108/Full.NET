# Worker 日志 Kafka 原生进程本地验证

- 日期：2026-09-30；基线：`c609759576c4bf54ff9e743161ecb829ca8d4d4a` 加当前未提交日志模块变更。
- 环境：Windows Docker Desktop 29.6.2，Linux SDK 10.0.400 容器，隔离 MySQL 8.0 与 Kafka 4.1.2 Testcontainers；嵌套容器通过 `TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal` 访问映射端口。
- RED：旧 Worker 原生产物（2026-08-29）可启动，但新用例无法从普通 Topic 读回资源日志，1/1 失败；这份旧产物不能证明当前接线。
- 发布：`pnpm test:aot:worker:publish:linux` 退出码 0，精确告警门禁接受 15 条已登记第三方告警，产出 86,092,792 字节的 linux-x64 原生可执行文件。发布耗时 420,256 毫秒不是容量数据。
- GREEN：新产物运行 `NativeWorkerKafkaLogMySqlE2ETests` 1/1 通过、0 跳过，耗时 2 分 52 秒。用例配置非生产 `ApplicationKafka`、临时 CA、SASL_SSL/PLAIN、指定 Topic 的 Write/Describe ACL；真实 Worker 通过健康启动后，普通 Topic 可读回资源日志，JSON `LogEventId` 等于 Kafka key；SIGTERM 后 30 秒内以退出码 0 停止，进程日志无原生致命标记。
- 本地运行命令：

  ```powershell
  docker run --rm --network host -v /var/run/docker.sock:/var/run/docker.sock -v /g/wwwroot/github_fork/Full.NET:/src -w /src -e DOCKER_HOST=unix:///var/run/docker.sock -e TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal -e FULLNET_TESTCONTAINERS_REUSE=0 fullnet-native-aot-publish-sdk:10.0 dotnet tests/Full.NET.IntegrationTests/bin/Release/net10.0/Full.NET.IntegrationTests.dll --no-ansi --progress off --filter FullyQualifiedName~NativeWorkerKafkaLogMySqlE2ETests --minimum-expected-tests 1 --timeout 15m
  ```

- Worker Native AOT 聚合门禁新增必需类型发现检查，避免最低用例数量掩盖本用例遗漏。`pnpm test:aot:worker:analyzers` 0 警告、0 错误，`pnpm test:dotnet:architecture --selection api-native-aot` 73/73，治理测试 12/12、工具测试 52/52 已通过；正式 Linux CI 终态未取得。
- 边界：只验证普通启动日志与进程正常停止；Worker 优先事件、目标平台 Secret/轮换、跨进程崩溃重放、SDK/native 内存峰值、最终存储查询和容量尚未验证。生产 `ApplicationKafka` 仍关闭，状态为 `Capacity-not-verified`。
