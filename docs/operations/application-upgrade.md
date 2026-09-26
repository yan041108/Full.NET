# 应用升级

Full.NET 生成应用升级遵循 **Migrator → Worker → Api** 顺序，与 `eng/deploy/Invoke-FullNetRelease.ps1` 一致。

## 升级前

1. 备份数据库与 `framework-manifest.json` 摘要
2. 在预发环境执行 `diagnose --profile production`
3. 核对新版本迁移编号闭包与模块预设
4. 停止应用框架目录的写入与运行进程，保留本地定制；升级预览报告冲突时先人工合并，不强行覆盖

## 升级步骤

0. **预览受管框架升级**（Full.NET 源码树）：`node scripts/templates/upgrade-framework.mjs --app <AppRoot> --package <NewTemplatePackage> --dry-run`（`--apply` 提交受管更新；冲突报告不会覆盖 `src/` 业务文件）
1. **Migrator**：应用 SQL Server/MySQL 成对迁移
2. **Worker**：滚动重启，等待 Outbox/Jobs 消费追平
3. **Api**：滚动重启，验证健康检查与关键读取路径

## Expand/Contract

- 仅追加列/表时使用 Expand；删除或重命名列前必须 Contract 旧读路径
- 跨版本框架源码升级时合并 `framework/fullnet/`，保留 `src/` 业务所有权
- 升级器校验完整包摘要、清单副本、路径与链接，并重新投影应用既定模块预设和双库迁移清单；预览后的输入变化会拒绝提交。模块闭包改变或删除旧受管文件须另行设计迁移，自动升级不会执行删除。
- 固定预设执行历史 203 UUID 修复时，Migrator 按清单内的建表来源保留所选表的完整校验与恢复；未选表必须同时不存在于数据库，否则拒绝预设漂移。变换仅匹配两库历史全文摘要，不改写资源或 DbUp 记账名称；无清单的完整底座仍执行原脚本。此适配不支持通过修改清单删除已有模块，也不替代模块新增/退役的数据迁移设计。
- 应用根目录的依赖锁文件、Vue 骨架和宿主属于应用资产，升级器只更新 `framework/fullnet/`；应用资产的兼容合并仍需开发者处理和验证。

## 回退

- 数据库：使用备份恢复或执行已验证的 down 脚本（若提供）
- 应用：回滚到上一版本镜像/源码包，保持迁移版本一致
- 框架源码：升级输出 `recoveryRoot`，其中 `previous/` 与 `previous-manifest.json` 保存原框架及根清单，成功后也不自动清理。普通提交失败会恢复原目录与清单；失败的新内容保留在恢复区。
- 进程中断：应用根目录存在 `.fullnet-upgrade.lock` 时，后续升级失败关闭。停止所有相关进程，按锁内 `recoveryRoot` 核对 `previous/`、`next/`、活动目录及两份清单；保留全部内容后恢复匹配的框架/清单。完成检查后再人工移除锁；不能直接重试或删除恢复区。源码恢复不代替数据库、密钥或对象文件恢复。

## 验证

- `tests/deployment/foundation-preset-release.test.mjs`
- 发布清单：`docs/verification/f16-release-checklist.md`
