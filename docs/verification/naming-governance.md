# 命名治理验证记录

- 日期：2026-07-18
- 状态：`Implemented`
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

当前债务清单共 90 项：列 6、动态 SQL 5、错误码 24、消息类型 2、未命名主键 10、查询 1、Statement ID 37、表名 5。每项均包含精确类型、值、文件、原因和 `M1.0` 移除里程碑；文件移动或新值不会被放行。

本能力不能标记为 `Verified`：1.0 前 Expand/Contract 规范化尚未执行，完整元数据/模板生成器与重复生成快照尚未实现，动态 SQL 仍需要人工审查。具体迁移服从[1.0 前存量命名规范化计划](../superpowers/plans/2026-07-18-pre-v1-naming-normalization.md)，禁止修改已执行迁移或在本门禁任务中静默改变已发布协议值。
