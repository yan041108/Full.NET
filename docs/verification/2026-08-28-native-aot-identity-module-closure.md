# Identity 模块 Native AOT 闭环验证

## 结论

Identity Host.Api 可达 SQL 路径已完成 Native AOT 静态参数与行物化闭包。Linux `linux-x64` 原生产物已在 SQL Server/MySQL 上登录 Host 管理员，创建真实 Host 用户；重复用户名返回 `identity.users.username_exists`，分页与按 ID 读回均命中本次创建行。

## 变更边界

- Identity 模块匿名 SQL 参数替换为固定键名字典，保留 SQL、scope、事务、乐观并发、超级管理员保护、字段投影与认证语义。
- 补齐 Host 用户/角色/菜单/会话/API Key/TOTP/机构投影/字段投影/超级管理员等行物化器；登录、刷新会话、审计与用户/角色/菜单 Insert record 绑定器仍注册。
- 可变列序的 Host 用户列表/详情、字段投影裁剪档案按列名读取；其余稳定投影使用 ordinal。
- 新建用户/角色/菜单行的空 `LockoutEndUtc`/`UpdatedAtUtc` 使用 SQL 字面量 `NULL`，避免 Native AOT 下未标注 DbType 的空参数在 SQL Server 被推断为 nvarchar。
- 原生 E2E 创建唯一 Host 用户后必须冲突关闭，再读分页与详情，不以空结果冒充物化覆盖。
- `AotDataReaderExtensions.ReadInt64` 供机构投影 `SourceVersion`（bigint）双库读取。

## 验证结果

| 验证 | 结果 |
| --- | --- |
| Identity Architecture RED | 初始按预期失败：匿名参数 34 个文件，随后换行匿名对象 12 个文件；缺失 materializer |
| Identity Architecture GREEN | 2/2，通过 |
| Identity AOT compile | `-p:FullNetAotAnalysis=true` 0 警告、0 错误 |
| Integration Release build | 0 警告、0 错误 |
| `pnpm test:aot:analyzers` | 0 警告、0 错误 |
| `pnpm test:aot:publish:linux` | 通过；9 条允许的第三方警告；ELF 72,143,504 bytes |
| Linux Native SQL Server/MySQL | 5/5，通过；4 分 45 秒 |
| Native AOT/Dapper Architecture | 65/65，通过 |
| `pnpm test:inner -- --snapshot native-aot-identity-20260828` | 18/18（Identity + smoke），通过 |
| `pnpm test:governance` | 52/52，通过 |
| 独立代码审查 | 无 Critical/Important/Minor；未改变公开 API、租户或超级管理员语义 |

原生进程 TRX：`artifacts/native-aot/linux-x64/test-results/Full.NET.IntegrationTests-native-aot-identity-linux-local.trx`。

## Harness 调试记录

Architecture 选择器不得与 `pnpm test:aot:analyzers` 并行编译：AOT 属性会泄漏到常规 Release 构建，出现 `IL2026`/`IL3050` 与 Fusion `SystemTextJson` 缺失。串行重跑后 65/65。

Docker publish 与 Linux 测试容器会把 `project.assets.json` 指到容器 NuGet 路径。Windows 上 inner 首次报 `NETSDK1064`；执行 `dotnet restore Full.NET.slnx --force-evaluate` 后原命令通过。这与 Tenancy/Organization 闭环同一类本地编排问题，不是 Identity 产品缺陷。

换行形式的 `new { ... }` 不会被字面量 `new {` 搜索命中，Architecture 门禁使用 `new\s*\{` 才能关闭。

## 剩余边界

- 本记录只声明 Host.Api 的 Identity API 可达闭包。不声明 Worker CDC 投影、切流/回退或 Migrator Native AOT。
- 原生 E2E 覆盖 Host 用户创建、用户名冲突、分页与按 ID 读取；角色、菜单、TOTP、API Key 等其余 SQL 由 Architecture 物化器登记与 JIT inner 覆盖，未在本次原生产物上逐条写读。
- Windows 上 `pnpm test:aot:native:e2e` 只做发现 skip，不能替代本次 Linux 原生产物证据。
