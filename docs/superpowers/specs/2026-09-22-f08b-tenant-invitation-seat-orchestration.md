# F08b：成员席位持久化编排

**日期**：2026-09-22
**状态**：2026-10-27 持久化恢复实现并完成本地聚焦验收
**关联**：`foundation-productization` F08b、`module-local-transaction-debt.json`（已清零）

## 问题

接受租户邀请时，Identity 曾在同一本地数据库事务内调用 Tenancy `ITenantMemberSeatQuotaPort`（预留/确认/释放），违反模块本地事务边界。

## 决策

2026-10-27 直接替换原请求内补偿设计。原设计把失败恢复依赖于请求进程存活，无法解释确认响应丢失后的席位和成员状态。

1. Identity 本地事务同时写入 Pending 成员、已接受邀请（如有）及 `fn_identity_member_seat_operation`。待接受邀请不占席位；Pending 成员不具备 Active 成员权限。表由 Identity 独占写入，不与 Tenancy 建外键或共享事务。
2. 本地事务提交后，以工作项 UUID + Attempt 作为固定消费身份，在事务外调用 Tenancy 预留、确认。依次持久化 Requested、Reserved、Confirmed；确认后才在 Identity 本地事务激活成员并写 Activated。邀请、成员角色恢复及状态写入都锚定原租户、成员和版本。
3. API 可同步推进；Worker 每 5 秒扫描最多 20 项，单批预算 25 秒，单项预算 10 秒，租约 30 秒。异常不清除恢复阶段，重新取得租约后再读权威阶段；单项故障不阻塞其他项。进程中断由租约超时和幂等调用收敛，不把租约超时视为退款依据。
4. 失败或成员移除进入 Cancelling。先释放尚未确认的预留，再探测原消费的幂等取消；确认消费只能 Confirmed → Cancelled，原指标扣减和消费终态在同一 Tenancy 事务提交。原指标缺失时保留待恢复状态，不能猜测周期或当作已退款。Released、Expired、Cancelled 重放不二次退款，迟到确认不能复活消费。
5. 归还完成后，Identity 按冻结版本恢复成员及邀请，写 Cancelled。并发移除/修改不得重新激活成员或重新开放旧邀请；工作项保存同时校验租约和预期阶段，避免取消被旧执行器覆盖。
6. 成员开通以租户 + 规范用户名摘要找到原操作，并验证原账号密码及非敏感请求摘要，响应丢失后不会再建账号或再占席位。不保存密码或邀请令牌。额度不足后的新尝试增加 Attempt；手动终止或更改的原成员禁止通过旧创建请求复活。邀请重放绑定邀请 UUID、接受版本及用户。
7. 用量对账在成员计数前后各读取一次 Identity 未收敛租户集合，跳过任一集合中的租户；新消费的并发增量再由 Tenancy 指标版本 CAS 拦截。这样覆盖确认后未激活，以及成员刚移除但消费尚未归还两个窗口。
8. 既有活动成员仍由现有权威计数对账，不虚构历史预留身份。基础预设没有 Tenancy 时使用明确的空端口；装配 Tenancy 的 API/Worker 均使用正式配额适配器，运行失败不得降级为空端口。

最终一致性边界为成员激活/取消工作项；租户和成员授权仍即时读取权威状态。取消或跨模块异常时可能暂时多占额度，但不会因恢复流程授予 Pending 成员权限。重要业务事件仍遵守事务 Outbox；本工作项是 Identity 自有恢复状态，并非在 Outbox 消费事务中执行跨模块调用。

## 验证

- `TenantInvitationRollbackTests`：成员或邀请写入失败必须整体回滚，不能在失败事务外留下配额预留。
- `TenantSeatOperationProcessorTests`：确认响应丢失、新执行器恢复、并发移除、取消失败重试、原指标缺失、无效租户和租约竞争。
- `TenantQuotaRollbackTests` / `TenantQuotaReplayTests`：消费取消的原子回滚和终态重放。
- `TenantQuotaUsageBaselineServiceTests`：激活与移除窗口不覆盖尚未收敛的消费。
- `TenantSeatRecoveryTests`：SQL Server/MySQL 真实服务、事务、迁移重入、租约、剩余一个席位的竞争、创建重试及移除后只归还一次。

本切片不代表 F08 全部消费者、SaaS 支付履约、Native AOT 发布或容量验收完成。

2026-10-27 实际验证：业务单元 60/60、注册回归 2/2、双库集成 6/6、模块事务/表归属/外键架构 8/8、测试矩阵 Node 7/7 均通过，无跳过。324 两份迁移的命名、UUID 和 SQL 注释校验零违规；Identity、Tenancy 的 `FullNetAotAnalysis=true` Release 构建均零警告、零错误，并已恢复默认 JIT 依赖图。未执行全量测试或 Native AOT 发布。
