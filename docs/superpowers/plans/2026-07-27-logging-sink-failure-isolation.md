# Logging Sink Failure Isolation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 确保普通与高优先级日志后台 Worker 在单条事件调用内部 Sink 失败后继续消费后续事件，并把失败事件计入既有丢弃指标。

**Architecture:** 保持现有双通道、有界队列、非阻塞调用方和共享退出预算不变。只把异常隔离边界从整个消费循环收紧到单条事件：内部 Sink 抛异常时记录 SelfLog、递增该通道 dropped 计数，然后继续处理下一条事件；队列基础设施异常仍由外层保护捕获。

**Tech Stack:** .NET 10、Serilog 4.4、`BlockingCollection<LogEvent>`、MSTest、OpenTelemetry Metrics

## Global Constraints

- 普通日志与 `Error/Critical` 必须继续使用独立容量，调用方不得因 Sink 故障同步等待。
- 单条 Sink 失败只允许丢弃当前事件，不得永久停止对应通道，也不得把失败事件重新排队造成无限循环。
- 失败事件必须计入既有 `fullnet.logging.events.dropped{channel}`，不得新增高基数标签。
- SelfLog 只记录异常类型，不写异常消息、日志正文、租户、用户、Token、Cookie 或连接串。
- 本切片不引入磁盘 Spool、跨重启重放、外部投递确认或审计语义；这些仍属于后续 Task 8B。
- 代码标识符使用英文；手写注释使用中文解释异常隔离边界与敏感信息风险。

---

### Task 1: 单条 Sink 故障隔离

**Files:**
- Modify: `tests/Full.NET.UnitTests/Hosting/HighPriorityLoggingTests.cs`
- Modify: `src/BuildingBlocks/Full.NET.Hosting/Observability/FullNetBoundedAsyncSink.cs`

**Interfaces:**
- Consumes: `ILogEventSink.Emit(LogEvent)`、`FullNetAsyncLogMonitor`
- Produces: 使用 `AuditTo` 传播内部失败的 Sink 包装器
- Produces: 单条 Sink 异常后继续消费的 `FullNetBoundedAsyncSink`，失败事件累计到 `DroppedMessagesCount`

- [x] **Step 1: 写普通与高优先级通道的失败回归测试**

  在 `HighPriorityLoggingTests` 增加两个测试。`ThrowOnceSink` 第一次 `Emit` 抛 `InvalidOperationException` 并发出 attempted 信号，后续事件委托给 `CollectingSink`：

  ```csharp
  [TestMethod]
  public void General_worker_continues_after_one_sink_failure()
  {
      using var monitors = new FullNetLoggingMonitors();
      var delivered = new CollectingSink();
      var generalSink = new ThrowOnceSink(delivered);
      using var logger = CreateLogger(
          monitors,
          generalSink,
          new CollectingSink(),
          generalBufferSize: 4,
          highPriorityBufferSize: 4);

      logger.Information("discard first general event");
      Assert.IsTrue(generalSink.WaitUntilAttempted());
      logger.Information("deliver second general event");

      Assert.IsTrue(delivered.WaitForCount(1));
      Assert.AreEqual(
          "deliver second general event",
          delivered.Events.Single().RenderMessage());
      Assert.AreEqual(1, monitors.General.Snapshot.DroppedMessagesCount);
  }
  ```

  高优先级测试使用同一结构，写入两条 `Error`，并断言 `monitors.HighPriority.Snapshot.DroppedMessagesCount == 1`。

- [x] **Step 2: 运行聚焦测试并确认 RED**

  Run:

  ```powershell
  dotnet build tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --no-restore
  dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --no-ansi --progress off --filter "FullyQualifiedName~Full.NET.UnitTests.Hosting.HighPriorityLoggingTests" --minimum-expected-tests 11
  ```

  Actual: 第一轮两个新增测试都送达第二条事件，但 dropped 仍为 0，证明内部 `WriteTo` Logger 吞掉了真实 Sink 异常；把内部包装单独切换为 Audit 语义后，两个测试都因第二条事件无法到达而失败，证明异常传播后现有外层 catch 会终止 Worker。既有 9 项持续通过。

- [x] **Step 3: 传播内部失败并将异常隔离收紧到单条事件**

  `FullNetLoggingPipeline` 的两个内部 Sink 配置从
  `Action<LoggerSinkConfiguration>`/`WriteTo` 改为
  `Action<LoggerAuditSinkConfiguration>`/`AuditTo`。该 Audit 只用于后台
  Worker 与实际 Sink 之间的失败传播，不改变业务审计边界，也不会把
  调用方改为同步写日志。

  `FullNetBoundedAsyncSink.Consume()` 保留队列枚举的外层保护，并把内部 Sink 调用改为：

  ```csharp
  foreach (var logEvent in _queue.GetConsumingEnumerable())
  {
      try
      {
          _sink.Emit(logEvent);
      }
      catch (Exception exception)
      {
          Interlocked.Increment(ref _droppedMessagesCount);
          SelfLog.WriteLine(
              "Full.NET asynchronous logging sink rejected one event with {0}; "
              + "the worker will continue.",
              exception.GetType().FullName);
      }
  }
  ```

  SelfLog 禁止包含 `exception.Message` 或 `logEvent`，避免在降级路径泄露敏感正文。

- [x] **Step 4: 运行聚焦测试并确认 GREEN**

  Run:

  ```powershell
  dotnet build tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --no-restore
  dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --no-ansi --progress off --filter "FullyQualifiedName~Full.NET.UnitTests.Hosting.HighPriorityLoggingTests" --minimum-expected-tests 11
  ```

  Actual: **11/11**，失败 0、跳过 0；两个通道都在第一次 Sink 失败后投递第二条事件，dropped 各为 1。

### Task 2: 运维事实与验证收口

**Files:**
- Modify: `docs/operations/logging-degraded-mode.md`
- Modify: `docs/superpowers/plans/2026-07-18-architecture-hardening.md`
- Create: `docs/verification/logging-sink-failure-isolation-2026-07-27.md`
- Modify on final main sync only: canonical Unit threshold sources if the actual discovered total changes

**Interfaces:**
- Consumes: Task 1 的事件级异常隔离与新鲜测试输出
- Produces: 平台 Sink 故障的当前处置边界、剩余 Task 8B 范围与可定位验证证据

- [x] **Step 1: 更新运维与硬化计划**

  文档必须明确：

  - 单条 Sink 异常会丢弃当前事件、递增对应通道 dropped，并继续消费后续事件。
  - SelfLog 只暴露异常类型；平台恢复仍需通过 dropped 增量与队列深度确认。
  - 连续失败不会阻塞请求线程，但会持续丢弃；当前仍没有持久 Spool 或投递确认。
  - Task 8B 剩余磁盘 Spool、容量/保留/加密、磁盘满、跨重启重放和至少一次重复语义不变。

- [x] **Step 2: 写验证记录**

  `logging-sink-failure-isolation-2026-07-27.md` 记录基线 9/9、首轮 RED 的 dropped 假绿、异常传播后的两个后续事件超时、GREEN 11/11、完整门禁和未完成项。未执行的磁盘满、跨重启和外部平台测试必须明确列为未验证。

- [x] **Step 3: 同步最终 main 并执行完成门禁**

  在 Jobs active lease renewal → Files RootPath → IdentityOptions 启动校验队列全部完成后，把最新 `main` 合入本分支，解决 canonical 文档差异，然后运行：

  ```powershell
  dotnet build Full.NET.slnx -c Release --no-restore
  dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --no-ansi --progress off --minimum-expected-tests 406
  dotnet tests/Full.NET.CompatibilityTests/bin/Release/net10.0/Full.NET.CompatibilityTests.dll --no-ansi --progress off --minimum-expected-tests 7
  dotnet tests/Full.NET.ArchitectureTests/bin/Release/net10.0/Full.NET.ArchitectureTests.dll --no-ansi --progress off --minimum-expected-tests 49
  pnpm test:openapi
  pnpm test:openapi:breaking -- --base-ref main
  pnpm test:governance
  pnpm test:skills
  pnpm test:naming
  pnpm test:workspace
  pnpm test:integration:tooling
  pnpm test:integration:partitions
  pnpm test:integration:full
  git diff --check
  ```

  Actual: 所有命令退出码 0；Release 0 warning/0 error；Unit / Compatibility / Architecture 为 **406/7/49**，Logging 聚焦 **11/11**；完整 Integration **189/189**，失败 0、跳过 0、stderr 0，耗时 **31m23s**。Hosting 是共享基础设施，因此完整 Integration 已顺序独占 Docker 执行，结束后容器自动归零。

- [ ] **Step 4: 规则与 Skill 复盘、提交、合并清理**

  读取并执行 `rules/rule-evolution.md` 与 `rules/skill-evolution.md`。确认只暂存本切片 owned 文件，提交：

  ```powershell
  git commit -m "fix: keep logging workers alive after sink failures"
  ```

  在主工作树确认 `main` 未漂移后合并；合并后重跑聚焦 11 项与 Governance/Skill/workspace/diff check，最后删除 `codex/logging-sink-failure-isolation` 分支、Git worktree 注册和物理目录。

## Self-Review

- Spec coverage: 两个通道、失败计数、后续消费、非阻塞和 SelfLog 敏感信息边界均有对应测试或实现步骤；持久化能力明确不在本切片。
- Placeholder scan: 所有行为、文件、命令与停止条件均已明确；预计 Unit 门槛为 406，最终同步时只允许按实际发现数上调。
- Type consistency: 测试与实现都使用既有 `DroppedMessagesCount`、`FullNetAsyncLogMonitor`、`FullNetBoundedAsyncSink` 和 `ILogEventSink.Emit(LogEvent)`，未增加公共 API。
