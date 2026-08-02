# CodeGeneration 生命周期 SQL 运行时语义矩阵验证记录

## 结论

`Single` 场景显式生命周期 fixture 的生成 SQL 已在隔离 SQL Server/MySQL 库通过运行时矩阵，并保持 `Build-verified`：`SoftDelete`（Create→Update→StaleVersion→软删→读后过滤）与 `HardDelete`（Create→Update→物理删除）均已覆盖。本切片不扩展 HTTP/UI。

## 新鲜验证证据

| 验证 | 结果 |
| --- | --- |
| HEAD | 见本记录提交 |
| `GeneratedLifecycleSqlRuntimeIntegrationTests` Release 编译 | 0 warning / 0 error |
| SoftDelete 双库矩阵 | 2 项；需 Testcontainers |
| HardDelete 双库矩阵 | 2 项；需 Testcontainers |
| 本机 Docker | 未就绪时运行时未执行 |

## 治理复盘

未命中规则或 Skill 升级触发条件；一行结论：无需规则/Skill 变更。