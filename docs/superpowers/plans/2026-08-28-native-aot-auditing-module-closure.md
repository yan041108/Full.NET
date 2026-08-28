# Auditing 模块 Native AOT 闭环实施计划

**Goal:** 让 Host.Api Native AOT 原生产物在 SQL Server/MySQL 上完成 Auditing 核心写入与 Host 查询链路，并以静态门禁阻止匿名 SQL 参数和遗漏物化器回归。

**Architecture:** 保持四类审计可靠性、微批动态 SQL、Host 数据范围、脱敏和权限语义不变。查询及保留任务使用固定键名参数袋；六类查询记录在模块启动时注册静态物化器。Native E2E 通过既有请求产生访问/操作日志，再验证分页查询；异常与出站探针只在现有测试环境许可范围内覆盖。

**Baseline:** `53fb295d86419b547249b66e331f1ec2d5bfb1ab`

**Task snapshot:** `native-aot-auditing-20260828`

**Scope boundary:** 不改变审计可靠性分级、Channel/事务、保留策略默认值、脱敏契约、数据库结构或公共 API；动态微批 SQL 继续使用已有字典参数，不强行固定命令 Plan。

## Task 1: 静态闭包 RED

- [x] 门禁拒绝 Auditing SQL 执行调用中的匿名参数对象。
- [x] 门禁要求六类查询记录注册 AOT 物化器。
- [x] 聚焦测试先失败于现存匿名参数和缺失 contributor。

## Task 2: 参数与物化器 GREEN

- [x] 新增固定键名参数工厂并替换查询、Dashboard、Retention 匿名参数。
- [x] 注册 Access/Operation/Exception/Outbound/Dashboard 六类记录物化器。
- [x] 在 `FULLNET_AOT_COMPILE` 下注册并完成 Architecture/AOT analyzer 验证。

## Task 3: 双库原生进程闭环

- [x] 扩展 Native E2E，验证审计写入最终可由 Host 查询端点读回。
- [x] 发布 Linux ELF，并执行 SQL Server/MySQL 原生进程测试。
- [x] 完成快照影响集、治理、命名、diff 检查、独立审查、验证记录与提交。
