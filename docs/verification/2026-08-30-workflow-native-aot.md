# Workflow Native AOT 外部进程验证

## 范围与结论

- 基线提交：`be61c547d2d1ad5a2b0b4634edaadab29905f80d`
- 任务快照：`workflow-native-aot-20260830`
- 验证范围：Linux x64 Native AOT `Host.Api`、Workflow 表单/定义发布、两级线性审批、终态完成与终态驳回，以及 SQL Server/MySQL Dapper 路径。
- 结论：`Aot-published` 且本切片 `Build-verified`；真实 Linux 原生进程双库用例 2/2 通过、0 跳过。
- 非结论：本记录不覆盖 Worker 恢复、`notify.cc`、`gateway.exclusive`、生产容量或完整管理端浏览器矩阵，因此不将 Workflow 提升为 `Verified`。

## 场景闭环

两种数据库执行同一组 API 行为：

1. 使用 JIT Migrator 创建并播种隔离数据库；
2. 启动已发布的 Linux 原生 `Full.NET.Host.Api`，并以受保护管理员登录；
3. 创建并发布 Workflow 表单；
4. 创建并发布包含两个 `human.approval` 阶段的线性流程定义；
5. 启动实例，连续同意两个待办，断言实例进入 `completed`；
6. 启动第二个实例，驳回首个待办，断言实例进入 `rejected`；
7. 优雅停止原生进程，并检查日志不存在 Native AOT 启动或运行期失败。

## 新鲜验证结果

| 验证 | 结果 |
| --- | --- |
| Workflow Unit | 126/126 通过 |
| Workflow SQL Server/MySQL slice | 4/4 通过，双 Provider 均被发现 |
| `pnpm test:integration:partitions` | 663 项无遗漏或重复；`infrastructure=159` |
| `pnpm test:governance` | 52/52 通过 |
| `pnpm test:inner -- --snapshot workflow-native-aot-20260830` | 受影响工具链门禁通过；Integration 工具测试 53/53 通过 |
| `pnpm test:aot:analyzers` | 0 警告、0 错误 |
| `pnpm test:dotnet:architecture --selection api-native-aot` | 73/73 通过 |
| `pnpm test:aot:publish:linux` | Docker Linux SDK 发布成功；9 条第三方告警全部命中既定 allowlist |
| Linux 容器内 Workflow Native E2E | 2/2 通过，0 失败、0 跳过；MySQL 1:24、SQL Server 0:41，总计约 2:07 |

发布 manifest 记录 `runtimeIdentifier=linux-x64`、`publishMode=docker`，原生可执行文件大小为 `74,401,296` bytes。TRX 位于忽略的本地证据目录 `artifacts/native-aot/linux-x64/test-results/Full.NET.IntegrationTests-native-aot-workflow.trx`。

Windows 上的 `pnpm test:aot:native:e2e` 只完成非 Linux 发现门禁，发现 21 项并按设计跳过；最终通过结论来自 Linux SDK 容器内直接运行测试程序集和原生二进制，不以 Windows 跳过结果代替 Linux 证据。

## 测试矩阵与边界

- `nativeAotIntegration.minimum` 从 5 调整为 7。
- 新增两项同时归属 `infrastructure`，其最低发现数从 157 调整为 159；全量唯一事实源从 661 调整为 663。
- 本切片未增加第三方依赖，不改变 Workflow-Vue3 作者授权或 VForm3 PoC 裁决，也不产生新的许可证结论。
- 生产等价容量未验证，保持 `Capacity-not-verified`。

## 规则与 Skill 演进

未发现新的规则冲突或项目 Skill 缺口，不更新规则与 Skill 候选。
