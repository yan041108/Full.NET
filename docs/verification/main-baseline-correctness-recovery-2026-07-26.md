# 主干基线正确性恢复验证记录（2026-07-26）

- 状态：**Build-verified**
- 范围：2026-07-26 全面审查确认的认证、授权、租约恢复、事务边界、敏感日志、配置校验、客户端与治理门禁问题
- 实施计划：[主干基线正确性恢复计划](../superpowers/plans/2026-07-26-main-baseline-correctness-recovery.md)

## 已关闭问题

| 区域 | 修复与证据 |
| --- | --- |
| API Key 认证 | 认证主体建立前使用 `Global` Statement，并由 SQL 显式限定 Host 用户；`HostCatalogSqlScopeTests` 锁定范围与行过滤 |
| API Key 授权 | 创建权限集合不得超过操作者当前有效权限和目标用户权限快照；SQL Server/MySQL 回归 **2/2** |
| Jobs 租约 | `Pending` 与租约已过期的 `Running` 执行均可原子领取，恢复后 `AttemptCount` 递增；SQL Server/MySQL 回归 **2/2** |
| 事务外副作用 | 公告、站内信、未读数 SignalR 推送，以及文件物理删除均在数据库提交后执行；外部副作用失败不反转已提交业务结果 |
| 异常审计 | 数据库与查询 API 不再保存或返回原始异常消息、堆栈，只保留异常类型、安全占位消息、路径、TraceId 与脱敏客户端标识；双库回归 **2/2** |
| Files 配置 | `Files:Local:RootPath` 与 `MaxUploadBytes` 使用 `ValidateOnStart`，生产缺失配置时启动即失败 |
| 公共错误码 | 全局限流码修正为符合稳定契约的 `hosting.rate_limit.exceeded`；Architecture **40/40** |
| 限流策略映射 | 端点级策略错误码改由 Options 管道完成配置，认证限流继续返回 `identity.authentication.rate_limited`，全局限流返回 `hosting.rate_limit.exceeded`；聚焦回归 **4/4** |
| 缓存一致性 | 测试宿主在模块注册前注入 Redis 配置；本机提交后失效不受请求取消影响且不替代 Outbox；Worker 等待 Backplane 发布完成、让广播异常冒泡并进入 Outbox 重试后才确认消息；SQL Server/MySQL + Redis 聚焦回归 **6/6** |
| 可扩展授权断言 | 超级管理员权限与内置导航测试从运行时授权目录推导，不再随正规模块扩展产生陈旧硬编码误报 |
| 客户端 | 修复请求体序列化、枚举目录运行时类型守卫、任务页 i18n 类型、通知面板定时器竞态及 E2E 精确选择器 |
| 依赖安全 | `postcss` 通过精确 override 收敛；移除不兼容的 `brace-expansion` 跨主版本 override，客户端审计保留已登记的 Vite 与 `brace-expansion` 精确路径、限期例外 |
| 迁移恢复测试 | `Through008/009/010` Runner 改为明确迁移编号上界，新增后续迁移不再自动跑入旧恢复场景 |
| Host API 契约 | 用户、角色、菜单端点补齐明确成功响应 schema；角色自定义数据范围保持 Host-only，并通过 `tenantId` 显式选择机构校验租户 |
| 审计跨上下文写入 | Access/Operation/Exception insert 使用 `Global`，可信中间件显式写入 `TenantId`；全部审计查询继续保持 `HostOnly`，Unit **2/2** |
| 仪表盘与 Outbox 回归 | 仪表盘租户统计改用当前 `fn_tenancy_tenant`；缓存重试测试按当前租户事件 ID 断言状态，避免把同批历史消息误判成可靠性失败 |
| 运行时数据范围 | 租户查询读取 Host 角色范围时改用显式 Host 行过滤的 `Global` Statement；参数合并支持字典且忽略索引器；按 ID 查询统一租户锚点，SQL Server/MySQL 回归 **2/2** |
| 规则/Skill | `C-20260721-host-catalog-sql-scope` 第二次命中后升级为强制规则；模块交付 Skill 增加配置启动校验与运行时断言 |

## 验证摘要

| 验证 | 结果 |
| --- | --- |
| Release 构建 | 通过，0 warning / 0 error |
| Unit / Compatibility / Architecture / Integration canonical 门槛 | **359/359、7/7、40/40、172/172** |
| API Key、Jobs、Auditing、Notifications、Files 双库聚焦回归 | **12/12** |
| 限流、Identity 登录、健康检查、缓存一致性聚焦回归 | **4/4、2/2、7/7、6/6** |
| 客户端构建 | Vue、Layui、uni-app H5/微信/支付宝全部通过 |
| 客户端依赖审计 | 无未复核 Critical/High |
| Skill 合约 | **52/52** |
| Mock parity E2E | **102** 项发现，**97 passed、5 skipped、0 failed** |

## 规则与 Skill 复盘

- 规则已演进：跨上下文 Host 目录 SQL 的重复失败已固化为 `R-20260726-host-catalog-sql-scope`，并由 `HostCatalogSqlScopeTests` 与双库运行时数据范围用例共同锁定；本轮后续命中属于同一规则，不再创建近义条目。
- Skill 已修改：`fullnet-module-delivery` 的交付地图同步配置启动校验、运行时断言与最新 canonical 门槛，合约 **52/52**；本轮没有出现边界稳定且与现有模块交付 Skill 可分离的新工作流，因此不新增 Skill。

## 仍保留的边界

- SignalR 推送仍是提交后尽力通知；需要可靠交付的业务事实继续只通过事务 Outbox 表达。
- 本地文件删除失败会留下可恢复的孤立 Blob，后续仍需独立清理任务。
- 异常详细信息只进入受控运行日志与 Trace 关联链，不通过审计查询 API 暴露。
- API Key 当前只支持 Host 作用域，尚未提供双管理端管理界面。
- Host 角色 `custom` 范围后端已要求显式目标租户并通过双库验证；双管理端尚无 Host 侧“目标租户＋机构”选择闭环，当前该路径只通过 API/自动化使用，不能据此声明 UI 已完成真实后端验收。
