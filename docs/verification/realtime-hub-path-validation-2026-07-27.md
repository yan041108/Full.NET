# Realtime HubPath 启动校验验证记录（2026-07-27）

- 范围：`Realtime:HubPath` 启动配置校验、聚焦 Unit 回归。
- 状态：**Build-verified**
- 最终同步基线：`main@b837747e0d2a301747947a229468725690562826`

## 行为合同

1. 启用 Realtime 时，HubPath 必须是以 `/` 开头的绝对应用路径。
2. HubPath 不得包含空白、查询字符串或片段；无效配置在服务注册期间以
   `OptionsValidationException` 快速失败，避免应用启动后才暴露路由错误。
3. 合法的自定义 HubPath 保持原值，不改写既有默认值
   `/hubs/notifications`。
4. `Realtime:Enabled=false` 时不校验 HubPath，使应急关闭 Realtime 的配置
   仍可启动；此时不会映射 Hub。

## RED / GREEN 证据

| 阶段 | 新鲜结果 |
| --- | --- |
| RED | `Invalid_hub_paths_fail_during_realtime_registration` 因未抛出异常失败 **1/1** |
| GREEN | `RealtimeBackplaneRegistrationTests` **3/3**，失败 0，跳过 0 |
| 构建 | `Full.NET.UnitTests.csproj -c Release --no-restore` **0 warning / 0 error** |

覆盖的非法值包括空值、相对路径、查询字符串、片段和路径内空白。该切片不修改
数据库、SQL、Integration 用例或 Docker 编排；Redis 可达性和跨节点投递继续由
既有 Realtime Integration 与故障恢复验证负责。

## 最终验证

| 门槛 | 新鲜结果 |
| --- | --- |
| `Full.NET.slnx` Release | **0 warning / 0 error** |
| Unit / Compatibility / Architecture | **407/407** / **7/7** / **49/49**，失败 0、跳过 0 |
| Realtime 注册聚焦 | `RealtimeBackplaneRegistrationTests` **3/3** |
| Realtime SQL Server/MySQL | **2/2**，失败 0、跳过 0，**1m36s** |
| Integration 分片发现 | API SQL Server **35** + API MySQL **35** + Migrations **62** + Infrastructure **57** = **189** |
| Governance / Project Skill / Workspace | **11/11** / **52** 项 / 通过 |
| `git diff --check` | 通过 |

前序 Logging 在包含全部服务端前置变更的最终树上完成完整 Integration
**189/189**（失败 0、跳过 0，**31m23s**，stderr 0）。其后合入的
BroadcastChannel 与 admin-real-stack locator 契约仅涉及 TypeScript、Node
静态测试和文档；本切片再用最终 Realtime 服务端实现完成双库 **2/2** 聚焦。
测试退出后 Docker 容器为 0。

最终 canonical 为 **407/7/49/189**。四处门槛、审计记录与能力状态已同步。

## 规则与 Skills 复盘

本次是单一 Realtime 配置边界遗漏，已由启动期回归测试稳定阻断；没有形成跨模块
重复模式或新的高风险架构决策，因此不新增或修改强制规则。现有项目 Skill 能完整
覆盖同步、验证与合并流程，本次无 Skill 变化。
