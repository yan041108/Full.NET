# CodeGeneration Database Schema Import 验证记录

- 日期：2026-07-30（closeout 2026-08-02）
- 代码基线：main @ 67a4164
- 状态：**Build-verified**（双库 Integration 需 Docker/Testcontainers）
- 计划：[2026-07-30-codegeneration-database-schema-import.md](../superpowers/plans/2026-07-30-codegeneration-database-schema-import.md)
- 任务快照：codegeneration-database-schema-import-20260730

## 交付范围

Full.NET.Data.CodeGeneration 提供 provider-neutral 单表元数据导入 API：DatabaseColumnMetadataMapper 封闭类型映射、DatabaseCrudSchemaAssembler 组装并复用 FullNetCrudSchema.CreateProject 校验、DatabaseCrudSchemaImporter 参数化只读查询 SQL Server dbo / MySQL 当前库。本切片不含 CLI、页面模板或迁移。

## 验证矩阵

| 类别 | 测试 | 结果 |
|------|------|------|
| 类型映射 | DatabaseColumnMetadataMapperTests (4) | Unit 4/4 GREEN |
| Schema 组装 | DatabaseCrudSchemaAssemblerTests (7) | Unit 7/7 GREEN |
| 双库只读导入 | DatabaseCrudSchemaImporterIntegrationTests (2) | 需 Docker；本地编译通过 |

## 行为要点

- 表名由 OwnerKey + ModuleKey + EntityKey 计算，不接受任意 SQL 标识符。
- 主键必须精确为单列 Id；未知或歧义数据库类型失败关闭。
- 连接与查询严格只读；异常不暴露连接串或凭据。

## 未交付

- CLI 暴露（见 [database-import-cli](codegeneration-database-import-cli-2026-07-30.md)）、整库扫描、批量预览/Apply 与页面模板仍属开放项。

## 规则/Skill 复盘

未触发规则或 Skill 升级条件；沿用双库门禁与命名不变量。
