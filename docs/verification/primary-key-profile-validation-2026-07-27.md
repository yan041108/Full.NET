# Primary Key Profile 兼容性校验验证记录

- 日期：2026-07-27（Asia/Shanghai）
- 分支：`codex/primary-key-profile-validation`
- 初始基线：`main@fd3257dbfb0000389b24acc1e9370e7196a33c5d`
- 最终同步基线：`main@fa3449442bcce350872088084268771697a5a7e8`
- 状态：实现与最终同步验证已完成，等待 fast-forward 合入与清理

## 范围与契约

本切片修复代码生成器主键配置档兼容性判断：

- `UuidV7` 只与 `UuidV7` 兼容；
- `Snowflake` 只与 `Snowflake` 兼容；
- 任意未受支持的 `PrimaryKeyProfile` 值即使左右相同，也不得被误判为兼容；
- `Resolve` 的 C#、SQL Server、MySQL 与 JSON 物理映射保持不变；
- 不改变数据库对象、迁移、API、配置、客户端或 canonical 测试数量。

## RED / GREEN

| 阶段 | 证据 |
| --- | --- |
| 基线 | `PrimaryKeyTypeMappingTests` **5/5** |
| RED | 在现有互斥用例中加入两个相同非法枚举值后，`AreProfilesCompatible` 实际返回 `true`，用例按预期失败 |
| GREEN | 兼容性判断显式限定当前受支持档后，同一聚焦套件 **5/5**，失败 0、跳过 0 |

## 验证

| 最终同步门禁 | 结果 |
| --- | --- |
| Release 全解决方案构建 | 0 warning / 0 error |
| Unit | **407/407**，失败 0、跳过 0 |
| PrimaryKey 聚焦 | **5/5**，失败 0、跳过 0 |
| Compatibility | **7/7**，失败 0、跳过 0 |
| Architecture | **49/49**，失败 0、跳过 0 |
| Naming | **23/23** |
| Governance | **11/11** |
| Skill 契约 | **52** 项通过 |
| workspace | 通过 |

## 规则与 Skills 复盘

- 规则：本次是已有“不支持的主键档不得进入生成边界”基线下的单次布尔判断遗漏，已由现有
  Unit 方法自动化；没有重复遗漏、规则歧义或高风险事故证据，本次不新增或修改规则。
- Skills：本切片只有单一纯函数兼容性守卫，没有形成三个以上需要工程判断的高复用流程，
  也不属于 `fullnet-module-delivery` 交付范围，本次无 Skills 变化。

## 状态结论

本切片达到 `Build-verified`：未受支持的主键配置档即使左右值相同，也不会再被关系图兼容性
预检放行；现有 UUID v7 与 Snowflake 物理映射和互斥语义保持不变。
