# CodeGeneration 模块 Native AOT 闭环验证

## 结论

CodeGeneration Host.Api 可达 SQL 路径已完成 Native AOT 静态参数与行物化闭包。Linux `linux-x64` 原生产物在 SQL Server/MySQL 上读取数据库表、列目录及模板/运行分页；Worker 检查点清理只完成静态绑定，不宣称 Worker Native AOT 运行闭环。

## 变更边界

- 九个 SQL 调用文件的匿名参数替换为固定键名字典，保留 SQL、事务、乐观并发及 Apply/Rollback 终态语义。
- 补齐 CatalogColumn、Template、Run、CheckpointCleanup materializer，连同既有 CatalogTable 共五类。
- 原生 E2E 实际读取非空数据库表与列目录，创建模板与 preview run，再从分页按对应 ID 读回；不执行会写工作区的 Apply/Rollback。

## 验证结果

| 验证 | 结果 |
| --- | --- |
| CodeGeneration Architecture RED | 2/2 按预期失败：9 个匿名参数文件、4 个缺失 materializer |
| CodeGeneration Architecture GREEN | 2/2，通过 |
| CodeGeneration Release build | 0 警告、0 错误 |
| Integration Release build | 0 警告、0 错误 |
| `pnpm test:aot:analyzers` | 0 警告、0 错误 |
| `pnpm test:aot:publish:linux` | 通过；9 条允许的第三方警告；ELF 72,148,064 bytes |
| Linux Native SQL Server/MySQL | 审查修复后 5/5，通过；6 分 30 秒 |
| Native AOT Architecture | 33/33，通过 |
| `pnpm test:inner -- --snapshot native-aot-codegeneration-20260828` | 12/12，通过 |
| `pnpm test:governance` | 52/52，通过 |
| 独立代码审查 | 初审 1 个 Important 已修复；复审无 Critical/Important |
| `git diff --check` | 通过 |

原生进程 TRX：`artifacts/native-aot/linux-x64/test-results/Full.NET.IntegrationTests-native-aot-codegeneration-linux-local.trx`。

## Harness 调试记录

首次隔离构建把测试程序集放在 `/tmp`，仓库定位器在应用启动前失败；第二次虽移入仓库，但 sibling 数据库地址仍解析为容器内 localhost。最终设置 `TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal` 后通过。这两次失败均未触及生产应用逻辑。

独立审查指出空分页不能证明 Template/Run 行物化。测试随后改为创建有效模板、生成 preview run，并要求分页按 ID 命中；修复后的双库原生套件通过。

## 剩余边界

- Checkpoint Retention 仅在 Worker 装配，本记录不宣称其原生进程运行验证。
