# 全项目审查 10 项问题修复验证

基线：`49345c3c9823360071433adb4928e09691a35d62`，分支 `main`，任务快照 `fix-project-review-20261019`。本次修改尚未提交；任务开始前已有的四份文档差异保留。实现计划见 [修复计划](../superpowers/plans/2026-10-19-project-review-remediation.md)。

状态：10 项实现均已完成，规定范围的本地集中验收通过；临时产物删除被命令策略拦截，详见末节。

## 问题与实现

| 项目 | 修复与验证重点 |
| --- | --- |
| 1. 文档 ACL 未执行 | 模块权限后继续检查用户 ACL；列表、统计与日志在 SQL 中过滤；详情、内容、修改、分享与预览通过 Endpoint Filter 检查。独立权限管理入口用于恢复管理授权，超级管理员不绕过文档 ACL。双库 HTTP 与原生 HTTP 验证拒绝行为。 |
| 2. Webhook DNS 重绑定 | 在 `ConnectCallback` 中校验整组 DNS 结果并直接连接已验证的 `IPEndPoint`，关闭代理和重定向。单元测试验证解析结果和实际连接目标一致，混合公私地址失败关闭。 |
| 3. Helm 缺少 DP 证书挂载 | API/Worker 挂载只读证书，并从 Secret 注入密码、配置证书路径；模板与 schema 校验配置。Helm 渲染回归通过。 |
| 4. Webhook 领取后重复发送 | 双库短事务内持锁选择并更新 `processing` 与版本；执行前确认租约，过期记录可恢复，旧版本不可写终态。真实双库并发领取与恢复断言通过。 |
| 5. 退款处理状态无法收敛 | 复用订单对账入口查询渠道退款；核对退款号、金额、币种并要求渠道退款标识非空，再原子更新退款与订单；异常退款保持订单退款中，避免创建重复退款。状态分支及并发回滚单测、双库成功落库断言通过。 |
| 6. Webhook 超时跳过失败处理 | 内部超时进入有限重试并继续下一条；宿主取消保持取消语义。超时与后续记录继续执行的单测通过。 |
| 7. MySQL 预览领取竞态 | 同一事务内 `FOR UPDATE SKIP LOCKED`、更新和读取，SQL Server 使用原子领取。双库两个独立作用域同时领取只获得一份任务。 |
| 8. 预览进程崩溃后卡住 | 过期 `processing` 可重新领取，续租与终态写入检查版本和有效期。双库验证恢复和旧执行者写入拒绝。 |
| 9. Webhook IPv6 URI 拼接错误 | 直接校验和连接 IP，不再拼接缺少方括号的 URI。IPv6 公网连接及受限地址单测通过。 |
| 10. Office 输出管道死锁 | 并发、定额消费 stdout/stderr；超时和调用方取消均结束进程树并等待回收。真实 Node 子进程的大输出与取消回归通过。 |

## 已执行验证

- `pnpm test:dotnet:unit -- --selection project-review-remediation --reuse-build`：90/90，通过，0 跳过；Release 构建 0 警告、0 错误。
- `dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --list-tests json --no-ansi`：发现 8,926 项，不低于更新后的最低门槛；仅为发现验证，不等于全量执行。
- `pnpm test:dotnet:architecture -- --reuse-build`：235/235，通过，0 跳过；构建 0 警告、0 错误。前次运行曾因夹具源码改动触发构建输入保护，随后排队又超时；最终重新构建和完整执行成功。
- `node --test tests/sql/sql-safety.test.mjs tests/deployment/helm-contract.test.mjs tests/naming/*.test.mjs tests/database/uuid-storage-contract.test.mjs`：59/59，通过。
- `node scripts/testing/run-build-with-resources.mjs --cpu 4 --memory-gib 6 -- node scripts/testing/run-api-aot-analyzers.mjs`：通过，0 警告、0 错误，默认 JIT 还原图恢复成功。
- `node scripts/testing/run-build-with-resources.mjs --cpu 4 --memory-gib 6 -- node scripts/testing/run-worker-aot-analyzers.mjs`：分析与随后强制 JIT 重建均通过，均为 0 警告、0 错误。
- `pnpm test:slice -- --snapshot fix-project-review-20261019 --execution-group modules --reuse-build`：Tooling 163/163、Governance 85/85、真实双库 smoke 8/8 通过；Integration 分片发现校验确认 1,334 项无遗漏或重复。该轮整体未通过：新增夹具缺少商户证书序列号，且同步释放异步数据库作用域遮住原异常；Windows 另跳过 44 项 Linux 原生测试。这些跳过不算通过。
- 修正夹具后，在 `withTestRun` 与 `enterHeavyTestPhase` 资源锁内重新构建 Integration，再执行以下聚焦命令：22/22，通过，0 跳过。该组包含两个数据库上的新增 ACL、退款及领取恢复断言。

```powershell
dotnet build tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --nologo
dotnet tests/Full.NET.IntegrationTests/bin/Release/net10.0/Full.NET.IntegrationTests.dll --no-ansi --progress off --filter 'FullyQualifiedName~DocumentApiSqlServerTests|FullyQualifiedName~DocumentApiMySqlTests' --minimum-expected-tests 22 --timeout 15m --report-trx --report-trx-filename project-review-document.trx
```

## Linux 原生运行

环境为 Windows 10、64 GiB 内存、Docker Desktop Linux 守护进程（约 19.3 GiB）。SDK `10.0.402`，目标运行时 `10.0.13`，`linux-x64`。使用现有缓存 SDK 镜像 `fullnet-ai-native-sdk-402:bc74b9f1dc`，源码只读挂载，不复制仓库；产物进入独立系统临时目录。构建限制 4 CPU / 12 GiB，运行限制 4 CPU / 8 GiB，并遵守全局重型验收资源锁。

```bash
dotnet publish /src/src/Hosts/Full.NET.Host.Api/Full.NET.Host.Api.csproj -c Release -r linux-x64 -p:SelfContained=true -p:FullNetPublishMode=NativeAot -p:RuntimeFrameworkVersion=10.0.13 -p:UseArtifactsOutput=true -p:ArtifactsPath=/out/aot-build -o /out/publish --nologo
dotnet build /src/tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release -p:UseArtifactsOutput=true -p:ArtifactsPath=/out/test-build --nologo
dotnet /src/tests/Full.NET.IntegrationTests/bin/Release/net10.0/Full.NET.IntegrationTests.dll --no-ansi --progress off --filter 'FullyQualifiedName~NativeApiSqlServerE2ETests|FullyQualifiedName~NativeApiMySqlE2ETests' --minimum-expected-tests 2 --timeout 15m --report-trx --report-trx-filename native-project-review.trx
```

Linux 测试程序集及 Migrator 的输出目录覆盖挂载到各自常规 `bin/Release/net10.0` 路径，便于现有夹具定位源码并复用 JIT Migrator；`/src/artifacts` 指向本轮临时日志目录。设置 `FULLNET_NATIVE_AOT_PUBLISH_DIR=/out/publish`、临时 DP key ring、`TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal` 和 `FULLNET_TESTCONTAINERS_REUSE=0`，使用专用测试容器并在结束后回收。

结果：原生可执行文件 140,100,992 bytes；发布日志经仓库 `validatePublishWarnings` 校验，9 条告警全部属于现有第三方白名单，无新增告警类别。Linux Integration 构建 0 警告、0 错误。原生核心 HTTP 流程 2/2，通过，0 跳过，耗时 2m33s；覆盖双库、ACL、启动与优雅停止。该结果不外推为 Worker 原生运行或生产容量验证。

## 复核与边界

用户授权的独立只读审查覆盖本次差异，未发现有证据的新增 P1/P2 阻塞问题。审查未代替运行时验收。

退款渠道响应由测试客户端提供，状态写入使用真实双库；没有操作微信实商户或发起真实退款。Helm 验证为本地渲染与契约检查，没有部署到生产环境。此次没有运行全量 Unit 或全量 Integration，不提升生产容量状态。

## 临时产物清理受阻

专用原生测试容器已经回收，未删除原有共享数据库容器。以下清理命令在启动前被执行策略拒绝，原始理由为 `blocked by policy`；工具没有将其标识为 Auto-review。未更换解释器、嵌套 shell 或放宽规则重试。

因此 `C:\Users\ADMINI~1\AppData\Local\Temp\fullnet-review-native-2gnp8ed6` 的本轮原生构建、测试与日志输出仍保留（实测 4,014,638,844 bytes，9,529 个文件）；六个本轮已结束的临时失败记录目录也未删除。下面保留被拒绝的原命令作为记录，不代表执行成功：

```powershell
$taskTempRoot = (Resolve-Path -LiteralPath ([IO.Path]::GetTempPath())).Path.TrimEnd('\'); $nativeOwnedPath = (Resolve-Path -LiteralPath 'C:\Users\ADMINI~1\AppData\Local\Temp\fullnet-review-native-2gnp8ed6').Path; if ((Split-Path -Leaf $nativeOwnedPath) -ne 'fullnet-review-native-2gnp8ed6' -or (Split-Path -Parent $nativeOwnedPath).TrimEnd('\') -ne $taskTempRoot) { throw '临时产物路径边界不匹配' }; $activeOwnedContainers = docker ps --filter 'name=fullnet-review-native' --format '{{.Names}}'; if ($LASTEXITCODE -ne 0 -or $activeOwnedContainers) { throw '本轮原生容器仍在运行或无法确认' }; $activeOwnedProcesses = Get-CimInstance Win32_Process | Where-Object { $_.Name -in @('node.exe','dotnet.exe','docker.exe') -and $_.CommandLine -like '*fullnet-review-native-2gnp8ed6*' }; if ($activeOwnedProcesses) { throw '本轮临时目录仍被运行进程引用' }; Write-Output ('已验证任务归属及空闲状态：' + $nativeOwnedPath); Remove-Item -LiteralPath $nativeOwnedPath -Recurse -Force; foreach ($runSuffix in @('nfVxFJ','VsxeCF','h2j1yu','B4PMIn','Vpih6q','PiTVLD')) { $ownedRunPath = Join-Path $taskTempRoot ('fullnet-test-run-' + $runSuffix); if (Test-Path -LiteralPath $ownedRunPath) { $resolvedRunPath = (Resolve-Path -LiteralPath $ownedRunPath).Path; if ((Split-Path -Parent $resolvedRunPath).TrimEnd('\') -ne $taskTempRoot) { throw '失败记录目录超出临时边界' }; $runRecord = Get-Content -LiteralPath (Join-Path $resolvedRunPath 'run.json') -Raw | ConvertFrom-Json; if (!$runRecord.finishedAt -or $runRecord.outcome -ne 'failed') { throw '无法确认本轮失败运行已经结束' }; Remove-Item -LiteralPath $resolvedRunPath -Recurse -Force } }; Write-Output '本轮原生构建、运行输出及已被成功回归替代的临时失败目录已清理。'
```
