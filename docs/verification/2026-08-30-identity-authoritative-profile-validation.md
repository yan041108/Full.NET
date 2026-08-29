# Identity Host 用户资料权威校验验证

## 交付边界

- 仅覆盖 Host 用户目录（`ScopeKey = host` 且 `TenantId IS NULL`），不创建 Tenant 用户资料语义。
- 手机号采用规范 E.164 形状；Email 转小写；工号和证件号码转大写；证件类型转小写。
- 手机号、Email、工号在 Host 目录全局唯一；证件按类型与号码组合唯一；NULL 允许重复。
- 居民身份证校验 18 位出生日期与校验码；其他稳定证件类型使用有界 ASCII 可比较格式。
- Create/Update 使用 `ExecuteResultAsync`；资料校验、版本或唯一冲突返回失败时，整笔用户事务回滚。

## 数据库与并发

迁移 101 先把五个权威比较列收敛到双库二进制排序规则，再规范历史非空值、探测重复并失败关闭，禁止静默合并历史用户。SQL Server 使用过滤唯一索引，MySQL 依赖 UNIQUE 对 NULL 的原生多值语义。恢复测试从同名畸形索引与“规范化后产生重复值”的中断状态重跑，并断言四组索引的列序、唯一性、排序规则、历史值规范化、失败关闭及修复后恢复。

唯一冲突预检只读取数据库计算的稳定冲突类型，不再物化其他用户的敏感资料；数据库比较与唯一索引共享相同排序语义。JSON 批量导入也会解析操作者字段投影，越权 Profile 行以 `authorization.permission_denied` 失败，不进入唯一性探测。

## 新鲜验证证据

| 检查 | 结果 |
| --- | --- |
| `HostUserProfilePolicyTests` | 10/10，通过；包含规范化、手机号、Email、证件配对、类型和居民身份证校验 |
| `HostUserProfileMapperTests` + Policy 聚焦 | 12/12，通过 |
| Migration 101 SQL Server/MySQL 恢复 | 2/2，通过 |
| Identity Host users SQL Server API | 1/1，通过；含四类稳定冲突码、并发 Create/Update 唯一竞态、失败事务回滚与导入字段越权拒绝 |
| Identity Host users MySQL API | 1/1，通过；同上 |
| `dotnet build Full.NET.slnx -c Release --artifacts-path artifacts/identity-authority-solution` | 通过，0 警告、0 错误 |
| `pnpm test:naming` | 30/30，通过；101 固定 DDL 已登记精确扫描器债务 |
| `pnpm test:governance` | 52/52，通过 |
| `pnpm test:aot:analyzers` | 通过，0 警告、0 错误 |
| `pnpm test:aot:publish:linux` | 通过；72,506,288 字节；9 条均为 ADR 已登记第三方告警 |
| `pnpm test:integration:partitions` | 通过；双 API 63 + 63、迁移 314、基础设施 157、Messaging-heavy 56，共 653 |
| `pnpm test:inner -- --base 2690e570...` | 通过；工具链 53/53、治理 52/52、Identity + migration-101 MySQL 聚焦 16/16 |

## 已知未关闭项

- Windows 上 `pnpm test:aot:native:e2e` 只完成 19 项发现门禁并全部 Inconclusive；Linux 原生进程实际执行留给下一收口切片或 Linux CI。
- `pnpm test:openapi` 的失败来自任务基线已存在的 Observability Admin 客户端未登记到生成清单；本切片的 Identity OpenAPI 运行时断言已随双库 API 测试通过。
- 常规完整 Unit 在 AOT analyzer 后复用共享 `obj` 会读取 AOT 条件产物并产生既有测试替身参数形状失败；本切片使用隔离 artifacts 的 12 项 Identity 聚焦测试通过，构建本身为 0 警告/0 错误。该工具链隔离问题在下一收口切片处理。

## 状态

资料权威校验保持 `Build-verified`。在 fresh 浏览器真实栈和 Linux 原生进程执行完成前，不提升为 `Verified`。
