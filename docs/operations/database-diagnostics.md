# 数据库诊断与 Provider 支持矩阵

## 连接与诊断入口

数据库正式 Provider 当前为 `SqlServer`、`MySql`，由 `DatabaseOptions`、`DatabaseProvider` 与 `DbConnectionFactory` 定义。API、Worker、Migrator 的 appsettings 不保存连接密码；运行时读取 `Database__ConnectionString` 或 `ConnectionStrings__fullnet`，Aspire 通过资源引用注入。**诊断使用独立账户与配置，不把元数据账户替换进业务或迁移进程。**

在仓库根目录执行，首次使用或工具源码变化后先构建：

```powershell
pnpm run db:diagnose:build
pnpm run db:diagnose --provider SqlServer --check connection
pnpm run db:diagnose --provider MySql --check connection
pnpm run db:diagnose --provider SqlServer --check catalog --table fn_identity_user
pnpm run db:diagnose --provider MySql --check indexes --table fn_identity_user
pnpm run db:diagnose --provider SqlServer --check blocking
pnpm run db:diagnose --provider MySql --check all
# 脚本消费 JSON 时直接使用 Node，避开 pnpm 的命令标题。
node scripts/database/diagnose.mjs --provider SqlServer --check all
```

`connection` 为默认检查；可选 `catalog`、`indexes`、`blocking`、`all`。`--table` 是严格校验并参数化的表名，SQL Server 固定 `dbo`，MySQL 固定连接所选数据库。没有任意 SQL 入口；不读取业务行、会话 SQL 原文、锁键值、列默认值或注释。输出为 JSON，包含 UTC 时间、检查结果、稳定错误分类与驱动数字码；不输出原始驱动异常或连接串。每项最多输出 100 行，存在第 101 行时设置 `truncated=true`，应按表缩小范围。连接超时 5 秒、查询超时 8 秒、单次查询取消预算 10 秒、整个连接与检查预算 45 秒。退出码 `0` 为所选检查成功，`1` 为连接或检查失败，`2` 为参数无效或尚未配置。

构建经仓库资源预算和隔离输出入口运行。输出缓存按仓库路径区分，Windows 位于 `%LOCALAPPDATA%/Full.NET/database-diagnostics/`，Linux 位于 `${XDG_CACHE_HOME:-~/.cache}/Full.NET/database-diagnostics/`。构建在同一输出锁内记录工具源码、构建脚本、中央构建／依赖配置、SDK 版本与完整运行目录摘要；构建失败或输入变化不登记有效记录。启动时校验这些摘要，缓存失效时退出 `2` 并提示重建，不隐式构建或启动数据库。测试命令 `pnpm run test:database-diagnostics` 会先校验、必要时按资源锁重建，再测试当前产物；直接调用测试文件也会拒绝无效构建。该工具是独立 .NET 运维 CLI，不是新增业务 Provider 或 Host.Api 的 AOT 支持声明。

`pnpm run test:database-diagnostics:live` 使用现有真实栈的 Testcontainers 依赖与 `mysql:8.0` 临时容器，实际关闭 Performance Schema，验证 `connection`、`blocking`、`all` 的结果；运行前准备当前工具构建和 Docker Engine。测试遵循共享重型资源锁，结束后停止并移除临时容器，不修改开发容器的启动配置。

## 本机凭据与权限

默认先读取对应进程环境变量；未设置时读取本工具的 .NET User Secrets：

| Provider | 环境变量 | User Secrets 键 |
| --- | --- | --- |
| SQL Server | `FULLNET_DIAGNOSTICS_SQLSERVER_CONNECTION` | `ConnectionStrings:SqlServer` |
| MySQL | `FULLNET_DIAGNOSTICS_MYSQL_CONNECTION` | `ConnectionStrings:MySql` |

User Secrets ID 为 `fullnet-database-diagnostics`；开发机文件不加密，不进入 Git，也不用于生产凭据分发。更换连接可用隐藏输入：

```powershell
pwsh -File scripts/database/set-diagnostics-connection.ps1 -Provider SqlServer
pwsh -File scripts/database/set-diagnostics-connection.ps1 -Provider MySql
```

自动化可由秘密存储注入上述环境变量；`--connection-env ENV_NAME` 指定另一个变量，变量缺失时直接失败，不回退个人配置。不要把实际连接串放入命令参数、聊天、截图或报告。生产连接保持驱动的证书验证；本地 SQL Server 开发容器的自签名证书可在本机配置明确使用 `TrustServerCertificate=True`，不可推广至生产。

本地配置使用 `fullnet_diagnostics` 元数据账户：

- SQL Server：目标库 `VIEW DEFINITION`；阻塞检查另需 SQL Server 2022+ 的 `VIEW SERVER PERFORMANCE STATE`，旧版为 `VIEW SERVER STATE`。这项权限的服务器可见范围较广，应只授予批准的运维身份。工具主动检查权限，缺失时返回 `permission_denied`，不把只看到自身请求当成“无阻塞”。依据 [Microsoft DMV 权限说明](https://learn.microsoft.com/en-us/sql/relational-databases/system-dynamic-management-objects/sys-dm-exec-requests-transact-sql)。
- MySQL：目标库 `REFERENCES` 使相关对象元数据可见，不授予业务表 `SELECT` 或写入；对 `performance_schema.data_locks` 和 `performance_schema.data_lock_waits` 单独授予 `SELECT`。`blocking` 先检查 `@@performance_schema`，关闭时返回 `performance_schema_disabled`、该项 `ok=false` 和退出码 `1`，不把空表当成无阻塞；权限依据 [MySQL data_lock_waits 文档](https://dev.mysql.com/doc/refman/8.4/en/performance-schema-data-lock-waits-table.html)。

诊断命令只执行固定系统查询；账户权限仍由数据库控制。元数据可见性依赖账户，空结果不能单独证明对象不存在。阻塞输出只反映当前目标库的瞬时锁等待，未覆盖历史死锁、元数据锁、所有跨库等待或完整慢查询分析。

## 本机开发资源

2026-10-23 配置了已有 Aspire SQL Server 开发容器与新增 MySQL 专用开发容器。连接密码保存在本机 User Secrets，不在本文维护端口副本；检查当前地址使用：

```powershell
docker ps --format '{{.Names}} | {{.Image}} | {{.Ports}}'
docker start fullnet-diagnostics-mysql
docker stop fullnet-diagnostics-mysql
```

MySQL 容器名 `fullnet-diagnostics-mysql`，镜像 `mysql:8.0`，库名 `fullnet`，命名卷 `fullnet-diagnostics-mysql-data`，仅绑定 `127.0.0.1`。正常 stop/start 保留数据和映射；重新创建容器或 Aspire 资源可能改变端口、凭据或账户，需要重新配置诊断连接。初次连接检查时两个库都没有业务表，不能由此宣称迁移或应用链路已验收。新增 MySQL 尚未自动接入业务 Host 或执行 Full.NET 迁移。

## AI 排查流程

1. 从故障请求确定 Provider、环境和症状，先运行 `doctor --profile backend`；核对 appsettings、注入键和当前容器，只记录配置来源与是否存在，不打印值。
2. `connection` 判断连接配置、认证、数据库选择、版本与排序规则。分类为认证、权限、库不可用、超时或配置错误；`database_error` 的数字码结合匹配版本的官方文档和本机脱敏日志分析，不能猜成唯一根因。
3. 表／列／Guid 映射异常先用 `catalog --table`；索引或唯一约束问题用 `indexes --table`。对照 Dapper SQL、`DatabaseCatalogQueries`、DbUp 迁移和 SQL Server `uniqueidentifier`／MySQL RFC 9562 大端 `BINARY(16)` 契约。迁移版本与实际部署来源需另行核对，工具不自动运行 Migrator。
4. 锁等待／请求超时用 `blocking` 并对照 API/Worker 的时间、Trace 和事务边界；空结果只表示该时点、权限与查询范围内未发现，不能排除瞬时或其他类型等待。慢查询先分析源码和索引，必要时使用项目性能 skill；不自动开启 profiler、全局日志、执行生产 EXPLAIN ANALYZE、终止会话或创建索引。
5. 给出证据、候选根因、最小修复及验证范围。用户要求修复后遵循仓库双库与测试规则；保留其他会话改动。连接成功只证明诊断路径，不替代 API、迁移、租户隔离或端到端测试。

## PostgreSQL／Oracle 新 Provider 支持矩阵

用户已提出将 PostgreSQL、Oracle 作为新增 Provider 的方向；本轮登记接入范围，运行时枚举与正式双库基线保持当前实现。下表中“现有”表示仓库已有实现，具体验收仍以该功能的最新测试为准；“待实现”不得标成已支持。

| 能力 | SQL Server | MySQL | PostgreSQL | Oracle |
| --- | --- | --- | --- | --- |
| 正式 Provider／连接工厂 | 现有 `SqlServer` | 现有 `MySql` | 规划中，待实现 | 规划中，待实现 |
| 驱动与中央版本 | 现有 Microsoft.Data.SqlClient | 现有 MySqlConnector | 候选 Npgsql，尚未引入／冻结版本 | 候选 Oracle.ManagedDataAccess.Core，尚未引入／冻结版本 |
| 本工具连接／结构／索引／阻塞诊断 | 本机已验证 | 本机已验证 | 待实现 | 待实现 |
| Dapper SQL、分页、参数、返回值、并发语义 | 现有，修改时双库验证 | 现有，修改时双库验证 | 待实现方言与完整语义测试 | 待实现方言与完整语义测试 |
| UUID v7 逻辑主键 | `uniqueidentifier` | 大端 `BINARY(16)` | 候选原生 `uuid`，须验证字节序与往返 | 候选 `RAW(16)`，须冻结字节序与映射 |
| UTC、布尔、decimal、JSON／大字段 | 已有契约与实现 | 已有契约与实现 | 待冻结类型、精度及 UTC 映射 | 待冻结类型、空字符串／NULL、精度及 UTC 映射 |
| DbUp、schema journal、种子与恢复 | 现有 | 现有 | 待实现／验证迁移执行器 | 待实现／验证迁移执行器 |
| 代码生成元数据目录／CRUD | 现有 | 现有 | 待实现 catalog 与生成 SQL | 待实现 catalog 与生成 SQL |
| 租户隔离、Outbox、租约、幂等、重试分类 | 现有 | 现有 | 待同场景验证 | 待同场景验证 |
| Native AOT 静态绑定与 Linux 发布运行 | 按现有 AOT 门禁 | 按现有 AOT 门禁 | 待驱动／Dapper／Host 全闭包验证 | 待驱动可行性与全闭包验证 |
| API／Worker／Migrator／Aspire／真实栈 E2E | 现有入口 | 现有入口 | 待扩展测试矩阵与编排 | 待扩展测试矩阵与编排 |

接入前需确定数据库最低／目标版本、驱动版本与许可证、大小写／标识符策略、类型与排序规则，以及 AOT 能力边界。PostgreSQL 的 `uuid` → `Guid`、UTC timestamp 映射参见 [Npgsql 类型文档](https://www.npgsql.org/doc/types/basic.html)；Oracle 候选驱动包参见 [ODP.NET Core 安装文档](https://docs.oracle.com/en/database/oracle/oracle-database/26/odpnt/InstallODPCore.html)。候选类型不是已批准物理契约；尤其要验证 PostgreSQL 名称折叠、Oracle 名称大小写、空字符串语义与 Full.NET 列映射的冲突。

实施顺序建议为：冻结契约与版本 → 连接／诊断适配 → 迁移与基础类型 → 全部受影响模块 SQL／可靠性语义 → 元数据生成器 → API/Worker/真实栈与 AOT 验收。不能只增加 enum 或安装驱动就声明 Provider 支持；正式纳入时同步数据规则、测试矩阵和能力状态。
