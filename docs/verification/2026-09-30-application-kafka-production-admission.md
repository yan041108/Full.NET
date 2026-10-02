# ApplicationKafka 生产入口本地准入验证

- 日期：2026-09-30；来源为用户批准的全项目本地验收标准，以及继续推进生产 ApplicationKafka 门禁的授权。
- 基线：`c609759576c4bf54ff9e743161ecb829ca8d4d4a` 加任务快照 `logging-application-kafka-production` 之后的工作区增量。工作区既有其他日志开发改动，本记录不宣称全部仓库能力验收完成。
- 环境：Windows PowerShell、.NET 10、Docker Desktop 29.6.2（共享主机），Linux SDK 10.0.400 容器、Kafka 4.1.2、隔离 SQL Server/MySQL 与 Elasticsearch 测试容器。没有生产部署/切流。
- 当前状态：本地验收通过，生产 API/Worker 的 ApplicationKafka 入口配置门禁开放。两个 Production 原生用例均通过，0 失败、0 跳过；默认仍为 Legacy，未执行生产部署/切流。

## 变更与确认范围

`ServiceDefaultsExtensions` 和 Helm 移除 Production ApplicationKafka 环境硬拦截；保留 Local 禁止、旧 ES 冲突、同值预期模式、静态 Adapter、TLS/双 Topic/冻结路由/专用 Producer Secret。Collector 使用 applicationkafka Pod 标签排除同一日志流。默认仍是 Legacy，不自动切流。

Production 原生用例覆盖真实环境双变量，配置本次生成的 JWT 密钥、Data Protection PFX 与独立 Key Ring，不使用开发临时签名选项；断言 Broker 读回的启动资源事件 `Environment=Production`。未使用的 S3/OSS Provider 仅提供测试占位配置以满足既有生产校验，本测试不执行云文件外部调用或宣称这些 Provider 已验收。宿主先退出，再清理测试证书与 Key Ring。

入口确认边界止于应用受限 Producer 的 Broker 投递报告。入队 Accepted 不等于 ACK；默认没有应用磁盘 Spool，进程崩溃会丢失未确认事件且不跨重启重放。Broker ACK 不表示 ES 索引完成。B0/B1 审计仍走原持久化边界；独立 LogConsumer 的实验开关和消费确认契约不因入口准入改变。容量/P99、生产 Secret/证书轮换、物理节点故障域和最终存储按实际范围另行验证。

## 已完成的本地验证

| 命令 | 实际结果 |
| --- | --- |
| `dotnet build Full.NET.slnx -c Release --nologo -clp:ErrorsOnly` | 退出 0，0 警告/错误 |
| `pnpm test:dotnet:unit -- --no-build --filter 'FullyQualifiedName~HighPriorityLoggingTests\|FullyQualifiedName~KafkaLogProducer\|FullyQualifiedName~KafkaLogDeliveryLane' --minimum-expected-tests 1` | 74/74，0 跳过 |
| `node --test tests/deployment/helm-contract.test.mjs` | 17/17，含双角色 Production Secret/CA/路由正反例 |
| `node scripts/testing/run-integration-shard.mjs logging-kafka` | 13/13，0 跳过；Production TLS/SASL Host 双 Topic/ID 读回、错误凭据/ACL 拒绝、Broker 中断恢复；并回归现有 ES/DLQ/Offset、SIGKILL/重平衡场景 |
| `pnpm test:integration:smoke` | SQL Server/MySQL 合计 8/8，0 跳过 |
| `pnpm test:dotnet:architecture -- --no-build --selection api-native-aot` | 73/73，0 跳过 |
| `pnpm test:aot:analyzers`、`pnpm test:aot:worker:analyzers` | 两者退出 0，0 警告/错误 |
| `pnpm test:governance` | 57/57，0 跳过 |
| `pnpm test:integration:tooling` | 53/53，0 跳过 |
| `pnpm test:integration:partitions` | 1096 项分片无遗漏/重复 |
| `pnpm test:observability-deploy` | 9/9，0 跳过 |
| `pnpm test:aot:publish:linux` | 串行重跑退出 0；17 条现有精确白名单告警，linux-x64 可执行文件 133,360,528 字节 |
| `pnpm test:aot:worker:publish:linux` | 串行重跑退出 0；15 条现有精确白名单告警，linux-x64 可执行文件 86,092,792 字节 |
| `dotnet build tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --nologo -clp:ErrorsOnly` | 补齐生产测试配置后重新构建，退出 0，0 警告/错误 |
| 下方 Linux Production 原生运行命令 | 2/2，0 失败、0 跳过，退出 0；7 分 05.860 秒，API 普通/优先及 Worker 资源日志 Broker 读回、ID/Production 环境断言、SIGTERM 退出 0 |

```powershell
docker run --rm --network host -v /var/run/docker.sock:/var/run/docker.sock -v /g/wwwroot/github_fork/Full.NET:/src -w /src -e DOCKER_HOST=unix:///var/run/docker.sock -e TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal -e FULLNET_TESTCONTAINERS_REUSE=0 fullnet-native-aot-publish-sdk:10.0 dotnet tests/Full.NET.IntegrationTests/bin/Release/net10.0/Full.NET.IntegrationTests.dll --no-ansi --progress off --filter 'FullyQualifiedName~NativeApiKafkaLogMySqlE2ETests|FullyQualifiedName~NativeWorkerKafkaLogMySqlE2ETests' --minimum-expected-tests 2 --timeout 25m --results-directory artifacts/native-aot/production-log-ingress/test-results --report-trx --report-trx-filename application-kafka-production.trx
```

产物与原始结果：API/Worker 分别为 `artifacts/native-aot/linux-x64/publish-manifest.json`、`artifacts/native-aot/worker/linux-x64/publish-manifest.json`；最终 TRX 为 `artifacts/native-aot/production-log-ingress/test-results/application-kafka-production.trx`。成功运行日志分别为 `artifacts/native-aot/linux-x64/test-logs/fullnet-native-aot-70159ac5a7de4d3e881443732a37a676.log`、`artifacts/native-aot/worker/linux-x64/test-logs/fullnet-native-worker-runtime-mysql-df161771ebfe4575bc992d629a7832ce.log`。本次未验证 Worker 优先事件的原生触发；Producer 双通道隔离由聚焦单元、真实 Broker 和 API 原生双通道用例覆盖，不把 Worker 资源日志用例扩大为该范围。

先增加 Production 断言，Host 与 Helm 均因旧环境拦截失败；解除拦截后上述用例通过。首次同时发布 API/Worker 时 Docker 返回 `unexpected EOF`，两命令均退出 125；它们不计通过，后续改为串行发布。API/Worker 串行发布分别耗时 1,210,960/637,653 毫秒，仅是构建记录，不是容量数据。

第一次 Production API 原生运行失败：测试夹具仍使用 `Files:Storage:DefaultProviderKey=local`，生产存储校验拒绝启动，进程退出 134，测试结果 1 失败、0 通过/跳过。已仅在夹具指定 `s3` 并提供未被调用的测试配置；生产校验未放宽。初次失败日志位于 `artifacts/native-aot/linux-x64/test-logs/fullnet-native-aot-5974fd24ec7a4e0bb794094aa2fd7e53.log`，不能计作通过。

独立只读审查未发现本次准入修改的阻塞问题。`pnpm test:integration:affected:plan -- --snapshot logging-application-kafka-production --phase slice` 确认本任务 15 个变更文件选择 integration-matrix、logging-kafka、native-aot 与 smoke；本次按受影响行为完成上述本地验证，其中原生运行聚焦两个受改动的生产 Kafka 日志用例，未重跑全项目矩阵或全体原生集合。原生用例/产物结果以各自 TRX、日志与 manifest 为依据，未由先前非生产记录代替。`git diff --check` 通过；保留既有及无关工作区改动，未提交或推送。
