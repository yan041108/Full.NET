# F08b：AcceptTenantInvitation 席位编排

**日期**：2026-09-22
**状态**：已实现（main）
**关联**：`foundation-productization` F08b、`module-local-transaction-debt.json`（已清零）

## 问题

接受租户邀请时，Identity 曾在同一本地数据库事务内调用 Tenancy `ITenantMemberSeatQuotaPort`（预留/确认/释放），违反模块本地事务边界。

## 决策

1. **预留**在 Identity 成员写入事务**之前**完成；失败则直接返回，不写成员。
2. **成员 + 邀请状态**仅在 Identity `ICommandTransaction` 内写入。
3. **确认**在本地事务提交**之后**调用；失败时执行**补偿事务**（成员标记 Removed 或恢复先前状态、邀请回 Pending）并 **Release** 席位。
4. 不引入跨模块两阶段提交；可靠事件/Outbox 补偿留作后续容量切片。

## 验证

- `TenantInvitationRollbackTests`（单元）
- Tenancy/Identity 集成与 real-stack 邀请接受用例（既有矩阵）
