# Integration 本地影响集验证

日期：2026-07-29

任务基线：`0599205`

范围：跨任务窗口 Integration 选择、规则与工具契约

## 结论

本地任务不再因为共享路径自动运行完整 193 项。每个任务窗口记录
`git rev-parse HEAD`，完成时先运行：

```powershell
pnpm test:integration:affected:plan -- --base <任务基线>
pnpm test:integration:affected -- --base <任务基线>
```

选择器合并任务基线后的已提交、暂存、未暂存和未跟踪变更，并映射为：

| 变化 | 本地影响集 |
| --- | --- |
| 文档、规则、纯客户端 | `none` |
| Integration 工具脚本 | `tooling` |
| 普通模块、Identity、Tenancy、Outbox、缓存等 | 对应 SQL Server/MySQL 过滤集 |
| 共享宿主、Composition、未知服务端路径 | 双库 Smoke |
| 迁移与迁移 Runner | migrations 分片 |

本地选择器没有 full 执行分支；完整 193 项继续由 `main` CI 的
`api-sqlserver`、`api-mysql`、`migrations`、`infrastructure` 四个互斥分片覆盖。

## 新鲜证据

| 验证 | 结果 |
| --- | --- |
| 选择器 RED | 新目标模型落地前，10 项契约中 7 项按预期失败 |
| 选择器 GREEN | **10/10** |
| Integration 治理契约 | **6/6** |
| 本任务自动规划 | `tooling`，未选择 Integration 全量 |
| 本任务自动执行 | 工具与治理合计 **20/20**，命令墙钟约 4 秒 |
| Auditing 真实双库影响集 | **6/6**，失败 0、跳过 0，`1m52.320s` |
| Integration Release 增量构建 | 0 警告、0 错误，`22.62s` |
| Governance | **13/13** |
| 项目 Skills | module-delivery **52**、performance-hardening **33** 项契约通过；两个 `quick_validate.py` 均通过 |
| main 分片发现 | `35 + 35 + 62 + 61 = 193`，无遗漏或重复 |

对比最近一次完整 193 项的 `36m08s`，Auditing 日常反馈连同增量构建约
`2m15s`。这只证明本地反馈链缩短，不代表单个测试本身或生产请求吞吐得到改善。

完整本地测试曾按旧规则启动，但在用户确认“本地只运行受影响测试”后中止；残留
`pnpm`、Node 和 dotnet 测试进程树已按明确 PID 终止，未把中止运行记为通过。

## 不变量

- 聚焦过滤器执行前必须发现 SQL Server 与 MySQL，且以精确发现数作为最低门槛。
- 每次运行先构建当前 Release Integration 程序集，不复用变更前二进制。
- 没有共享可变数据库，也没有跳过受影响的迁移恢复测试。
- `main` CI 仍验证四分片合计等于 canonical 193 项且无重复、遗漏。
