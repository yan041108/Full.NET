# Realtime Redis Backplane 故障恢复验证（2026-07-26）

- 范围：专用 Realtime ready 健康检查、双 API 节点跨节点投递、Redis 中断与自动恢复
- 计划：[实施计划](../superpowers/plans/2026-07-26-realtime-redis-backplane-recovery.md)
- 运维：[故障与恢复说明](../operations/realtime-redis-backplane.md)
- 状态：**Build-verified**

## 行为合同

1. 运行连接固定 `AbortOnConnectFail=false`，Channel Prefix 为
   `fullnet:{environment}:signalr:`。
2. 仅配置 `Realtime:RedisBackplaneConnectionString` 时注册
   `realtime-backplane` ready 检查，不误注册 `distributed-cache`。
3. Backplane 不可达时 `/health/ready=503`，`/health/live` 与
   `/health/startup` 保持 200。
4. 客户端连接节点 A、节点 B 发布用户组消息时可跨节点收到。
5. Redis 中断期间不得把跨节点消息当作已送达；Redis 在同一端点恢复后，两个宿主和
   客户端都无需重启即可恢复投递。

## RED 与根因纠偏

| 阶段 | 结果 |
| --- | --- |
| Unit RED | `RealtimeRedisConfiguration` 尚不存在时构建按预期失败 |
| Unit GREEN | `RealtimeBackplaneRegistrationTests` **2/2** |
| HTTP 聚焦 | 专用 Backplane 不可达 **1/1**；完整 `HealthEndpointTests` **8/8** |
| 双节点首次故障演练 | SQL Server/MySQL **0/2**，均在 Redis 重启后持续 ready 503 |
| 根因诊断 | Docker 随机宿主端口在同一容器 stop/start 后重新分配；诊断复现端口 `32796 → 32797`，容器内已 `PONG`，API 仍指向旧端点 |
| 夹具修复 | 专用 Redis 预留固定宿主端口，并断言 stop/start 前后连接端点不变 |
| 双节点 GREEN | SQL Server 单项 **1/1**；SQL Server/MySQL 完整聚焦 **2/2**，0 失败、0 跳过 |
| Release 编译 | Unit 与 Integration 测试项目均 **0 warning / 0 error** |

首次双节点失败证明的是测试基础设施端点漂移，不是生产重连失败。夹具修复没有改变生产
实现或放宽业务断言；固定端点后才真正覆盖生产部署中“Redis 服务在原端点恢复”的语义。

## 安全与可靠性边界

- 健康结果不包含连接字符串、异常堆栈或 Redis 内部类型。
- Channel Prefix 不包含租户、用户、连接或消息机器码。
- 即时下行仍是尽力交付；需要可靠传播的业务状态继续使用事务 Outbox。
- Vue/Layui SignalR 客户端已通过共享契约、双端单测、Mock parity 与生产构建；
  当前仍未验证生产多副本编排、Redis Cluster/Sentinel、TLS、指标告警和浏览器真实
  后端断网恢复 E2E，因此能力不提升为 `Verified`。

## 最终门槛

| 门禁 | 新鲜结果 |
| --- | --- |
| Release 构建 | `Full.NET.slnx` **0 warning / 0 error** |
| NuGet 漏洞审计 | 全解决方案直接与传递依赖均无已知漏洞 |
| Unit / Compatibility / Architecture | **392/392 / 7/7 / 49/49**，失败 0、跳过 0 |
| Health 聚焦 | `HealthEndpointTests` **8/8** |
| Realtime 双库聚焦 | SQL Server/MySQL **2/2**，失败 0、跳过 0 |
| Integration 分片 | API SQL Server **35** + API MySQL **35** + Migrations **62** + Infrastructure **57** = **189** |
| Integration 全量 | **189/189**，失败 0、跳过 0，**50m49s**，stderr 0 |
| canonical 门槛 | **392/7/49/189** |
| 静态与治理 | Naming **23/23**、OpenAPI **58/58**、breaking **25/25**、Governance **11/11**、Integration tooling **4/4**、Skill **52**、partition/workspace 均通过 |

最终全量在 `main@1745244` 与本任务源码组成的树上执行；后续同步到
`main@1d994ca` 的变化仅涉及 OpenAPI Node 门禁、Notifications 客户端与文档，不改
C#、数据库或 Integration。同步后的 OpenAPI 58、breaking-change、Governance、
Skill、workspace 与 Integration 分片门禁均已重新通过。

## 规则与 Skills 复盘

- 规则：登记候选经验
  `C-20260726-testcontainer-restart-port-stability`，次数 1；固定端点断言已自动化，
  尚未达到第二次重复或高风险升级门槛。
- Skills：本任务把 `fullnet-realtime-feature` 候选由 **1 → 2**；同步管理端客户端
  后仓库聚合证据为 **3**。当前仍只有一类业务消费者，且缺生产编排/浏览器真实断网
  恢复流程，创建独立 Skill 会扩大任务范围，因此保留候选。

## main 收口

- Realtime 由 `75e3d3f` 非快进合并到 `main`；合并树与已验证功能分支树一致。
- 合并后的 Release 构建 **0 warning / 0 error**，Unit **392/392**，OpenAPI
  **58/58**、breaking **25/25**、Governance **11/11**、Skill **52**、
  workspace、Integration tooling **4/4** 与四分片 **189** 精确发现均通过。
- 首次合并后验证曾把解决方案构建与 Integration `--list-tests` 并行执行，后者锁定
  旧 DLL，导致构建产生 5 次复制重试且分片脚本读到旧 **184** 项；锁进程退出后串行
  重建与复跑恢复 **0 warning / 0 error** 和 **189** 项。该结果属于本次验证编排竞态，
  未修改或放宽产品代码、测试和门槛。
- `codex/realtime-redis-backplane-recovery` 分支、Git 工作树注册与物理目录均已删除；
  变更可由 `main` 提交历史恢复。
