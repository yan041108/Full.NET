# Tenant SQL Scope Guard 加固验证

## 范围与基线

- 基线提交：`22f576bd249e4cd6504525e71e86e5b962ff122c`
- 日期：2026-08-29
- 范围：`SqlScopeGuard` 对 `TenantRequired` 固定 SQL 的执行前检查。
- 非目标：不修改 SQL、数据库结构、Provider 行为、事务、Outbox 或租户来源。

## 风险与实现

旧实现使用 `Text.Contains("@TenantId")`，注释、字符串、投影、无约束 SET 和参数名前缀都可能形成假命中。

候选实现使用 AOT 安全的轻量词法扫描：

- 跳过 SQL Server/MySQL 的行注释、块注释、字符串和引号标识符；
- 要求完整 `@TenantId` 参数令牌；
- WHERE/JOIN ON 必须把 `TenantId` 与参数做等值比较；租户目录根记录允许 `Id = @TenantId`；
- INSERT 必须在 VALUES 子句使用参数；
- 每个不可变 `SqlStatement` 通过 `ConditionalWeakTable` 只扫描一次，后续执行只读取缓存结论；
- 不引入反射、动态代码或通用 SQL Parser。

这是一项安全加固，不声明吞吐或延迟提升。未运行生产等价容量测试，状态为 `Capacity-not-verified`。

## 验证

- `SqlScopeGuardTests`：覆盖注释、字符串、块注释、投影、SET、参数前缀、错误列、恒真参数检查、双库引号标识符，以及 WHERE/JOIN ON/VALUES 合法形状。
- `SqlDataScopeRulesTests.Production_tenant_statements_use_tenant_parameter_in_a_safe_clause`：反射读取生产程序集全部静态 `TenantRequired` Statement，并执行相同运行时守卫。
- `pnpm test:integration:affected:plan -- --base 22f576bd249e4cd6504525e71e86e5b962ff122c --phase inner`：选择共享 Dapper smoke。
- `pnpm test:inner -- --base 22f576bd249e4cd6504525e71e86e5b962ff122c`：Release 构建 0 警告、0 错误；4 个 MySQL smoke 因本机 Docker 未运行而无法启动 Testcontainers，不能记为通过。

## 未验证项

- SQL Server/MySQL 真实数据库 smoke 等待 Docker 或 CI 环境执行。
- 未执行生产等价延迟、吞吐、分配或容量认证；本变更不提供性能收益结论。

## 规则演进

本次审查证明旧的参数子串检查一次即可把注释、字符串或无约束表达式误判为租户过滤，属于潜在租户越权的高风险类别。`rules/development-quality.md` 第 5 节第 3 条已补充可执行约束，并由上述 Unit 与 Architecture Tests 自动化验证。未发现项目 Skill 的流程缺口，不修改 Skill。
