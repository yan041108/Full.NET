# Workflow 排他网关首切片验证记录

> 日期：2026-09-05
> 状态：`Build-verified`；SQL Server/MySQL、Linux Native AOT 和真实客户端执行以目标提交的 GitHub Actions 结果为最终门禁。
> 任务基线：`65793cc18432dc33609f7b5dd93f44b69dc18375`

## 1. 交付边界

本切片把 `gateway.exclusive` 从仅可设计节点升级为可发布、可执行节点，但没有引入通用表达式引擎。每个网关只允许 1–15 个有序单字段条件分支和一个唯一默认分支，按声明顺序选择首个命中项。允许的操作符固定为相等、不等、大小比较和空值判断；脚本、远程调用、SQL、URL、回调与未知配置全部失败关闭。

发布编译必须将条件绑定到工作流定义版本关联的不可变 `WorkflowFormSchema`，并验证字段存在、操作符兼容及常量线格式。运行时只读取已经通过同一表单协议校验的实例提交；审批后的网关读取本次 Field Patch 合并并校验后的完整值，驳回不会求值或执行任何下游网关和抄送。

## 2. 运行时与持久化

- `WorkflowRuntimePlan` 支持由 `start` 或当前 `human.approval` 沿抄送、排他网关继续到下一审批或终点，并要求每条终止路径至少包含一个人工审批。
- 自动节点保持真实路径顺序。网关复用 `fn_workflow_step` 写入 `gateway.exclusive/completed` 步骤，并向 `fn_workflow_execution_log` 写入 `node.gateway.exclusive` 与 `branch:<branchKey>`；不新增表或迁移。
- 网关、抄送、下一待办和实例修订仍位于 Workflow 本地事务，不读取或写入其他模块表。
- Workflow-Vue3 type `10` 与权威 IR 双向转换；Vue 抽屉只从选定的已发布表单版本提供字段，并按字段类型限制操作符和值。

## 3. 本地新鲜证据

| 验证 | 结果 |
| --- | --- |
| 排他网关配置、编译、运行计划和自动写入 TDD 选择集 | 46 项通过 |
| Workflow Unit 选择集 | 153 项通过 |
| Workflow/Comment/AOT/Dapper Architecture 选择集 | 82 项通过 |
| 错误资源完整性 | 5 项通过 |
| Workflow-Vue3 适配、设计器与定义页 Vitest | 19 项通过 |
| `pnpm --dir ui/admin typecheck` | 通过 |
| `pnpm --dir ui/admin build` | 通过 |
| Integration 项目 `--no-restore` 构建 | 通过，0 警告、0 错误 |
| 受影响 Integration 计划 | 精确选择 Workflow SQL Server/MySQL slice；本地未用容器冒充门禁 |
| `ui/admin-layui` 影响检查 | 零修改 |

共享双库 Integration 场景已覆盖审批 Patch 命中条件分支、默认分支、网关轨迹，以及驳回不产生网关轨迹。环境重型执行遵循 GitHub Actions 优先规则。

## 4. 保留边界

以下能力仍开放，因此 Workflow 不得升格为 `Verified`：

- Worker 恢复、租约、重放与 reconcile；
- Tenant 本地审批人/抄送人候选目录；
- Workflow 到 Notifications 的异步业务提醒投影；
- 更复杂的组合条件、并行/包容网关、会签与加签；
- 人工产品验收、生产等价容量与故障演练。

规则复盘结论：本轮未出现新的高风险类别、重复失败或规则冲突，不新增规则候选。
