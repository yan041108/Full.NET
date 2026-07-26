# 强化模块化单体硬化验证记录

- 日期：2026-07-26
- 分支：`codex/strengthened-modular-monolith`
- 状态：Build-verified；双数据库完整 Integration 门禁通过
- 范围：租户缓存一致性、跨模块表所有权、模块契约依赖方向、API Key 认证热路径

## 已实现边界

1. 租户名称更新和停用在业务事务内写入 `fullnet.tenancy.tenant.changed`（Schema 1）Outbox；API 节点只在事务返回后修复本机缓存，Worker 等待 L2 删除与 Backplane 广播完成，任一传播异常均进入 Outbox 重试。
2. 架构测试扫描 `src/Modules` 中的 `fn_<module>_*` 表访问；当前 7 条跨模块只读查询按来源模块、表和文件精确登记，禁止通配、重复、缺少原因/里程碑及过期债务。
3. Identity 通过自身 Contracts 中的 `IIdentityOrganizationUnitDirectory` 消费机构校验能力；Organization 同一适配器保留旧公共契约并实现新 Port，Identity 实现项目不再引用 Organization.Contracts。
4. API Key 继续每次读取并即时校验 Key/用户状态，但 `LastUsedAtUtc` 最多每五分钟尝试一次更新；应用判断和 SQL 条件更新共同限制并发物理写入。

## 验证证据

| 门禁 | 结果 |
| --- | --- |
| Restore + Release Build | **0 warnings / 0 errors** |
| Unit | **363/363** |
| Architecture | **43/43** |
| Compatibility | **7/7** |
| Naming | **23/23** |
| 项目 Skill 契约 | **52** 项通过 |
| TenantProvisioning SQL Server/MySQL | **2/2** |
| CacheConsistency SQL Server/MySQL、Redis 多节点与失败重试 | **6/6** |
| Integration 全量 | **172/172**，失败 **0**、跳过 **0**，**26m 48s** |

完整 Integration 首轮在测试宿主 `ValidateOnBuild` 阶段发现：两个精简宿主仍只注册旧 `ITenantOrganizationUnitDirectory`，没有注册新的 Identity 消费方 Port。生产 Composition 已正确注册。补齐精简宿主后，原失败的双库 Provisioning 与缓存一致性焦点集均通过。

架构级自审又关闭两项交付缺陷：租户管理 Integration 计数 SQL 已从不存在的 `EventType` 列改为规范 `MessageType`；Worker 失效选项新增 L2 同步等待与异常重抛，并通过“修改前不抛、修改后抛出 `FusionCacheDistributedCacheException`”的回归测试证明失败会进入 Outbox 重试。

## 状态与范围边界

- 本轮没有新增数据库对象或迁移；SQL Server 与 MySQL 使用相同运行时查询语义。
- Reporting 读模型、独立服务、CDC/Kafka 和生产部署清单不在本轮实施范围。
- 当前 7 条跨模块 SQL 是显式迁移债务，不代表允许新增同类访问。
- 未完成生产等价多实例指标、延迟 Worker 演练和发布编排证据，因此能力状态不提升为 `Verified`。

## 治理复盘

- Rules：现有模块化单体、Dapper、Outbox、缓存和租户边界已经覆盖本次结论；新增的是 ADR 细化与可执行架构门禁，没有新增近义强制规则。
- Skills：`fullnet-outbox-event-delivery` 更新为候选 2 次，`fullnet-cache-feature` 更新为候选 3 次；本轮只同步 `fullnet-module-delivery` 的测试数量，不新建 Skill。
