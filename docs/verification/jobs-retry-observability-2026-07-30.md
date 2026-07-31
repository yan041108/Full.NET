# Jobs 重试查询可观测性验证（2026-07-30）

## 状态与范围

- 状态：`Build-verified`
- 任务基线：`975da1ee9c0e073e6cfbf0bd2c2cd530063d8313`
- 任务快照：`jobs-retry-observability-20260730`
- 范围：Host 执行查询响应、Jobs OpenAPI 冻结契约、SQL Server/MySQL 重试记录查询映射。
- 不包含：队列聚合指标、告警、人工重放、Cron、通用延迟调度和退避抖动。

## 契约结论

`HostJobExecutionResponse` 新增可空 `NextAttemptAtUtc`，JSON/OpenAPI 名称为
`nextAttemptAtUtc`。运维调用方可结合既有 `status`、`errorMessage` 与
`attemptCount` 判断执行是否正在等待重试以及下一次可领取时间。成功、终态失败和从未排期的
执行返回 `null`，不会把租约内部字段暴露为公共 API。

这是向后兼容的响应属性扩展，没有改变路径、HTTP 方法、权限、状态码或既有属性语义，也没有
新增数据库迁移。

## TDD 证据

1. 静态契约 RED：Jobs OpenAPI 专项测试 2 项中 1 项失败，明确指出
   `HostJobExecutionResponse` 缺少 `nextAttemptAtUtc`。
2. 静态契约 GREEN：加入冻结契约与 C# 响应字段后，专项测试 2/2 通过。
3. 查询映射 RED：撤掉映射后，聚焦 Jobs SQL Server 测试以 `CS7036` 失败，证明新字段未被
   查询响应构造器消费。
4. 查询映射 GREEN：恢复 `JobExecutionRecord.NextAttemptAtUtc` 映射，并在真实重试生命周期
   中断言查询响应与数据库值一致。

## 新鲜验证

| 验证 | 结果 |
| --- | --- |
| Jobs SQL Server | 1/1，失败 0，跳过 0 |
| Jobs MySQL | 1/1，失败 0，跳过 0 |
| OpenAPI 契约全量 | 62/62 |
| OpenAPI 向后兼容 | 25 个基线契约、27 个当前契约，检查通过 |
| Jobs 模块 Release 构建 | 0 警告、0 错误 |

第一次 SQL Server GREEN 使用系统临时目录作为隔离输出时，测试宿主无法向上发现仓库根目录，
因此在进入业务断言前以 `DirectoryNotFoundException` 退出；改用仓库内隔离产物目录后，
SQL Server 与 MySQL 均串行通过。该环境路径失败不计作业务通过证据。

## 未验证项

- 没有运行完整 225 项 Integration；完整集合仍由 `main` CI 的互斥分片门禁运行。
- 任务快照包含 CodeGeneration 窗口在快照后写入的文件，`inner` 计划因此被扩展到
  CodeGeneration；本窗口没有重复执行其影响集。
- 本切片没有新增测试方法或迁移，`eng/testing/test-matrix.json` 门槛与 037 选择器保持不变。
- 规则演进检查未命中重复失败、高风险新类别或规则冲突；本次不新增规则候选。
- Skill 演进检查未发现 `fullnet-module-delivery` 缺口；本次不修改 Skill。
