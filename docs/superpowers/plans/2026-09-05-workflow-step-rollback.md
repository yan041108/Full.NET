# 工作流审批退回实施计划

**目标：** 当前待办处理人可以把运行中的实例退回到同一实例当前有效执行链上、已经完成的指定人工审批步骤；后端状态、修订号、轨迹、审计、通知与 Vue 待办页保持一致。

**基线：** `codex/workflow-timeout-escalation@cd533940`；实施分支 `codex/workflow-step-rollback`。

**语义边界：** 本轮只实现 `selected_completed_human_step` 策略。目标必须是当前可信作用域、当前实例、当前有效执行链中状态为 `completed` 的 `human.approval` 步骤。自动节点、其他实例步骤、当前步骤、已回退的旧分支步骤均失败关闭。回退不重新执行目标之前或目标之后的自动节点，直接创建目标审批节点的新步骤与新待办；新待办沿用目标历史步骤的办理人快照，后续角色/组织动态解析由独立任务实现。

## 1. RED：固化公共契约和权限边界

- 扩展 Workflow 单元测试，先证明合法目标响应、退回请求、独立 `workflow.todos.return` 权限和端点尚不存在。
- 增加 `GET /api/v1/workflow/todos/{todoId}/return-targets` 与 `POST /api/v1/workflow/todos/{todoId}/return`。
- 请求包含目标步骤、待办期望修订号、字段补丁、退回原因和幂等键；退回原因必填且最大 512 字符。
- 补齐 ProblemDetails 错误码、资源、授权目录和 Native AOT JSON 元数据。

## 2. RED：合法目标与原子回退

- 新增服务测试，覆盖当前办理人查询、自动节点/其他实例/已回退步骤不可选、旧修订冲突、幂等重放和成功回退。
- 使用 SQL Server/MySQL 显式有界查询列出合法目标；提交时按目标标识、实例标识、人工节点类型和 `completed` 状态再次校验，禁止信任客户端列表。
- 在单一本地事务中更新表单提交，完成当前待办并把当前步骤标记为 `returned`，把目标及其之后仍为 `completed` 的旧执行链步骤标记为 `rolled_back`，推进实例修订号，再创建目标节点的新步骤和待办。
- 新待办从不可变发布版本读取目标节点超时策略并重新计算 SLA；目标办理人使用历史步骤的办理人快照。

## 3. 轨迹、审计、通知与静态闭包

- 写入 `return` 动作记录、`todo.return` 执行轨迹和包含源/目标步骤标识的 B0 领域审计详情。
- 原子发布一条 `TodoAssigned` Outbox 事件给目标办理人；幂等重放不得重复写入。
- 补齐 Dapper AOT 物化器、全局 SQL 清单、JSON 上下文与契约生成输入。

## 4. Vue 待办页

- 先扩展 API 与页面测试，证明只在 `workflow.todos.return` 权限下展示退回入口、打开时加载合法目标、未选目标或未填写原因时禁止提交、409 后关闭过期动作并刷新。
- 在待办抽屉增加合法目标选择和退回原因提交；继续复用字段补丁、幂等键和 ProblemDetails 处理。
- 补齐中英文 i18n，并从 OpenAPI 重新生成 TypeScript 客户端，禁止手改生成产物造成漂移。

## 5. 验证与提交

- 运行 Workflow 聚焦单元测试、授权/SQL/AOT/命名治理、Vue API 与页面 Vitest、admin typecheck、Release 构建和 `git diff --check`。
- 使用 `workflow-step-rollback-20260905` 快照审查 affected integration plan；双库 Integration、Linux Native AOT、页面真实栈 E2E、视觉调整和人工逐页验收留给 GitHub Actions 或最终页面收敛阶段。
- 更新验证记录和测试矩阵；仅在 `codex/workflow-step-rollback` 创建一个独立提交，停止，不合并、不删除分支、不提前执行编号 5。
