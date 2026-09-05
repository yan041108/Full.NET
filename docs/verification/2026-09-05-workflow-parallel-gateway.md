# Workflow 并行网关 `gateway.parallel` 验证记录

> 日期：2026-09-05  
> 状态：`Build-verified`；双库 Integration 与 OpenAPI 客户端快照以目标提交 GitHub Actions 为最终门禁。  
> 任务基线：`438ed9a67fe3b48d43ec5f3d3b415cdeca4d114d`

## 1. 交付边界

本切片为 Workflow 增加并行网关 `gateway.parallel`，支持 fork/join 成对节点：

- **Fork**：`gatewayRoleKey: fork`，配置 `joinNodeKey` 与 2–8 条 `branches[]`
- **Join**：`gatewayRoleKey: join`，配置 `forkNodeKey` 与 `nextNodeKeys`
- 运行时：分叉激活各分支审批，汇合等待全部分支到达后继续推进
- 驳回/取消：关闭全部活动待办与步骤，并取消等待中的汇合状态
- Vue 设计器：Workflow-Vue3 适配器编译/回显 `type: 12` 并行分支；实例轨迹展示 fork/join 标签

## 2. 本地新鲜证据

| 验证 | 结果 |
| --- | --- |
| Workflow 聚焦 Unit | 251 项通过，0 失败 |
| `dotnet build` Workflow 模块 | 通过 |
| Workflow-Vue3 适配器 Vitest（含并行网关往返） | 12 项通过，0 失败 |

## 3. 保留边界

- 实例详情 API 未返回 `parallelJoins` 列表；当前仅通过执行轨迹展示 fork/join。
- OpenAPI 正式客户端生成待快照脚本恢复后同步；本切片无新增公共 HTTP 契约字段。
- 页面真实栈 E2E、双库 Integration、Linux Native AOT 与人工验收未在本切片执行。

规则演进结论：未命中规则升级候选。Skill 演进结论：沿用 `fullnet-module-delivery`，无新 Skill 缺口。
