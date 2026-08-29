# Worker Native AOT 发布模式闭环验证

## 范围与基线

- 基线提交：`235f4054`
- 日期：2026-08-29
- 范围：Worker 的 `FullNetPublishMode=NativeAot` 到 SDK `PublishAot` 映射、已知第三方运行时边界和发布治理。
- 非目标：不切换默认 Worker 部署模式，不改变后台业务、SQL、消息、租户或重试语义，也不声明双库原生进程 E2E 已在本机完成。

## 根因与红灯

Worker publish 契约已传入 `FullNetPublishMode=NativeAot`，但 `Full.NET.Host.Worker.csproj` 没有像 API Host 一样设置 `PublishAot=true`。因此旧命令成功输出的只是带完整托管运行时文件的 78,256-byte apphost，并被 8 MB 产物门禁拒绝。

- MSBuild 回归测试红灯：Worker 的 `PublishAot` 实际为 false。
- 第一次最小修复后，publish 进入 `Generating native code`，证明根因判断正确。
- 随后的 ILC 红灯精确暴露 SignalR custom-awaitable 反射路径和第三方程序集告警策略未配置；没有通过降低产物大小或通配 `NoWarn` 绕过。

## 变更

- Worker 仅在显式 `FullNetPublishMode=NativeAot` 时设置 `PublishAot=true`，默认仍为 JIT。
- 原生模式关闭 SignalR custom-awaitable 反射发现和 SqlClient 反射式认证 Provider 发现。
- ILC 第三方程序集级告警交给既有精确 warning gate；自有代码继续由 Worker AOT/Trim analyzer 失败关闭。
- 精确保留 MemoryPack 与 Confluent.Kafka Linux native binding 所需元数据，不增加通配 root。
- Architecture Test 将发布许可更新为 API/Worker 两个已批准 Host；Migrator、生成器和仓库根目录仍不得继承 `PublishAot`。
- ADR-0008/0010 对齐 Worker 后续 Phase 已获批准的条件发布边界。

## 验证结果

- TDD：`WorkerProject_NativeAotPublishMode_EnablesPublishAot` 先红后绿；运行时边界测试同样先红后绿，最终 2/2 通过。
- `pnpm test:aot:worker:publish:linux`：Docker Linux SDK 完成真实 `linux-x64` 原生链接；warning gate 接受 7 个既有精确第三方告警；可执行文件 50,125,912 bytes。
- `pnpm test:aot:worker:analyzers`：AOT 分析与强制 JIT 重建均为 0 警告、0 错误。
- `pnpm test:dotnet:architecture -- --selection api-native-aot`：73/73 通过。
- `pnpm test:inner -- --snapshot worker-native-aot-publish-mode`：Release 构建 0 警告、0 错误；MySQL smoke 4/4 通过。
- `pnpm test:aot:worker:native:e2e`：Windows 精确发现 14 项，14 项按 Linux-only 规则跳过。

## 状态边界

当前可以声明 Worker 本地 `linux-x64` Native AOT publish 已闭合，不能声明 SQL Server/MySQL 原生 Worker 的后台终态已通过。14 项双库外部进程验证仍须由 `worker-native-aot-linux.yml` 在 Linux runner 执行；默认生产部署保持 JIT，是否切流仍需独立发布决策和生产等价证据。

## 演进检查

本切片修正的是现有 ADR、CI 与项目配置之间的规则冲突，并把批准清单收窄为 API/Worker；未发现需要新增通用规则或 Skill 的流程缺口。
