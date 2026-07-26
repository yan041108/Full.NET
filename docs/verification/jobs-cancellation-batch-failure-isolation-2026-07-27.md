# Jobs 取消传播与批次故障隔离验证（2026-07-27）

## 目标

补齐 Jobs Worker 的两项可靠性边界：

1. 宿主停止或调用方取消时，Runner 立即传播取消，不把执行误记为业务失败。
2. 同一领取批次中一条任务缺失 Handler 时，该任务独立失败，后续健康任务仍可成功。

本次不修改公共 API、数据库对象、领取 SQL、租约时长或重试策略。

## 实现与不变量

- `JobExecutionRunner` 只在传入令牌已经取消时单独传播
  `OperationCanceledException`；普通处理器异常仍记录日志并标记当前执行失败。
- 取消发生时保留当前运行中租约，不用已取消令牌写补偿状态；现有租约过期恢复路径负责重新领取。
- 双库场景在既有 `JobsApiSqlServerTests` 与 `JobsApiMySqlTests` 内各写入一条缺失
  Handler 的待执行记录和一条健康 `jobs.ping` 记录，并用一次批量领取处理两条记录。
- 失败与成功记录都必须只领取一次、清空租约并写入结束时间；失败记录保留错误信息，成功记录不保留错误信息。

## 测试先行证据

取消回归测试最初在生产实现未修改时失败：期望收到
`OperationCanceledException`，实际没有抛出异常，证明通用 `catch (Exception)` 吞掉了宿主取消。
加入仅针对已取消调用令牌的捕获分支后，聚焦 Unit 测试 **1/1** 通过。

## 自动化证据

| 门槛 | 结果 |
| --- | --- |
| Unit 聚焦 | **1/1**，失败 0、跳过 0 |
| Full.NET.slnx Release | **0 warning / 0 error** |
| Unit / Compatibility / Architecture | **396/396** / **7/7** / **49/49**，失败 0、跳过 0 |
| Integration Release 构建 | 0 warning / 0 error |
| Jobs SQL Server/MySQL 聚焦 | **2/2**，失败 0、跳过 0，约 **60s** |
| Integration 分片发现 | API SQL Server **35** + API MySQL **35** + Migrations **62** + Infrastructure **57** = **189** |
| Governance / Project Skill / Workspace | **11/11** / **52** 项 / 通过 |
| C# format | 四个受影响文件 `--verify-no-changes` 通过 |
| Docker | SQL Server/MySQL 测试容器与 Ryuk 已退出 |
| canonical | Unit **395 → 396**；Compatibility/Architecture/Integration 保持 **7/49/189** |

规则复盘没有达到新增规则门槛：现有后台取消、租约恢复和坏消息批次隔离规则已经覆盖本次缺陷。
Skills 复盘也没有发现项目 Skill 缺口；本次只是一次 Jobs Runner 局部修复，机械门槛同步已更新既有
`fullnet-module-delivery` reference。

## 能力边界

本验证证明宿主取消不会被误分类为业务失败，并证明缺失 Handler 不会阻断同批后续任务。
Cron/延迟调度、失败重试分类、运维重放、长耗时 Handler 的主动续租和大规模压力基准仍未完成。

## 关联

- [Jobs Host 任务定义验证](jobs-host-definitions-2026-07-26.md)
- [Jobs 多 Worker 原子领取验证](jobs-multi-worker-claim-2026-07-27.md)
- [能力状态](../roadmap/capability-status.md)
