# Jobs 有界并发验证

## 范围

- 任务基线：`88aa58531773d27265c18d9b61b7b88e988fd937`
- 计划：Task 26 Step 1、Step 2
- 生产默认值：`Jobs:Worker:MaxConcurrency = 1`

本切片不调整生产默认并发，不宣称容量收益，也不运行完整容量矩阵。

## 已实现边界

1. `MaxConcurrency` 合法范围为 `1..16`，且不得超过 `BatchSize`；越界配置在 Worker
   启动期失败。
2. 默认值保持 `1`，继续走原有串行路径，不额外创建执行 Scope。
3. 显式启用并发时，以 `JobKey` 为顺序键：不同键按上限并行，相同键按领取顺序串行。
4. 每条并发执行创建独立 DI Scope，Handler、数据库命令与 Scoped 依赖不跨执行共享；
   批次领取与租约续期仍由批次 Runner 统一管理。
5. 部署方必须按 `MaxConcurrency × Worker 副本数` 预留 Handler 数据库连接，再叠加领取、
   续租和其他宿主连接预算；容量证据不足时保持默认值 `1`。

## 测试先行与本地验证

| 门槛 | 结果 |
| --- | --- |
| Options RED | 因缺少 `MaxConcurrency` 编译失败 |
| 并发语义 RED | 因 Runner 不接收执行 Scope 工厂编译失败 |
| 双库并发 RED | 独立执行 Scope 未恢复 Host 上下文，失败终态写入触发 `HostContextRequiredException` |
| Jobs 受影响 Unit | **8/8**，失败 0、跳过 0 |
| SQL Server Jobs smoke | **1/1**，失败 0、跳过 0，约 27 秒 |
| MySQL Jobs smoke | **1/1**，失败 0、跳过 0，约 52 秒 |
| Release 项目构建 | Unit 与 Integration 项目均为 0 warning、0 error |

并发 Unit 使用三个执行验证：两个相同 JobKey、一个不同 JobKey、最大并发 2。观察到
全局峰值为 2、每个 JobKey 峰值为 1，且三个执行 Scope ID 全部不同。

双库 smoke 显式设置 `MaxConcurrency = 3`，使用四条执行覆盖两个相同 JobKey、一个不同
JobKey 和一个预期失败 Handler。SQL Server 与 MySQL 均观察到全局峰值 2、同键峰值 1、
四个不同 Scope ID；慢 Handler 阻塞期间租约到期时间继续前移，失败执行进入 Failed，
其余三条进入 Succeeded，全部只领取一次并清理终态租约。既有同一 smoke 还继续覆盖
多 Worker 领取无重复终态。

双库 RED 暴露并修复了并发执行 Scope 丢失 Host 上下文的问题。Runner 现在在每个独立
Scope 内设置 Host，并在 `finally` 中清理，保证成功与失败终态写入继续经过 HostOnly
作用域门禁。

## 未完成项

- 并发 `1/2/4/8` 容量 A/B 仅允许夜间或手动 CI；两库没有可重复收益前，
  `MaxConcurrency` 默认值必须保持 `1`。
- 连接池预算、持续积压、慢 Handler 与多副本组合的容量收益仍由上述夜间/手动矩阵决定，
  不在本地 smoke 中扩展样本。
