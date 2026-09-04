# Workflow `notify.cc` 首切片验证（2026-09-04）

## 范围与结论

- 实现提交：`2fcac226aa758cefcb8fdb4d1590de8fcbbd7ec8`
- 任务快照：`workflow-notify-cc-20260904`
- 结论：`notify.cc` 已进入 Workflow 闭合编译器和线性运行时，可在启动、批准迁移中原子写入完成步骤、实例级去重抄送和执行日志；管理端已交付稳定用户标识选择器与“我的抄送”只读/已读页面。
- 状态：Workflow 保持 `Build-verified`，不提升为 `Verified`。`gateway.exclusive`、Worker 恢复/reconcile、Notifications 异步提醒投影、Tenant 本地收件人候选目录、人工产品验收和生产容量仍开放。

当前定义发布只通过既有 `IHostUserDirectory` / `IHostUserSelectionDirectory` 校验和选择活动 Host Identity 用户。抄送数据读取始终同时约束可信当前用户和 `TenantScopeKey`；本切片没有隐式引入 Workflow 对 Identity/Organization 表的跨模块读取，也没有把 Host 候选目录外推为 Tenant 本地成员目录。

## 已验证行为

- `recipientUserIds` 只接受 1–20 个唯一、非空、规范 `D` 格式 GUID；未知配置字段、脚本、URL、回调和任意载荷均失败关闭。
- `start -> (notify.cc | human.approval)+ -> end` 保持闭合线性拓扑且至少包含一个审批节点；抄送可位于首审批前、审批间或末审批后。
- 每个到达的抄送节点产生完成步骤和 `node.notify.cc` 执行日志；同一实例/收件人只产生一条 `fn_workflow_cc`，驳回不执行下游抄送。
- 抄送写入与实例启动/待办批准处于同一 Workflow 本地事务；不在事务内调用 Notifications 或外部渠道。
- “我的抄送”只返回当前用户在可信作用域内的记录；首次已读受用户和作用域双重条件保护，重放返回同一首次已读时间，跨用户访问隐藏为 404。
- 抄送人是实例只读参与者，但不会获得待办或审批动作。

## 本地新鲜证据

| 验证 | 结果 |
| --- | --- |
| `dotnet build Full.NET.slnx --no-restore` | 通过，0 警告、0 错误 |
| Workflow/Localization 定向 Unit | 177/177 通过 |
| Workflow/Comment/AOT/Dapper Architecture | 82/82 通过 |
| Workflow Integration 项目构建 | 通过，0 警告、0 错误 |
| 管理端定向 Vitest | 5 个文件、15/15 通过 |
| `pnpm --filter @fullnet/admin typecheck` / `build` | 通过；`WorkflowCcView` 独立懒加载块 3.19 kB（gzip 1.44 kB） |
| `pnpm test:bundle-budgets` | 3/3 通过 |
| `pnpm test:openapi` | 123/123 通过 |
| `pnpm openapi:client:snapshot --check --offline` | 通过 |
| `git diff --check` | 通过 |

## GitHub Actions 新鲜证据

| 工作流 | 结果 |
| --- | --- |
| [`api-native-aot-linux` run 33889720388](https://github.com/yan041108/Full.NET/actions/runs/33889720388) | **success**；Linux Native AOT publish、架构门禁和外部进程 E2E 通过 |
| [`worker-native-aot-linux` run 33889720263](https://github.com/yan041108/Full.NET/actions/runs/33889720263) | **success**；Worker Linux Native AOT 与 SQL Server/MySQL E2E 通过 |
| [`ci` run 33889720511](https://github.com/yan041108/Full.NET/actions/runs/33889720511) | build-test、client-build-test、api-mysql、migrations、infrastructure、messaging-heavy 通过；api-sqlserver 的 Workflow 场景通过，但同分片既有 Identity 并发资料更新用例出现 200+500（期望 200+409），导致分片 65/66。SQL Server/MySQL 宽泛真实栈各有 95 个用例通过，同时分别有 23/21 个横跨日志、API Key、代码生成、字典、机构、租户与通知等既有场景失败；MySQL Workflow 租户场景与多个无关用例共同卡在进入租户后的“主导航”超时。整条 run 如实保持 failure，不把宽泛真实栈失败归入本切片完成证据。 |

对 `api-sqlserver` 的定向重跑（attempt 2，job `101088511661`）仍只失败于同一个 `Host_user_management_follows_contract_with_sql_server`，但落点变为 Identity 权威资料唯一性断言未得到预期 409。该重复失败作为独立 Identity CI 债务保留；不能据此把整条 CI 标为成功，也没有证据表明 `notify.cc` 引入 Workflow 回归。

## 状态边界

- 本切片没有新增数据库迁移；沿用 migration 102 已存在的步骤、抄送、外键和实例/收件人唯一约束。
- 本切片没有新增第三方依赖或许可证变化；Workflow-Vue3 仍只作为受控设计器来源。
- SQL Server 与 MySQL 的共享 Workflow API 场景均已由 CI 执行；不得因同一 SQL Server 分片的 Identity 并发失败否定或夸大 Workflow 结果。
- 容量仍为 `Capacity-not-verified`。

## 规则与 Skill 演进

本任务未触发新规则或 Skill 缺口。现有 `fullnet-module-delivery`、中文注释、双库、OpenAPI、GitHub Actions 优先和 Native AOT 规则已覆盖本切片。
