# Messaging 模块 Native AOT 闭环验证

## 结论

Messaging Host.Api 可达的死信查询与重放 SQL 路径已完成 Native AOT 静态参数和行物化闭包。Linux `linux-x64` 原生产物已在 SQL Server/MySQL 上读取真实失败 Inbox 行，并在重放路径读取对应 Outbox 信封；未登记订阅路由按稳定 ProblemDetails 失败，未扩大消息可靠性语义。

## 变更边界

- 死信列表、按键查询与 Outbox 信封查询不再向泛型执行器传递匿名参数，统一使用固定键名字典。
- 注册 `DeadLetterRecord` 与 `OutboxEnvelopeRecord` 的静态 ordinal materializer，列序与既有 SQL 投影保持一致。
- 原生 E2E 在 API 启动后向当前测试数据库写入唯一 Outbox/失败 Inbox 对，要求分页按消息 ID 命中，并要求重放返回 `messaging.subscription_route.not_found`；因此列表和 Outbox 信封物化均有非空运行证据。
- 测试种子仅位于 Integration harness；生产执行、事务、订阅和重放语义未改变。

## 验证结果

| 验证 | 结果 |
| --- | --- |
| Messaging Architecture RED | 2/2 按预期失败：2 个匿名参数文件、2 个缺失 materializer |
| Messaging Architecture GREEN | 2/2，通过 |
| Integration Release build | 0 警告、0 错误 |
| `pnpm test:aot:analyzers` | 0 警告、0 错误 |
| `pnpm test:aot:publish:linux` | 通过；9 条允许的第三方警告；ELF 72,152,176 bytes |
| Linux Native SQL Server/MySQL | 5/5，通过；4 分 34 秒 |
| Native AOT/Dapper Architecture | 56/56，通过 |
| `pnpm test:inner -- --snapshot native-aot-messaging-20260828` | 4/4，通过 |
| `pnpm test:governance` | 52/52，通过 |
| 独立代码审查 | 无 Critical/Important/Minor |
| `git diff --check` | 通过 |

原生进程 TRX：`artifacts/native-aot/linux-x64/test-results/Full.NET.IntegrationTests-native-aot-messaging-linux-local.trx`。

## 证据边界

- 原生 E2E 使用未登记的消费者和消息类型，预期停在订阅目录校验，避免执行真实业务消费副作用；到达该稳定错误前必须先读取并物化死信与 Outbox 信封。
- 本记录只声明 Host.Api 的 Messaging 运维读取/重放准备路径已闭环，不声明 Worker、CDC Relay、Kafka Delivery 或完整消息消费链路已经 Native AOT 化。
