# 五项分层问题修复与验证

基线 `49345c3c9823360071433adb4928e09691a35d62`，分支 `main`，任务快照 `layering-fixes-20261019`。保留任务前的上一批修复和已有文档改动；未提交、推送或部署。

本文先保留五项分层修复的原始验收事实；剩余三项现已修复，全量 Unit 复验无失败，详见末尾“剩余三项失败的后续修复”。

## 实现

1. **P1：失败结果与事务提交一致。** 分发器使用 Result 感知事务。默认适配器先通过异常路径回滚再还原失败结果；Dapper 在连接会话上记录 rollback-only，嵌套失败或逃出的异常不能被外层吞掉后提交。登录失败计数、刷新重放撤销和旧密码错误审计通过明确的认证拒绝策略提交；改密后段会话失效不在白名单中。
2. **P1：审批依赖闭包。** `DataApproval` 不再引用流水号程序集或声明伪可选依赖，改为消费自己拥有的 `IDataApprovalScenarioHandler`。流水号提供快照及幂等应用适配器。流水号规则管理实际使用审批策略，因此保留 `SerialNumbers → DataApproval` 的显式依赖。缺失场景适配器时明确失败，不会自动批准。
3. **P2：文档应用授权。** 删除根据 URL 推断权限的过滤器。查询、内容、版本、分享和回收站操作在应用入口通过 `IDocumentAccessPolicy` 检查明确操作；HTTP 适配器只提供可信身份。列表继续在 SQL 中过滤。公开分享先校验分享凭据与任务归属，再走显式内部授权路径；后台保留策略不依赖 HTTP 身份。
4. **P2：投递端口归属。** `IIdentityChallengeDeliveryPort` 与投递意图迁至 `Identity.Contracts`，Identity 删除对 Notifications.Contracts 的项目引用和可选模块声明。通知模块提供实现，未装配通知时仍由 Identity 的失败关闭实现处理。
5. **P2：示例隔离。** 官方 Composition、Full/Enterprise 预设与迁移资源移除 EnterpriseRequest。示例自行拥有导入键和双库迁移；示例测试显式启用应用模块并物化 Application 来源目录。官方数据库注释目录、模板预设及现行架构文档同步更新。

## 集中验证记录

- 受影响 Unit 最终集合：1433/1433，通过，0 跳过。最后的默认包装器嵌套异常补强及下一轮事务恢复也包含在此集合。
- 完整 Architecture：239/239，通过，0 跳过。包含新增的消费方端口、审批程序集依赖、官方组合和示例迁移隔离门禁。
- Windows 真实 SQL Server / MySQL：38/38 用例通过，0 跳过，耗时 11m25s。包括改密后段回滚、失败登录计数和审计持久化、裁剪流水号后的审批闭包、文档全部 API 回归、审批场景、示例 CRUD/工作流/CSV 导入及官方导入目录。
- 上述双库命令误填 `--minimum-expected-tests 40`，因此进程退出码为 9。原始 TRX 的 38 项全部 Passed，随后逐项核对两个新增关键场景均在两种提供程序上执行；该命令的失败退出码保留在记录中。最后新增的默认事务包装器双库回归单独执行。
- 命名、UUID SQL：34/34，通过；模板预设/示例 Schema：5/5，通过；模板迁移归属/选项/manifest：5/5，通过。
- API 与 Worker AOT/Trim 最终分析均 0 告警、0 错误（最后事务补强后重新执行）。
- Linux Native AOT 发布成功，可执行文件 139,702,032 bytes；发布日志通过 `validatePublishWarnings` 校验，9 条均为现有第三方白名单告警。Linux Integration 测试程序集构建 0 警告、0 错误。
- 最终 Linux 运行：4/4，通过，0 跳过，耗时 4m07s。包括 SQL Server / MySQL 原生 API 关键 HTTP 流程各一项，以及默认事务包装器吞掉嵌套拒绝后仍不能提交密码写入的真实双库回归各一项。后两项使用 JIT 测试宿主，不将其称为原生 API 测试。与 Windows 的 38 项合计，真实双库功能回归 40 项通过，另有原生 API 流程 2 项通过。

初次全量 Unit：8934 项，8607 通过、326 失败、1 个 Linux 专属场景在 Windows 跳过。包含本批已修正的测试适配/旧预设断言、AI 并行 I/O 容量耗尽、时限和既有日志断言。随后以单 Worker 完成全量诊断：8934 项，8930 通过、3 失败、1 跳过，耗时 10m19s。三项失败为 Identity 注册快照两项和 Auditing 参数数量一项；AI 并行资源耗尽在该次诊断中未再出现。最后新增的一个嵌套异常用例通过最终聚焦集合验证，不把聚焦结果标记为全量通过。

独立复核对照基线源码确认：`IdentityModule.AddServices` 已先注册日志时间线读取器，而 `IdentityModuleRegistrationTests.ExpectedIdentityOwnedRegistrations` 的固定列表缺失该项，导致委托装配与重复注册两项快照失败；`AccessLogBatchSql` 已含每行 12 个参数（包含 `CorrelationJson`），两行实际 24 个，`AuditingAccessLogWritePathTests` 仍断言 22 个。本批未改动上述断言、日志时间线注册或审计 SQL 构造器；不为本次五项修复扩大修改范围，也不将这些失败报告为通过。

## 关键实际命令

```powershell
dotnet build tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj --no-restore --verbosity quiet
dotnet tests/Full.NET.UnitTests/bin/Debug/net10.0/Full.NET.UnitTests.dll --no-ansi --progress off --settings $env:TEMP/fullnet-layering-serial.runsettings --minimum-expected-tests 8576 --timeout 15m --report-trx --report-trx-filename layering-unit-serial.trx --results-directory $env:TEMP/fullnet-layering-tests
dotnet tests/Full.NET.ArchitectureTests/bin/Debug/net10.0/Full.NET.ArchitectureTests.dll --no-ansi --progress off --minimum-expected-tests 239 --timeout 8m --report-trx --report-trx-filename layering-architecture-delivery.trx --results-directory $env:TEMP/fullnet-layering-tests
node $env:TEMP/fullnet-layering-integration.mjs
pnpm test:aot:analyzers
pnpm test:aot:worker:analyzers
node --test tests/naming/*.test.mjs tests/database/uuid-storage-contract.test.mjs
node --test tests/templates/project-preset-composition.test.mjs samples/enterprise-request/tests/schema.test.mjs
node --test tests/templates/migration-script-modules.test.mjs tests/templates/template-options.test.mjs tests/templates/framework-manifest.test.mjs
```

双库脚本的实际过滤器为 `LayeringRegressionApiTests | DocumentApiSqlServerTests | DocumentApiMySqlTests | DataApprovalApi | EnterpriseRequestApi | ImportExportApi | Login_and_current_user_follow_secure_http_contract`（每项以 `FullyQualifiedName~` 匹配），最低数误设为 40；TRX 对每项结果和两种提供程序分别核对。聚焦 Unit 覆盖 Identity、Document、Modularity、Messaging、Data、DataApproval、SerialNumbers、Notifications、Tenancy，明确排除下面单列报告的两项 Identity 注册快照失败，未隐藏这些失败。

原生构建内部实际命令：

```bash
dotnet publish /src/src/Hosts/Full.NET.Host.Api/Full.NET.Host.Api.csproj -c Release -r linux-x64 -p:SelfContained=true -p:FullNetPublishMode=NativeAot -p:RuntimeFrameworkVersion=10.0.13 -p:UseArtifactsOutput=true -p:ArtifactsPath=/out/aot-build -o /out/publish --nologo
dotnet build /src/tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release -p:UseArtifactsOutput=true -p:ArtifactsPath=/out/test-build --nologo

dotnet /src/tests/Full.NET.IntegrationTests/bin/Release/net10.0/Full.NET.IntegrationTests.dll --no-ansi --progress off --filter 'FullyQualifiedName~NativeApiSqlServerE2ETests|FullyQualifiedName~NativeApiMySqlE2ETests|FullyQualifiedName~Nested_default_transaction_adapter_rejection_cannot_commit_password_write' --minimum-expected-tests 4 --timeout 15m --report-trx --report-trx-filename layering-native-and-transactions.trx
```

## 最终结果

五项修复及受影响范围集中验证已完成。受影响 Unit 1433 项、完整 Architecture 239 项、真实双库功能回归 40 项、原生 API 流程 2 项和相关 Node 结构测试 44 项均通过，所列最终集合无跳过。全量 Unit 诊断仍有上述 3 项既有断言失败、1 项平台跳过，未报告全项目测试全绿；未运行全量 Integration。

独立只读审查已完成，未发现阻断问题；最后的默认包装器嵌套异常风险也经独立复核确认并补强。审查不替代测试证据。最终检查 `git diff --check` 返回 0（生成的数据库注释 JSON 有 Git 换行转换提示）；分支和 HEAD 保持上述基线，已检查 `git status` 并保留任务前改动。

原始证据位于系统临时目录：`fullnet-layering-tests/{layering-unit-delivery,layering-architecture-delivery,layering-integration,layering-unit-serial}.trx`；最终 Linux TRX 位于 `fullnet-review-native-2gnp8ed6/test-build/bin/Full.NET.IntegrationTests/release/TestResults/layering-native-and-transactions.trx`。Native 构建与运行包装器分别退出 0；Windows 38 项包装器退出 9 的原因和 TRX 结果保留在上文。

## 运行与产物边界

Windows 开发机、64 GiB 物理内存；Docker Desktop Linux 约 19.3 GiB；Linux SDK 10.0.402、运行时 10.0.13。复用已有系统临时目录 `C:/Users/Administrator/AppData/Local/Temp/fullnet-review-native-2gnp8ed6`，源码只读挂载，没有复制整仓或新增根目录 artifacts。原生构建限制 4 CPU / 12 GiB，运行限制 4 CPU / 8 GiB，并通过测试运行上下文互斥重型阶段。

同一路径此前的清理命令被执行策略以 `blocked by policy` 拒绝，原始命令保存在上一批验证记录；本轮未绕过该拒绝或换解释器重试。复用的构建目录仍需保留；不宣称清理完成。未外推为生产容量、Worker 原生运行或外部真实商户/邮件投递已验证。

最终原生构建、运行容器及该次专用 Testcontainers 均已退出回收；既有共享 SQL Server / MySQL 容器保留。

## 剩余三项失败的后续修复

用户在同一工作区授权继续修复，任务快照为 `assertion-fixes-20261019`，分支与 HEAD 未变化。本批生产改动仅为 `IdentityLogTimelineSources` 的装配幂等保护；另更新 Identity 注册测试和 Auditing 访问日志测试。

进一步最小复现发现：Identity 快照除了日志读取器，还遗漏了现有的 `IIndependentCurrentSessionAuthorization`；补齐装配预期后，重复装配确实注册了两个 Identity 日志读取器，不能仅修改预期数量。现改为复用模块私有的静态 Scoped 工厂描述符并按描述符去重，保留其他模块的时间线来源；测试明确验证外部读取器保留且 Identity 读取器恰好一个。没有修改读取器 SQL、权限、返回协议或生命周期。Auditing 的两行参数预期改为 24，并分别验证两行均带 `CorrelationJson` 参数。

缺陷证据：更新回归断言后的最小复现发现 3 项、2 失败、1 通过（审计已通过）；其中重复装配明确报告预期 1、实际 2。修复后相关两组测试 12/12 通过、0 跳过，Release 构建 0 警告、0 错误。中间一次编译暴露静态字段初始化顺序的可空告警，已调整字段位置修正，没有屏蔽告警。原始日志分别为系统临时目录中的 `fullnet-assertions-red.log`、`fullnet-assertions-focused.log`、`fullnet-assertions-focused-final.log`。

全量 Release Unit 复验：发现 8935 项，8934 通过、0 失败、1 跳过，耗时 9m11s，命令退出 0。TRX 逐项确认上述三个原失败用例均为 Passed；跳过项仅为 Windows 无法执行的 Linux FIFO 句柄回归。使用 MSTest 单 Worker，保留矩阵最低发现数，独立设置 15 分钟预算容纳完整串行套件，没有过滤失败用例。原始记录为系统临时目录 `fullnet-assertions-full.log` 和 `fullnet-layering-tests/assertions-unit-full.trx`。

API AOT/Trim 分析通过，0 警告、0 错误；API Native AOT 架构专项 73/73 通过、0 跳过，Release 构建 0 警告、0 错误；治理检查 85/85 通过、0 跳过。新鲜 Native AOT 发布产物为 139,702,000 bytes，发布日志的 9 条第三方告警全部通过仓库白名单校验；Linux Integration 测试宿主构建 0 警告、0 错误。SQL Server / MySQL 原生 API 关键流程 2/2 通过、0 跳过，耗时 2m58s，TRX 分别确认两种提供程序均为 Passed。原生构建与运行的整组命令退出 0。

本批集中验证完成，三个原失败用例在无过滤的全量 Unit 中均已通过。最终 `git diff --check` 返回 0，仅有 Git 换行转换提示；分支仍为 `main`、HEAD 保持文首基线，已检查工作区状态并保留此前全部修改。没有提交、推送或部署。原生构建、运行与该次专用数据库容器均已回收，既有共享数据库容器保留；复用临时构建目录仍按前述策略拒绝记录保留，未重试清理。

补充原生证据为系统临时目录的 `fullnet-assertions-native-build.log`、`fullnet-assertions-native-runtime.log`、`fullnet-review-native-2gnp8ed6/assertions-publish.log`、`fullnet-review-native-2gnp8ed6/assertions-test-build.log` 和 `fullnet-review-native-2gnp8ed6/test-build/bin/Full.NET.IntegrationTests/release/TestResults/assertions-native.trx`；其他门禁日志为 `fullnet-assertions-analyzers.log`、`fullnet-assertions-architecture.log`、`fullnet-assertions-governance.log`。

沿用用户授权的独立只读复核已检查本批三个代码文件，未发现实际回归或断言弱化：静态描述符不共享 Scoped 实例，仅去重 Identity 自身；外部读取器保留；注册数、解析数、实例身份与来源的检查均有效。审查未修改文件或运行构建。

补充阶段实际命令（全量命令由测试矩阵读取最低发现数，使用已有当前 Release 构建；原生构建复用上述只读源码挂载）：

```powershell
pnpm test:dotnet:unit -- --filter 'FullyQualifiedName~IdentityModuleRegistrationTests|FullyQualifiedName~AuditingAccessLogWritePathTests' --minimum-expected-tests 3
node $env:TEMP/fullnet-assertions-full.mjs
pnpm test:aot:analyzers
pnpm test:dotnet:architecture -- --selection api-native-aot
pnpm test:governance
node $env:TEMP/fullnet-review-native-2gnp8ed6/build-assertions.mjs
node $env:TEMP/fullnet-review-native-2gnp8ed6/verify-assertions.mjs
```

全量包装器通过 `withTestRun` 申请重型验收资源，实际启动 `dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --no-ansi --progress off --settings $env:TEMP/fullnet-layering-serial.runsettings --minimum-expected-tests 8576 --timeout 15m --report-trx --report-trx-filename assertions-unit-full.trx --results-directory $env:TEMP/fullnet-layering-tests`。该 runsettings 仅将 MSTest 并行 Worker 设为 1，没有忽略用例。原生构建限制为 6 CPU / 12 GiB，原生运行仍为 4 CPU / 8 GiB；保留前述清理策略拒绝记录，不另建源码副本。
