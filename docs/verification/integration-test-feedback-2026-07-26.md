# Integration 测试反馈提速验证

日期：2026-07-26
基线：`main` / `d17aa19`
范围：Integration 测试启动、分层命令、耗时诊断与 main CI 门禁
状态：已验证

## 结论

此前 SQL Server/MySQL 全量 Integration **172/172** 的单进程墙钟为 **31分13秒**。本次保持测试数量和数据库隔离语义不变，将 main 门禁拆为四个互斥且穷尽的分片；本机新鲜运行结果如下：

| 分片 | 发现与结果 | 墙钟 |
| --- | ---: | ---: |
| API / SQL Server | **34/34** | **3分12秒** |
| API / MySQL | **34/34** | **15分31秒** |
| Migrations | **62/62** | **13分19秒** |
| Infrastructure | **42/42** | **7分46秒** |
| 合计 | **172/172** | CI 并行临界路径约为最慢分片 **15分31秒** |

最后一项是根据同机分片运行结果得出的 CI 墙钟推断；真实 GitHub Hosted Runner 仍会受镜像缓存、机器规格和网络影响，不能把本机数字当作 CI SLA。

## 已实施

1. `SharedDatabaseFixture` 改为首次消费时启动 SQL Server、MySQL 或 Redis，并使用独立异步锁避免并发重复启动。
2. 每个测试仍创建独立数据库，没有启用 Testcontainers 跨运行复用，也没有在测试之间共享可变业务数据。
3. 新增 `test:integration:*` 标准命令；每个分片保留精确最低测试数量，四个分片数量为 `34 + 34 + 62 + 42 = 172`。
4. 新增分片发现校验器，使用 MTP JSON 发现结果逐项校验 UID，阻止分片重复、遗漏或数量漂移。
5. 新增 TRX 分析器，输出结果数、累计耗时、数据库提供程序、测试套件和最慢 20 项。
6. main CI 使用四分片矩阵并行运行，由独立汇总门禁确认全部成功；PR 保持 8 项双库 smoke。
7. 开发规则与模块交付 Skill 改为按变更风险分层，不再要求所有模块局部改动在本地机械重跑 172 项。

## 按需启动证据

8 项双库 smoke 新鲜运行 **8/8**，墙钟 **2分13秒**；历史记录约为 **3分42秒**。运行期间 Docker 只出现 SQL Server、MySQL 和 Testcontainers 清理容器，未启动 Redis。

Infrastructure 分片开始时只启动 SQL Server/MySQL；运行约 6 分钟、首次进入 Redis Backplane 场景后才出现 Redis 容器，证明 Redis 由真实消费者延迟启动。

## TRX 发现

四个分片的累计测试耗时（并行测试的各用例 duration 求和，不等同墙钟）为：

- SQL Server API：**6分16秒**；
- MySQL API：**30分54秒**；
- Migrations：**26分26秒**；
- Infrastructure：**13分09秒**。

主要剩余瓶颈位于 MySQL。MySQL API 大部分用例约 **50–60 秒**，最慢的 Access/Operation Log 用例各约 **1分43秒**；迁移分片最慢项集中在 MySQL Outbox、迁移幂等和发布候选升级演练。

## 暂不采用的优化

本轮不复用已迁移/已引导的可变数据库，也不在同一 Factory 上并发运行多个测试。现有 API 测试会修改默认租户、角色、权限、会话、审计和后台任务状态，直接共享会引入测试顺序依赖与并发污染。

下一阶段只有满足以下条件才可继续：

1. TRX 进一步区分数据库迁移、宿主启动、管理员引导和测试主体耗时；
2. SQL Server 使用模板备份/还原、MySQL 使用可验证的 schema 模板或快照克隆；
3. 每个测试继续获得独立数据库，或建立已验证的确定性重置协议；
4. 双库、并行、后台 Worker 和失败清理全部通过后，再把模板克隆纳入默认路径。

## 验证命令

```powershell
dotnet build tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj --configuration Release --no-restore
pnpm test:integration:tooling
pnpm test:integration:partitions
pnpm test:governance
pnpm test:skills
pnpm test:integration:smoke
node scripts/testing/run-integration-shard.mjs api-sqlserver
node scripts/testing/run-integration-shard.mjs api-mysql
node scripts/testing/run-integration-shard.mjs migrations
node scripts/testing/run-integration-shard.mjs infrastructure
```

所有命令均使用最终实现生成的 Release 测试程序集；四分片合计 **172/172**，失败 **0**、跳过 **0**。
