# Audit 小批量保留清理验证

- 日期：2026-07-29
- 任务基线：`0d9bab4290a19c9968c52c2850fb25dff095dbd8`
- 范围：Audit 子切片；Outbox 成功终态清理由后续验证记录承接，持续写入容量矩阵仍开放

## 已实现边界

- 生产默认关闭，保留期、批次、单轮上限和轮询间隔均有启动校验；
- Worker 独占后台注册，API 不启动清理器；
- Access、Operation、Exception 按轮转公平推进；
- SQL Server 使用有界锁提示 CTE；MySQL 在短事务内领取 ID 并只删除领取集合；
- 严格使用 `OccurredAtUtc < CutoffUtc`，无需新增数据库对象或索引；
- 删除行数、失败数、最近成功时间和单轮耗时使用低基数指标。

## 新鲜验证

| 验证 | 结果 |
| --- | --- |
| Auditing retention Unit | **4/4**，失败 0，跳过 0 |
| MySQL 真实清理 | **1/1**，旧记录分批删除、新记录保留、三类公平推进 |
| SQL Server 真实清理 | **1/1**，旧记录分批删除、新记录保留、三类公平推进 |
| 最终 Auditing 影响集 | **10/10**，失败 0，跳过 0 |
| 共享宿主 Smoke | **8/8**，失败 0，跳过 0 |
| Integration tooling | **20/20**，当时四分片精确覆盖 197 项 |
| Release build | Unit/Integration 项目均 **0 warning / 0 error** |

第一次双库运行暴露聚合列物理类型差异：SQL Server 返回 `Int32`，MySQL 返回 `Int64`，
而 Dapper 的位置记录构造器要求精确签名。测试夹具最终改用无参 DTO 和可写 `Int64`
属性，由 Dapper 在赋值边界完成数值转换；双库定向复测 **2/2** 通过。生产删除 SQL 未因
该夹具问题改变。

最终影响集通过任务基线选择器运行；当时完整 **197** 项 Integration 只保留给 `main` CI 的四个
互斥并行分片，不在本地执行。
