# 命名治理验证记录

- 日期：2026-07-21（自动化矩阵增补）
- 状态：`Build-verified`（治理门禁）；1.0 前 Tenancy/Outbox 持久化规范化见[专项验证记录](pre-v1-naming-normalization.md)
- 代码基线：`5d9f775`
- 范围：Naming Profile、精确债务、SQL/迁移/C#／稳定协议门禁、CodeGeneration 命名内核和项目 Skill 接入

## 已实现能力

- `contracts/naming/fullnet-naming-profile.json` 是 Node、Architecture Tests 与 C# 命名内核的共同事实源；`examples.json` 同时驱动 Node/C# 行为一致性测试。
- SQL 门禁扫描 `src/**/*.sql` 与明确登记的 C# 静态 SQL 容器，检查表/列/索引/约束、64 字符上限、迁移文件配对和 `SELECT *`。动态 `EXEC/PREPARE`、DDL 字符串以及当前不支持的 View/Procedure/Drop/Rename 不会假装安全通过，必须人工审查并精确登记或扩展受控解析器。
- Architecture Tests 从运行时权限 Contributor、ErrorCodes Catalog、集成事件 Handler 与 `SqlStatement` 字段枚举稳定值，不用源码正则替代运行时契约。
- `Full.NET.Data.CodeGeneration` 嵌入 Profile，提供框架/项目表名、长索引/约束 SHA-256 确定性摘要及列、权限、错误、消息、Statement ID 校验。
- 项目模块交付 Skill 已要求读取命名规范、运行 `pnpm test:naming`，并禁止通配债务和模板自行实现命名算法。

## 新鲜自动验证

| 验证 | 结果 |
|---|---|
| `pnpm test:naming` | 12/12 通过，包含仓库 SQL 扫描、双库迁移配对和 Node/C# 共享样例 |
| `dotnet build Full.NET.slnx -c Release --no-restore` | 0 警告、0 错误 |
| CodeGeneration Unit Tests | 17/17 通过，包含固定 10 万名称样例无碰撞 |
| 全量 Unit Tests | 203/203 通过 |
| Compatibility Tests | 5/5 通过 |
| Architecture Tests | 15/15 通过 |
| SQL Server/MySQL Integration Tests | 18/18 通过，Docker Server 29.6.1 |
| 项目 Skill 契约 | 35 项检查通过 |
| `skill-creator` 官方 `quick_validate.py` | `Skill is valid!` |

## 存量债务与停止条件

当前债务清单共 **87** 项（2026-07-25）：列 14、动态 SQL 16、错误码 22、消息类型 1、未命名主键 11、查询 1、表名 17、不支持 SQL 5。2026-07-25 补登 MySQL `017_OutboxDeadLetter` 与 `019_TenancyTenantPackageAssignment` 的 `dynamic_sql`——两者用 `INFORMATION_SCHEMA` + `PREPARE` 实现可重入列/外键追加（MySQL 无 `ADD COLUMN IF NOT EXISTS`），此前未登记导致 `pnpm test:naming` 长期红灯。`fn_tenant_tenant` 与 Outbox legacy 列等项已在 011 Contract 后从运行时路径清除，但仍因历史迁移脚本保留在精确债务清单；另增 `015_HostRoleDataScope` 双库 `dynamic_sql` +2。

本能力不能标记为 `Verified`：协议别名排空与生产备份介质升级演练尚未完成；完整元数据/模板生成器与重复生成快照尚未实现，动态 SQL 仍需要人工审查。Tenancy/Outbox 持久化 010/011 与逻辑克隆升级演练自动化证据见[1.0 前命名规范化验证记录](pre-v1-naming-normalization.md)。
