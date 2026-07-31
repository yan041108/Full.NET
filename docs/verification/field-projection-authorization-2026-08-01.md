# 字段投影授权验证记录（2026-08-01）

## 结论

Admin.NET 的角色字段配置已按 Full.NET 安全边界完成改造。公共授权使用 `identity.host_users` 与稳定语义 `FieldKey`，不暴露物理表列；角色并集受代码目录限制，Host 超级管理员不跨作用域，普通角色没有 grant 时只能读取原有七个兼容字段。

Host Users 列表、详情与导出先调用同一解析器，再执行基础 SQL 和获准字段各自的固定补充 SQL。未授权的 `PreferredLocale`、`FailedLoginCount`、`LockoutEndUtc` 不会被 SQL 读取。密码哈希、安全戳和规范化用户名不属于目录。

角色 grant 替换与角色版本递增处于同一命令事务。第一阶段不缓存解析结果，因此提交后的下一请求必然重新读取角色授权；后续缓存不得把删除作为唯一撤销保证。

## 数据库与 API

- SQL Server/MySQL：`041_IdentityRoleFieldGrant.sql`
- 表：`fn_identity_role_field_grant`
- 唯一约束：`(RoleId, ResourceKey, FieldKey)`
- MySQL 为 `RoleId` 外键维护独立支撑索引，使复合唯一索引可独立恢复。
- API：字段目录、角色 grant 读取/替换、Host Users 投影导出。
- 精确权限：`identity.role_field_grants.read`、`identity.role_field_grants.write`、`identity.users.export`。

## 新鲜验证

- 字段投影 Unit 聚焦：12/12。
- 安全复审：初审无 Critical、4 个 Important；已分别关闭未知/退役字段回显、MySQL 041 四列伪唯一索引恢复、OpenAPI 冻结契约漂移和客户端投影对象失败开放，复验无遗留 Critical/Important。
- Identity Unit 聚焦：133/133（增加最终两条投影一致性测试前的聚焦结果）。
- affected 工具链：39/39。
- governance：16/16。
- Integration Release build：0 warning / 0 error。
- Integration 分片发现：SQL Server API 44、MySQL API 44、migrations 88、infrastructure 83，合计 259，无重复或遗漏。
- Identity + migration 041 affected：29/29，SQL Server/MySQL 双 Provider。
- client-contracts：104/104。
- Vue 管理端：256/256，typecheck 通过。
- Layui 管理端：125/125。
- Architecture：50/50。
- SQL naming：24/24。

全 Unit 的最终新鲜计数与独立安全审查结论在本切片最终冻结时补入矩阵唯一来源和交付回执，不在本记录复制长期门槛。

## 规则与 Skill 复盘

本切片没有出现新的重复失败类别或规则冲突；MySQL 外键支撑索引问题已由 041 专属恢复测试覆盖，不升级全局规则。现有 `fullnet-module-delivery` 已覆盖双库迁移、API、双端与 affected 流程，未发现需要修改项目 Skill 的缺口。
