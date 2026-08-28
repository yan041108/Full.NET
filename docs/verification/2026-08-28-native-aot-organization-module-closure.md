# Organization 模块 Native AOT 闭环验证

## 结论

Organization Host.Api 可达 SQL 路径已完成 Native AOT 静态参数与行物化闭包。Linux `linux-x64` 原生产物已在 SQL Server/MySQL 上进入 `local` 租户，创建真实机构、职级和职位；重复机构编码返回 `organization.units.code_exists`，分页与按 ID 读回均命中本次创建行。机构创建的事务 Outbox 在 SQL Server 上同步覆盖 Messaging 事件流所有权物化。

## 变更边界

- Organization 模块匿名 SQL 参数替换为固定键名字典，保留 SQL、scope、事务、乐观并发、数据范围与 Outbox 语义。
- 补齐 11 个行物化器；五个 Insert record 绑定器仍注册以满足 Architecture 门禁，运行时写入已改为字典插入。
- 新建行的 `UpdatedAtUtc` 使用 SQL 字面量 `NULL`，避免 Native AOT 下未标注 DbType 的空参数在 SQL Server 被推断为 nvarchar。
- 原生 E2E 创建唯一机构后必须冲突关闭，再读分页与详情；随后创建职级/职位并按 ID 与分页命中，不以空结果冒充物化覆盖。
- 机构创建会写入 `IdentityOrganizationUnitChangedIntegrationEvent`。SQL Server Native AOT 上 `fn_messaging_stream_ownership.CurrentOwner` 等列为 tinyint，`GetInt32` 抛出 `InvalidCastException`；Messaging 物化器改为 `AotDataReaderExtensions.ReadInt32`。这是 Organization Outbox 路径暴露的双库物化缺陷，不是机构 INSERT 本身。

## 验证结果

| 验证 | 结果 |
| --- | --- |
| Organization Architecture RED | 初始按预期失败：匿名参数与缺失 materializer/insert binder |
| Organization Architecture GREEN | 2/2，通过 |
| Integration Release build | 0 警告、0 错误 |
| `pnpm test:aot:analyzers` | 0 警告、0 错误 |
| `pnpm test:aot:publish:linux` | 通过；9 条允许的第三方警告；ELF 72,147,936 bytes |
| Linux Native SQL Server/MySQL | 审查修复后 5/5，通过；6 分 22 秒 |
| Native AOT/Dapper Architecture | 63/63，通过 |
| `pnpm test:inner -- --snapshot native-aot-organization-20260828` | 4/4 smoke，通过 |
| `pnpm test:governance` | 52/52，通过 |
| 独立代码审查 | SQL Server 机构创建 500 已定位为 Messaging tinyint 物化并修复；复审无剩余 Critical/Important/Minor |

原生进程 TRX：`artifacts/native-aot/linux-x64/test-results/Full.NET.IntegrationTests-native-aot-organization-linux-local.trx`。

## Harness 调试记录

首次 Linux 原生核心套件为 4/5：MySQL 完整关键 HTTP 流通过，SQL Server `POST /api/v1/organization/units` 返回 `common.unexpected`。日志 `InvalidCastException` 发生在 `ReadEventStreamOwnershipPersistenceRow` 的 `SqlBuffer.get_Int32()`，而不是机构 INSERT。字典化插入与 `UpdatedAtUtc` SQL `NULL` 之后该 500 仍在，直到 tinyint 改为 `ReadInt32` 后双库 5/5。

Docker publish 与 Linux 测试容器会把 `project.assets.json` 指到 `/root/.nuget/packages/`。Windows 上 inner 首次报 `NETSDK1064`；执行 `dotnet restore Full.NET.slnx --force-evaluate` 后原命令通过。这与 Tenancy 闭环同一类本地编排问题，不是 Organization 产品缺陷。

任务快照 JSON 曾带 UTF-8 BOM，导致 `--plan` 无法解析；去掉 BOM 后选择器恢复正常。快照本身不进入提交。

## 剩余边界

- 本记录只声明 Host.Api 的 Organization API 可达闭包，以及该路径触发的 Messaging 事件流所有权行物化修复。不声明 Worker CDC 投影、切流/回退或 Migrator Native AOT。
- Insert record 绑定器仍注册，但当前创建路径不再实例化这些 record。
- Windows 上 `pnpm test:aot:native:e2e` 只做发现 skip，不能替代本次 Linux 原生产物证据。
