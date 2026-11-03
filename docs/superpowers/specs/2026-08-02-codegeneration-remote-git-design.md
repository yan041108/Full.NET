# CodeGeneration Git 工作区与独立发布状态

**状态：** 整改实施中；实际验证范围见[执行计划](../plans/2026-10-24-codegeneration-remediation.md)
**日期：** 2026-08-02；2026-10-26 按已授权整改直接替换不安全的首切片策略
**实施基线：** `main @ a77902afd87af1662af3b43206d85160eeec566b`

## 1. 决策摘要

`WorkspaceRoot` 必须是事先克隆且已检出的专用持久工作分支。`CodeGeneration:Git` 与 Apply 均显式开启时，先核对本地工作树、暂存区、上一操作提交和远端基线，再 fetch。不得 reset、强推或以 `add -A` 收入计划外文件；默认分支只作为第一次建立工作分支的来源。

整个规划、文件、运行终态与发布由共享卷文件租约串行化；工作区 GUID 保存在共享卷中，数据库会话锁仅作附加协调。旧写入者仍持有文件租约时，即使数据库连接丢失也不授权第二个写入者。文件操作日志与 Git 收据分别保存，恢复时使用冻结计划和原始 Git 基线，不重新生成产物。

每次提交只允许完整计划中的文件与根 Manifest。提交前后均核对文件摘要；Git 对象摘要单独计算，以正确处理仓库换行归一化。先耐久保存本地 commit SHA，再尝试 push，并核对远端 SHA。push 被拒绝时保留本地提交；响应丢失后只重试同一提交，不重复 Apply、commit 或改写历史。

当前 CLI 的文件操作与 Host 共用工作区顺序；CLI 不自动使用 Host Git 凭据或发布。跨入口操作导致 Git 链偏离时停止并要求受控对账，不能重置工作区消除证据。自动 clone、PR 和生产部署均不在此功能范围。

## 2. 配置 `CodeGeneration:Git`

| 键 | 默认 | 说明 |
| --- | --- | --- |
| `Enabled` | `false` | 与 `Apply:Enabled` 同时为 true 才激活 |
| `PushEnabled` | `false` | 成功后 push |
| `DefaultBranch` | `main` | 首次初始化的来源分支 |
| `WorkingBranch` | 空 | 必填；预先创建并检出的专用分支，须不同于 DefaultBranch |
| `RemoteName` | `origin` | 远程名 |
| `AuthorName` / `AuthorEmail` | 空 | Git 启用即必填，关闭 push 仍会创建本地提交 |
| `CredentialEnvironmentVariable` | `FULLNET_CODEGENERATION_GIT_TOKEN` | Bearer 注入 `http.extraHeader` |

## 3. HTTP 与页面语义

本地文件成功与 Git 发布独立表达。Apply/Rollback 和运行历史附带 `publicationStatus` 与可空 `commitSha`：`disabled`、`local_only`、`pending`、`published`、`unavailable`。只有核实远端包含对应提交后才返回 `published`；缺失或损坏的历史收据返回 `unavailable`，不根据当前配置猜测过去状态。

`POST /api/v1/code-generation/runs/{runId}/publication/retry` 使用精确 Apply 权限，只接受本地已经成功的 Apply/Rollback。页面展示独立状态，待发布操作使用重试发布入口，不再提交第二次 Apply。已完成的历史发布状态保存在每个运行的独立收据中。

## 4. 错误与恢复

- `codegen.run.git_sync_failed` — 写盘前同步失败
- `codegen.run.git_publish_failed` — 发布诊断码；本地成功仍保留，响应通过 `pending` 明确发布未完成
- `codegen.run.invalid_publication_run` — 所选运行不是本地成功的 Apply/Rollback

有待发布操作时，下一次写盘先恢复该提交；未成功处理则拒绝新写入。源文件、暂存区、分支、基线或远端出现非预期漂移时保留恢复材料，停止发布，不覆盖人工变更。

`.fullnet` 中的提交、操作顺序与恢复材料属于本地控制面，不得由生成模板改写，也不包含访问令牌。凭据只从环境变量注入受控 Git 子进程，不写配置库、运行摘要或日志。Helm 不按 Production 隐式开启 Apply；正式运行时镜像缺少 SDK/Git 时，通过受控工具环境执行并保留实际验证范围。
