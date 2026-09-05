# Workflow 包容网关 `gateway.inclusive` 验证记录

> 日期：2026-09-05  
> 状态：`Build-verified`；双库 Integration 与 OpenAPI 客户端快照以目标提交 GitHub Actions 为最终门禁。  
> 任务基线：`0879a30335ad90a220f6782e94e7447cf53fb5f4`

## 1. 交付边界

本切片为 Workflow 增加包容网关 `gateway.inclusive`，支持条件多分支激活与动态汇合：

- **Fork**：`gatewayRoleKey: fork`，`joinNodeKey`、`branches[]`（闭合单字段条件）与 `defaultNextNodeKey`
- **Join**：`gatewayRoleKey: join`，`forkNodeKey` 与 `nextNodeKeys`
- 运行时：求值全部成立分支；无命中时回落默认分支；汇合等待全部已激活分支到达
- 复用 `fn_workflow_parallel_join` 汇合状态表，迁移 116 增加 `GatewayTypeKey` 并允许 `RequiredBranchCount >= 1`
- 实例 `GetAsync` 返回 `gatewayJoins[]` 展示汇合进度与已到达分支
- Vue：Workflow-Vue3 `type:13` 编译/回显、设计器入口、实例详情汇合卡片与轨迹标签

## 2. 本地新鲜证据

| 验证 | 结果 |
| --- | --- |
| Workflow 聚焦 Unit | 254 项通过，0 失败 |
| `dotnet build` Workflow 模块 | 通过 |
| Workflow-Vue3 适配器 Vitest | 12 项通过，0 失败 |

## 3. 保留边界

- OpenAPI 正式客户端生成待快照脚本恢复后同步 `gatewayJoins` 字段；当前 Admin 通过响应扩展类型读取。
- 页面真实栈 E2E、双库 Integration、Linux Native AOT 与人工验收未在本切片执行。

规则演进结论：未命中规则升级候选。Skill 演进结论：沿用 `fullnet-module-delivery`，无新 Skill 缺口。
