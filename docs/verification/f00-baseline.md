# F00 底座完善基线核对（2026-09-17）

> 任务 ID：`foundation-f00`
> 核对提交：`a6d3d02eb38e6384a123e39f89dfc7bbe8e90699`
> 总计划：[2026-09-16-foundation-productization.md](../superpowers/plans/2026-09-16-foundation-productization.md)

## 资产核对摘要

| 能力 | 任务 | 状态 |
| --- | --- | --- |
| B01 建项目工具链 | F01–F02、F15 | Build-verified（模板测试 + diagnose CLI） |
| B02 企业成员 | F05–F06 | Implemented（API + Vue，集成待 Docker） |
| B03 权益配额 | F07–F08 | Implemented（单元测试通过） |
| B04 注册恢复 | F03–F04 | Implemented（公开 Vue + API） |
| B05 订阅运营 | F12 | Verified（SaaS 子集；Host 订阅 MVP + 占位 Port；真实支付渠道未验） |
| B06 Webhook | F13–F14 | Implemented（Webhooks 模块注册） |
| B07 业务样板 | F09–F11 | Partial（Enterprise 预设 + CRUD/工作流/导入子集；见 enterprise-business-integration-closeout） |
| B08 发布恢复 | F15–F16 | Implemented（运维文档 + 发布清单测试） |

## C01–C07 门禁

| 编号 | 阻塞 | 说明 |
| --- | --- | --- |
| C01 | F04/F06/F14 | OIDC P0 待 CI 全链路 |
| C02 | F11 | 数据交付 Worker 真实验证 |
| C03 | F10 | Workflow 样板端到端 |
| C04 | F03/F05/F10 | 邮件渠道生产认证 |
| C05 | 全部 | 权限回归随切片补充 |
| C06 | F16 选装 | 不阻塞基础预设 |
| C07 | F15/F16 | 容量仍 Capacity-not-verified |

## 契约登记

- Manifest：`templates/fullnet-app/framework-manifest.schema.json`
- 预设投影：`scripts/templates/preset-modules.mjs`
- 模板：`dotnet new fullnet-app`（`templates/fullnet-app/.template.config/template.json`）

## 复用决策

- 注册政策演进为三态 `RegistrationMode`
- Identity 挑战独立于 Notifications 收件端点验证
- Tenancy 拥有权益与配额；Payments 拥有资金事实
- Webhooks 为官方可选模块 `Webhooks`

## 首版预设

**Minimal**（基础管理）为默认可创建预设；企业/SaaS 预设需相应 F 切片与 C 门禁证据后提升。
