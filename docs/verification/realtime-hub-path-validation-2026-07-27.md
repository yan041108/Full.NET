# Realtime HubPath 启动校验验证记录（2026-07-27）

- 范围：`Realtime:HubPath` 启动配置校验、聚焦 Unit 回归。
- 状态：**Build-verified**
- 基线：`main@aff0216648e463bfca940c0deebe11e8d6eb5869`

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

## 完成门槛

- 同步既定队列完成后的最新 `main`。
- 复跑 Release 构建、Unit、Compatibility、Architecture、Integration 分片发现、
  Governance、Skill、workspace 与 `git diff --check`。
- 按最新测试事实更新 canonical 门槛后合并到 `main`，删除功能分支和工作树。
