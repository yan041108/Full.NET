# 报表输出格式切版

本流程适用于使用 `fullnet-api` 和 `fullnet-worker` Helm Release 的部署。`Invoke-FullNetRelease.ps1 -ReportingOutputCutover` 在任何 Helm 发布阶段前核对旧宿主和目标数据库。

1. 核对候选镜像、实际 Helm values、集群 context、命名空间和发布使用的主库。为检查命令准备仅能读取报表导出任务表的账号。执行期间暂停会恢复副本的 GitOps、伸缩器和外部发布流程。其他命名空间、独立宿主和外部生产者如连接同库，也须先停稳。
2. 在入口停止新导出准入，等待已进入的请求结束。将旧 API 副本降为零，等待 Pod 正常终止。保持旧 Worker 处理存量，使用同一主库核对队列和处理中的任务。两库的只读查询位于 `eng/deploy/reporting-cutover/`。未知状态及残留租约也阻断切版。
3. 排空后将旧 Worker 副本降为零，等待 Pod 正常终止，移除两套旧 Deployment 的 HPA。节点失联或无法证明旧进程退出时暂停切版。遇到旧格式文件上传结果未知的任务，保留任务、原文件和相关证据，暂缓切版并结合[升级说明](application-upgrade.md)核对处置范围。
4. 使用下列参数执行发布。`KubeContext` 同时用于门禁和 Helm，数据库参数须指向该部署实际使用的主库。SQL Server 使用 ODBC `sqlcmd`，按环境提供集成认证或受控的 `SQLCMDUSER`、`SQLCMDPASSWORD`。MySQL 使用受控 login path 及服务器 CA。两种客户端均验证数据库服务器证书。

   ```powershell
   $release = @{
       Namespace = $ReleaseNamespace
       ImageTag = $CandidateImageTag
       KubeContext = $ReleaseContext
       ValuesFile = $ApplicationValues
       ProviderValuesFile = $ActualProviderValues
       EdgeAndDpValuesFile = $ActualEdgeValues
       ReportingOutputCutover = $true
       ReportingDatabaseProvider = $ActualProvider # SqlServer 或 MySql
       ReportingDatabaseServer = $ReportingPrimary
       ReportingDatabaseName = $ApplicationDatabase
   }
   if ($ActualProvider -eq 'MySql') {
       $release.ReportingMySqlLoginPath = $ReaderLoginPath
       $release.ReportingMySqlCaFile = $DatabaseCaFile
       $release.ReportingMySqlPort = $DatabasePort
   }
   ./eng/deploy/Invoke-FullNetRelease.ps1 @release
   ```

脚本要求两个旧 Deployment 已确认零副本，相关 Pod 均已退出，主库阻断任务数为零。查询结束后会再次核对旧宿主状态。发布协调者须在检查与发布期间维持上述停写条件，直至新版本就绪。
