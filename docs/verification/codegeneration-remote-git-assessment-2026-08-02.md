# CodeGeneration 远程 Git 工作区评估建议稿

- 日期：2026-08-02
- 代码基线：`main` @ `e171c44`
- 状态：**建议稿**（未经 Spec 批准，不得进入实施计划或生产代码）
- 上游证据：[Host Apply 验证](codegeneration-host-apply-2026-07-31.md)、[产品 Rollback 验证](codegeneration-product-rollback-2026-08-02.md)、[多实例互斥验证](codegeneration-distributed-workspace-gate-2026-08-02.md)

## 1. 结论

当前 Apply/Rollback 仅支持 **绝对本地** `WorkspaceRoot`（启动期校验禁止 UNC/远程路径）。生产多实例常把生成工作区放在共享 PVC 或每 Pod 独立 clone；下一合理切片是在 **不改变现有 Manifest-last 写盘内核** 的前提下，为 opt-in 部署增加 **受控 Git 同步边界**（fetch/checkout 与 commit/push 分离、与 Apply Gate 串行），而非把客户端路径或任意 URL 写入 HTTP 契约。

总体能力在 Spec 批准与双端 E2E 前仍保持 `Build-verified`。

## 2. 现状

| 项 | 边界 |
| --- | --- |
| 写盘 | `GenerationWorkspaceStore` / Rollback 检查点均在本地目录 |
| 配置 | `CodeGeneration:Apply:Enabled` 默认 false；`WorkspaceRoot` 必须本地存在 |
| 互斥 | 单进程 Gate + 可选 DB 会话锁 |
| 排除历史 | Host Apply Plan 明确不 clone/pull/push |

## 3. 建议首切片范围

### 纳入

1. **配置**（示例节 `CodeGeneration:Git`，默认全关）：`Enabled`、`RepositoryUrl`（只读配置源）、`DefaultBranch`、`CloneDepth`、`PushEnabled`、`AuthorName`/`AuthorEmail`（机器身份）；凭据走现有 Secret/环境变量约定，不得进仓库。
2. **生命周期**：Worker 或 Apply 前钩子 **幂等** `git fetch` + 硬重置到跟踪分支（或 fast-forward pull）；Apply/Rollback **成功后**可选 `git commit --trailer "Co-authored-by: Cursor <cursoragent@cursor.com>"` + `git push`（仅当 `PushEnabled` 且工作区干净地反映 Manifest 变更）。
3. **与 Gate**：Git 写操作（commit/push）必须在 Apply/Rollback Gate 内；fetch 可在 Gate 外但须 Spec 定义 happens-before。
4. **失败关闭**：冲突、detached HEAD、脏树（Manifest 外未跟踪文件策略需明确）、push 拒绝均映射稳定错误码；不得部分 push。
5. **测试**：Unit 用进程内 Git 替身；Integration 用临时 bare remote；不默认启用生产配置。

### 明确排除

- 客户端指定分支/路径/远程 URL
- PR/MR 创建、代码评审、签名提交
- 生产默认 `Apply:Enabled` 或 `Git:Enabled`
- 链式多 Apply 回滚、Rollback 后删检查点

## 4. 未决问题

1. Git 操作宿主：仅 API 还是 Worker 专职同步？
2. 多实例：共享 bare repo + 工作树，还是每实例 clone + push 串行？
3. Rollback 成功后是否自动 revert commit 还是新提交恢复 Manifest？

## 5. 规则/Skill

未触发规则或 Skill 升级条件；本文件仅为评估建议稿。