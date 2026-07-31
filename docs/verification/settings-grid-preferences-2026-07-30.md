# Settings 用户 Grid/Column 偏好验证记录

- 日期：2026-07-30
- 状态：**Build-verified**
- 计划：[`2026-07-30-adminnet-design-absorption-program.md` Task 3](../superpowers/plans/2026-07-30-adminnet-design-absorption-program.md#task-3-交付用户-gridcolumn-偏好)
- 参考：Admin.NET.Pro `SysColumnCustom`、`SysColumnCustomService` 与 Vue VXE Grid 列偏好 Hook 的产品语义

## 交付范围

本切片吸收了 Admin.NET.Pro 按“用户 + Grid”保存列固定、宽度、顺序和可见性的能力，并按 Full.NET 边界重新实现：

| 层级 | Full.NET 落点 |
|---|---|
| 可信目录 | 服务端与客户端只发布稳定 `GridKey` / `ColumnKey`；首个目录为 `identity.users`，偏好只影响展示，不能扩大数据权限 |
| API | 已认证当前用户 `GET` / `PUT` / `DELETE /api/v1/me/grid-preferences/{gridKey}` |
| 并发 | `Version` 乐观并发；旧版本写入返回稳定冲突错误 |
| Schema 演进 | `SchemaVersion` 不匹配或持久化 JSON 无效时回退本地默认；客户端同样 fail-safe |
| 持久化 | 双库迁移 `038_SettingsGridPreference.sql`；全局用户标识 + Grid 唯一，SQL 全部显式绑定 `UserId + GridKey` |
| 缓存 | FusionCache/HybridCache 按环境、用户、Grid、SchemaVersion 分区；事务提交成功后失效 |
| 契约 | Settings.Contracts C# 契约、OpenAPI 冻结夹具、`@fullnet/client-contracts` 运行时守卫 |
| 双管理端 | Vue 与 Layui 都提供远端 GET/PUT/DELETE 客户端和列顺序、宽度、可见性、固定状态适配器 |

## 与 Admin.NET.Pro 的取舍

- 保留产品能力，不复制其源码、表结构、SqlSugar 缓存或前端 Hook。
- 不采用 `ITenantIdFilter`、`IDeletedFilter`、`IOrgIdFilter`、`EntityBaseId`、`EntityBase`、`EntityBaseData` 等运行时万能实体继承；用户偏好是独立的当前用户用例和显式 SQL。
- Admin.NET.Pro 将固定、宽度、排序、显示分别保存为 JSON；Full.NET 在可信目录验证后保存单一规范 JSON，避免部分状态漂移。
- Admin.NET.Pro 以长时缓存承载读取；Full.NET 仍以数据库为事实源，缓存只做加速，并在提交后按精确键失效。
- 当前用户 ID 来自已验证令牌 `sub`，请求体不能指定其他用户。

## 新鲜验证

| 门禁 | 结果 |
|---|---|
| Settings Unit `GridPreferenceTests` | **6/6** |
| SQL Server/MySQL 当前用户 API | **2/2** |
| SQL Server/MySQL 038 半完成恢复 | **2/2** |
| OpenAPI 静态合同 | **1/1** |
| Architecture 全局 SQL 目录 | **2/2** |
| `@fullnet/client-contracts` 聚焦 | **3/3** |
| Vue 偏好适配器聚焦 | **3/3** |
| Layui 偏好适配器聚焦 | **3/3** |
| Integration Release build | **0 警告 / 0 错误** |
| Architecture Release build | **0 警告 / 0 错误** |
| Unit Release build | **0 警告 / 0 错误** |
| Fresh discovery | **Unit 792；Integration 235（40/40/72/83）** |
| Full Unit | **792/792** |
| Full Architecture | **49/49** |
| Release solution build | **0 警告 / 0 错误** |

038 恢复测试先稳定复现了“表存在、唯一索引缺失时迁移重跑不修复”的双库失败，再将表创建与索引修复拆为两个幂等阶段；独立审查后继续覆盖 SQL Server 过滤索引与 MySQL 前缀索引，确保同名但物理形状错误的索引也会被收敛。最终 SQL Server/MySQL 均通过。

独立代码审查还验证并推动关闭了并发首写唯一冲突、请求/持久化 `null` 列元素、事务提交后缓存失效取消、客户端 schema 漂移降级、本地目录绕过及 OpenAPI 401/错误状态等边界；复核结论为无剩余 Critical、Important 或 Minor。

任务快照 `adminnet-absorb-03-grid-preferences` 的最终 affected slice 命中共享 Data abstraction 后，先通过 smoke **8/8**，再通过 Files + `migration-038` + Realtime + Settings 组合 **21/21**；Integration Release build 为 **0 警告 / 0 错误**，Docker teardown 后 running/residual 均为 **0**。

## 已知边界

- 首批只发布 `identity.users` 目录；新增 Grid 必须同时更新服务端目录、客户端目录和相应契约测试。
- 当前交付的是可复用的双端偏好客户端与列适配器；现有用户管理页仍是卡片布局，不伪装为已完成可视化列编辑器。具体表格页接入、拖拽/勾选交互与真实浏览器 E2E 必须随该 Grid 消费者另行验收。
- 用户偏好跟随全局唯一用户，不按当前租户复制；偏好不能绕过租户、组织、字段投影或 Endpoint 权限。
- 状态保持 `Build-verified`，完整 `main` CI 与真实 Grid 消费者浏览器链路通过前不得标记为 `Verified`。

## 规则与 Skill 演进

本切片没有发现新的重复失败、高风险类别、规则冲突或项目 Skill 缺口，不更新规则或 Skill 候选。
