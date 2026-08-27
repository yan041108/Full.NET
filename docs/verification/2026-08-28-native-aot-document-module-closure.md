# Document 模块 Native AOT 闭环验证

## 结论

Document Host API 已完成 Native AOT 静态 SQL 参数闭包和已覆盖核心链路的行物化验证。Linux `linux-x64` 原生产物在 SQL Server 和 MySQL 上均通过真实外部进程业务链路；本记录不把无真实版本数据的物化器注册外推为运行时全覆盖，也不外推为文件上传、匿名分享、Auditing、Worker 或 Migrator 的 Native AOT 闭环。

## 变更边界

- 用固定键名的 `DocumentSqlParameters` 替换 Document SQL 调用点中的匿名参数对象。
- 在 `FULLNET_AOT_COMPILE` 下注册 12 个 Document 持久化记录物化器。
- 物化器对当前 SQL 承诺的列失败关闭；仅对明细投影明确未选择的兼容属性保持普通 Dapper 的默认值语义。
- 核心 Native E2E 增加分类冲突、标签、文档、权限、分享、分页、统计与回收站读取链路。
- 未修改数据库结构、SQL 业务语义、公共 HTTP 契约、权限或生产配置。

## RED 证据

新增的两项 Architecture 门禁在实现前均失败：14 个 Document 服务文件仍使用匿名 SQL 参数，且模块没有 AOT 物化器 contributor。该结果证明门禁能捕获本次修复目标。

## GREEN 与原生进程证据

| 验证 | 结果 |
| --- | --- |
| `pnpm test:dotnet:architecture --selection api-native-aot` | 53/53，通过 |
| `pnpm test:aot:analyzers` | 构建通过，0 警告、0 错误 |
| `pnpm test:aot:publish:linux` | 通过；9 条允许的第三方警告；ELF 72,144,192 bytes |
| Linux 原生进程 SQL Server/MySQL 核心 E2E | 2/2，通过；2 分 40 秒 |
| `pnpm test:inner -- --snapshot native-aot-document-20260828` | 4/4，通过 |
| `pnpm test:slice -- --snapshot native-aot-document-20260828` | 20 成功、0 失败、5 个 Linux-only 用例在 Windows Inconclusive；命令因此退出 1 |
| `pnpm test:governance` | 52/52，通过 |
| `pnpm test:naming` | 29/30；失败是基线已有迁移 100 的 4 条 FNSQL003，任务未触碰对应文件 |
| `git diff --check` | 通过 |

原生进程 TRX：`artifacts/native-aot/linux-x64/test-results/Full.NET.IntegrationTests-native-aot-document-linux-local.trx`。

## 调试记录

首次双库原生运行在文档详情物化时因投影不含 `DocumentNo` 而出现 `IndexOutOfRangeException`。普通 Dapper 对未投影属性保留默认值；修正后的静态物化器仅对已登记的兼容属性允许缺列，对当前投影必需列继续失败关闭。重新发布后 SQL Server/MySQL 均通过已覆盖链路。

## 剩余边界

- 文件上传依赖真实 Files 存储，不在本次原生 E2E 中执行。
- 因未创建文件版本，`DocumentVersionRecord` 与非空 `DocumentStatisticsByTypeRecord` 只完成静态注册和编译闭包，未宣称真实行运行时覆盖。
- 匿名分享安全语义未改变，也未扩大测试范围。
- Auditing 是下一个独立 Native AOT 模块切片。
- Worker 与 Migrator 不属于本次 Host.Api 原生产物结论。
