# 应用升级

Full.NET 生成应用升级遵循 **Migrator → Worker → Api** 顺序，与 `eng/deploy/Invoke-FullNetRelease.ps1` 一致。

## 升级前

1. 备份数据库与 `framework-manifest.json` 摘要
2. 在预发环境执行 `diagnose --profile production`
3. 核对新版本迁移编号闭包与模块预设

## 升级步骤

0. **预览受管框架升级**（Full.NET 源码树）：`node scripts/templates/upgrade-framework.mjs --app <AppRoot> --package <NewTemplatePackage> --dry-run`（`--apply` 提交受管更新；冲突报告不会覆盖 `src/` 业务文件）
1. **Migrator**：应用 SQL Server/MySQL 成对迁移
2. **Worker**：滚动重启，等待 Outbox/Jobs 消费追平
3. **Api**：滚动重启，验证健康检查与关键读取路径

## Expand/Contract

- 仅追加列/表时使用 Expand；删除或重命名列前必须 Contract 旧读路径
- 跨版本框架源码升级时合并 `framework/fullnet/`，保留 `src/` 业务所有权

## 回退

- 数据库：使用备份恢复或执行已验证的 down 脚本（若提供）
- 应用：回滚到上一版本镜像/源码包，保持迁移版本一致

## 验证

- `tests/deployment/foundation-preset-release.test.mjs`
- 发布清单：`docs/verification/f16-release-checklist.md`
