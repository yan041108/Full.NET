# 企业申请独立 Worker 进程恢复（2026-11-03）

基线 `4078eab73ad83294f8443b1244edf667e2cebc50`，对应产品化计划 §8.39。测试宿主复用应用模板的 `FULLNET_APP_WORKER` 静态装配，直接链接官方 Worker 源码并装配样例模块；无整仓副本，官方 Host 不引用业务样例。真实 Outbox、业务处理器、Workflow 和终态接收点均未改动。

SQL Server/MySQL 两项本地首轮通过，0 跳过，63.8 秒；测试宿主及集成项目定向构建 0 警告/错误。每库三个真实 `dotnet` 子进程，在流程启动已提交、业务终态已回写后且消息未确认时，各终止一次当前测试拥有的进程。新进程等待 10 秒租约自然恢复，未修改锁期或直接调用恢复处理器。原启动及终态消息最终均有 ProcessedAtUtc；流程、待办与批准事实不重复，回写后消息重放不再增加业务版本，两库最终均为 Approved / 版本 3。

MySQL 进程为 12316→9932→43052；SQL Server 为 28720→44784→29920。原始消息 ID、业务/实例 ID、提交版本、进程日志与结果保存在 `C:\Users\dai27\AppData\Local\Temp\fullnet-f10-process-evidence-1457e6c9c70a4b678b6f5d511b00e7e3`。故障装饰器只在测试项目中存在，先调用真实处理器，再原子写出检查点并等待；父测试只按自己持有的进程句柄终止，不扫描或接管其他进程。

普通构建集成项目会构建该测试宿主；使用 `BuildProjectReferences=false` 的集中构建需先显式构建 `tests/Tools/Full.NET.EnterpriseRequest.WorkerFixture/Full.NET.EnterpriseRequest.WorkerFixture.csproj`。聚焦筛选器 `enterprise-request-worker-process-recovery` 最少 2 项。默认运行构建目录为该项目 `bin/Release/net10.0`，隔离输出时使用 `FULLNET_ENTERPRISE_WORKER_FIXTURE_OUTPUT` 指向现行构建。

已有页面提交、批准/驳回/退回、通知及双库真实撤权证据继续复用，F10-R1 当前差集关闭。本次为 Windows 上 JIT 独立进程验证，未运行全量、Native 发布、MySQL 浏览器或容量认证。六个子进程均由本测试终止并回收；临时密钥/文件根的清理被自动审批审查以“blocked by policy”拒绝，尚未清理，不换实现绕过；原始日志和结果保留。
