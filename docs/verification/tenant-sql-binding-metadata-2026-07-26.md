# 租户 SQL 绑定元数据验证记录

- 日期：2026-07-26
- 分支：`main`
- 状态：Build-verified
- 范围：`SqlStatement`、统一 Dapper 执行边界、Organization/Tenancy 租户 SQL、全模块 Architecture 门禁

## 已实现边界

1. `SqlStatement` 新增 `SqlTenantBinding` 元数据；`None` 与
   `CurrentTenantId` 分别表达“不注入租户参数”和“注入受信任当前租户参数”。
2. 保留既有三参数构造函数和三值 `Deconstruct`，旧调用方默认得到 `None`；
   租户 Statement 必须显式迁移，避免默认值静默扩大租户读取范围。
3. `SqlScopeGuard` 不再以 SQL 文本包含判断代替语义声明：
   `TenantRequired` 必须与 `CurrentTenantId` 成对，
   `Global`/`HostOnly` 必须与 `None` 成对；同时保留 `@TenantId` 参数存在性检查作为纵深防护，
   可信租户上下文和 Host 上下文检查保持不变。
4. `DapperSqlExecutor` 只按 `TenantBinding` 注入 `TenantId`，调用方提供的同名值不能改变
   Scope/Binding 门禁。
5. Organization 34 条、Tenancy 1 条现有 `TenantRequired` Statement 已补齐绑定元数据；
   SQL 文本、参数名、事务、排序、分页和数据库对象均未改变。
6. 新增全模块 Architecture 门禁，扫描 BuildingBlocks、宿主以及 Identity、Tenancy、
   Organization、Settings、Auditing、Files、Jobs、Notifications 的静态 Statement。

## 测试先行证据

1. 范围守卫测试先因 `SqlTenantBinding` 与 `TenantBinding` 尚不存在而编译失败。
2. 最小边界实现后，范围守卫焦点测试 **3/3** 通过。
3. 新 Architecture 门禁首次运行准确报告 **35** 条
   `TenantRequired/None` 声明；迁移元数据后焦点门禁 **1/1** 通过。
4. 首次把 Jobs/Notifications 加入共享 `ProductionAssemblies.All` 时，
   既有命名测试暴露 14 个范围外历史错误码。根因是共享程序集目录同时驱动多类门禁；
   最终改为 SQL 专用程序集集合，既覆盖全部官方模块，也不隐式修改公共错误码或制造临时命名债务。
5. 独立审查发现“只声明绑定但 SQL 漏掉参数”的未来越权风险，以及旧命名参数调用兼容缺口；
   两项均先补失败测试，再恢复参数存在性纵深检查并保留 `Name`/`Text`/`Scope` 参数名。
   复审结果为 Critical **0**、Important **0**、Minor **0**。

## 验证证据

| 门禁 | 结果 |
| --- | --- |
| Release Build | **0 warnings / 0 errors** |
| Unit 全量 | **366/366**，失败 **0**、跳过 **0** |
| Compatibility | **7/7**，失败 **0**、跳过 **0** |
| Architecture | **44/44**，失败 **0**、跳过 **0** |
| Naming | **23/23**，失败 **0** |
| 项目 Skill 契约 | **52** 项通过 |
| Organization + Tenancy SQL Server/MySQL 焦点 | **12/12**，失败 **0** |
| Integration 全量 | **172/172**，失败 **0**、跳过 **0**，**27m 32s** |
| `git diff --check` | 通过 |

## 兼容性与剩余边界

- 三参数 `SqlStatement` 构造和三值解构保持可用；新增绑定参与 Record 值语义。
- 旧的外部 `TenantRequired` Statement 若未迁移绑定会被守卫明确拒绝，不会静默执行；
  这是租户安全边界的预期收紧。
- 元数据不能代替真实 SQL 租户谓词。`rules/development-quality.md` 继续要求查询和写入条件
  真实包含租户过滤；双库集成测试、SQL 审查和生成规范共同负责该层语义。
- 本轮完成 `TenantRequired` 受控绑定；`Global` Statement 精确目录仍是下一项 P1，
  不在本轮隐式扩展。

## 治理复盘

- Rules：明确长期租户安全决策已写入 `rules/development-quality.md` 第 5 节，
  并由 `SqlDataScopeRulesTests` 自动验证 Scope/Binding 一致性。
- Skills：`fullnet-module-delivery` 已覆盖 Dapper、租户隔离、双库与验证流程；
  本轮只同步 Architecture 数量门槛，没有形成新的重复且稳定工作流，不新增或演进 Skill。
