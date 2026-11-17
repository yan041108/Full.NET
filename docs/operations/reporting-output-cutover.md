# 报表输出格式切版

本流程适用于使用 `fullnet-api` 和 `fullnet-worker` Helm Release 的部署。`Invoke-FullNetRelease.ps1 -ReportingOutputCutover` 在任何 Helm 发布阶段前核对旧宿主和目标数据库。

1. 核对候选镜像、实际 Helm values、集群 context、命名空间和发布使用的主库。为检查命令准备仅能读取报表导出任务表和租户资源文件元数据表的账号。执行期间暂停会恢复副本的 GitOps、伸缩器和外部发布流程。其他命名空间、独立宿主和外部生产者如连接同库，也须先停稳。
2. 在入口停止新导出准入，等待已进入的请求结束。支持导出准入开关的旧版可将 Helm `api.reportingExportAdmissionEnabled` 设为 `false`，先以相同镜像更新 API，并在滚动完成后核对所有实例的导出 POST 均返回 503 和 `reporting.export_task.admission_paused`。独立部署可设置 `FullNet__Reporting__Export__AdmissionEnabled=false`。此前仍在运行的请求须等待结束，旧版不支持该开关时继续从入口阻止请求。保持旧 Worker 的 `ExecutionEnabled` 启用，用原任务查询、取消或下载核对进度和结果。将旧 API 副本降为零后等待 Pod 正常终止，再用同一主库检查全部存量。两库的只读查询位于 `eng/deploy/reporting-cutover/`。未知状态及残留租约也阻断切版。
3. 排空后将旧 Worker 副本降为零，等待 Pod 正常终止，移除两套旧 Deployment 的 HPA。节点失联或无法证明旧进程退出时暂停切版。遇到旧格式文件上传结果未知的任务，保留任务、原文件和相关证据，暂缓切版并结合[升级说明](application-upgrade.md)核对处置范围。
4. 使用支持导出准入开关的候选镜像及 Chart 执行发布。`KubeContext` 同时用于门禁和 Helm，数据库参数须指向该部署实际使用的主库。SQL Server 使用 ODBC `sqlcmd`，按环境提供集成认证或受控的 `SQLCMDUSER`、`SQLCMDPASSWORD`。MySQL 使用受控 login path 及服务器 CA。两种客户端均验证数据库服务器证书。

   ```powershell
   $release = @{
       Namespace = $ReleaseNamespace
       ImageTag = $CandidateImageTag
       ChartPath = $CandidateChart
       KubeContext = $ReleaseContext
       ValuesFile = $ApplicationValues
       ProviderValuesFile = $ActualProviderValues
       EdgeAndDpValuesFile = $ActualEdgeValues
       ApiDatabaseSecretName = $ApiDatabaseSecret
       WorkerDatabaseSecretName = $WorkerDatabaseSecret
       MigratorDatabaseSecretName = $MigratorDatabaseSecret
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

脚本要求两个旧 Deployment 已确认零副本、关联 ReplicaSet 已停稳、相关 Pod 均已退出，主库的报表阻断资源数也须为零。未完成任务、残留租约、未确认上传和未绑定的就绪文件均会阻断。同一租户及任务只计一次；终态或原任务缺失时，未处置的文件依然会阻断。检查使用 Helm 实例标签、控制器 UID 和现存 ReplicaSet 生成的 Pod 名称前缀。操作账号需能列出命名空间内的 Deployment、ReplicaSet、Pod 和 HPA。查询前后均须停稳，且两个旧 Deployment 的 UID 和 generation 须分别一致。查询期间发生过重新伸缩或重建时，先重新确认停写和排空，再从头执行发布门禁。发布协调者须在检查与发布期间维持上述停写条件，直至新版本就绪。

Migrator 每次发布创建带 Helm 修订号的 Job，与其 ConfigMap 和 ServiceAccount 一起由 Helm 创建。发布脚本等待本次 Job 完成才启动 Worker 阶段，随后将 API 以 `api.reportingExportAdmissionEnabled=false` 发布并等待就绪。单独调用 Helm 时，Migrator 阶段也须传入 `--wait --wait-for-jobs`，API 阶段须显式暂停导出准入。

三个数据库 Secret 指向同一业务主库，分别使用宿主所需权限及与 Chart 预算一致的最大连接池大小。未传角色专用参数时，该角色使用 values 中的 `database.connectionSecretName`。

## 已完成导出的复核

迁移 364 创建发布版本授权表。已有定义级授权继续覆盖该定义的发布版本。若需要缩小到单一版本，先核对租户、定义和发布版本，由具备 `reporting.definitions.grant_tenants` 权限的 Host 操作员设置精确授权，再停用对应定义级授权。仅撤销版本级授权时，仍有效的定义级授权可以继续提供访问。

从原租户上下文使用仍然有效的导出创建者会话，核对旧任务的文件引用、行数及下载内容。使用无授权会话验证下载被拒绝。若原会话已失效，保留任务和文件证据，等待放行后按当前授权重新发起导出。迁移后的下载检查应覆盖原有对象存储和相应的加密材料。

放行前，通过受控入口确认所有新 API 实例的导出 POST 返回 503 和 `reporting.export_task.admission_paused`。完成复核后，使用同一个候选 Chart 和 API Release 的现有配置，只滚动更新 API。若使用 GitOps 管理，将相同值同步至其权威配置。

```powershell
helm upgrade fullnet-api $CandidateChart --namespace $ReleaseNamespace --kube-context $ReleaseContext `
    --reuse-values --set api.reportingExportAdmissionEnabled=true --wait --timeout 10m
```

更新完成后，恢复入口并用获授权的测试会话验证新导出受理。放行时无需再次执行三阶段发布。

## 上传结果未知时的取证

门禁报出阻断数量后，可在同一主库用同一只读账号列出阻断项。每项返回租户和任务资源 UUID 以及 `reasons` 数组，覆盖已找不到原任务的文件。同一任务可同时有多个原因。

| 原因 | 核对内容 |
| --- | --- |
| `task_unfinished` | 任务仍在排队、处理中，或状态值无法识别。 |
| `task_has_lease` | 任务仍有租约标识或到期时间，包含已到期的残留租约。 |
| `pending_upload` | Files 仍有待确认的上传。 |
| `ready_file_unbound` | 已就绪文件没有被同一租户的原任务绑定。 |

默认列出 25 项，`-Limit` 可设为 1～100。`hasMore` 为 `true` 时，将本页最后一项的 `tenantId` 与 `taskId` 传给 `-AfterTenantId` 和 `-AfterTaskId` 读取下一页。全程使用同一主库，并维持停写条件。需要核对单个对象时继续读取下面的证据。取证完成后仍须重新运行切版门禁，确认阻断项归零。

```powershell
$query = @{ Provider = 'SqlServer'; Server = $ReportingPrimary; Database = $ApplicationDatabase }
$page = ./eng/deploy/reporting-cutover/Get-ReportingCutoverBlockers.ps1 @query | ConvertFrom-Json
# 核对 $page.items 后，如仍有后续结果则读取下一页。
if ($page.hasMore) {
    $last = $page.items[-1]
    $page = ./eng/deploy/reporting-cutover/Get-ReportingCutoverBlockers.ps1 @query `
        -AfterTenantId $last.tenantId -AfterTaskId $last.taskId | ConvertFrom-Json
}
```

保持发布暂停，停止该库的旧写入方，记录已知的租户 UUID、任务 UUID 与故障时间。使用受控的数据库只读账号，在同一可写主库上核对一个任务。命令会拒绝只读数据库实例。SQL Server 与 MySQL 沿用上面的 TLS 及认证配置，MySQL 额外传入 `MySqlLoginPath`、`MySqlCaFile` 和实际端口。

```powershell
./eng/deploy/reporting-cutover/Get-ReportingCutoverEvidence.ps1 `
    -Provider SqlServer -Server $ReportingPrimary -Database $ApplicationDatabase `
    -TenantId $AffectedTenantId -TaskId $AffectedTaskId
```

只读证据依赖 `fn_reporting_export_task` 的 `TenantId`、`Id`、`StatusKey`、`OutputFileId`、`RowCount`、`LeaseId`、`LeaseExpiresAtUtc`，以及 `fn_files_tenant_resource_file` 的 `TenantId`、`ResourceId`、`OwnerModuleKey`、`StatusKey`、`Id`、`CreatedAtUtc`、`OriginalFileName`、`SizeBytes`、`ContentHash`。查询按租户、任务和 `reporting` 所有权限制文件。输出包含最新就绪文件的标识、大小和记录哈希，不包含文件名、存储键或文件内容。`hasRowIdentityPrefix` 仅说明文件名是否有新版前缀。

需要逐份核对时，对同一任务运行 `Get-ReportingCutoverFiles.ps1`。它列出待确认和就绪文件的 ID、状态、记录字节数、SHA-256、创建时间及是否被原任务绑定，原任务缺失时仍可列出相应文件。当 `hasMore` 为 `true` 时，用末项的 `fileId` 作为 `-AfterFileId`，保持相同租户和任务继续读取。默认每页 25 项，`-Limit` 可设为 1～100。保持停写并使用相同的可写主库和只读账号。

```powershell
$taskQuery = $query + @{ TenantId = $AffectedTenantId; TaskId = $AffectedTaskId }
$filePage = ./eng/deploy/reporting-cutover/Get-ReportingCutoverFiles.ps1 @taskQuery | ConvertFrom-Json
if ($filePage.hasMore) {
    $filePage = ./eng/deploy/reporting-cutover/Get-ReportingCutoverFiles.ps1 @taskQuery `
        -AfterFileId $filePage.items[-1].fileId | ConvertFrom-Json
}
```

- 已就绪但任务未绑定时，保留原对象和文件引用。使用受控对象存储路径核验实际字节数和 SHA-256，对照旧 Worker 记录确认原始导出和行数。`recordedRowCount` 是当前任务快照，不能作为尚未绑定文件的行数。
- Worker 发现已存在的原输出并尝试绑定时，若租约条件更新未命中，本轮结束。后续有效领取会重新核对任务和原文件。只要任务仍有未完成状态或残留租约，切版门禁继续阻断。
- 对于只有一个旧格式就绪文件的未绑定任务，可保存上述 JSON，并从受控存储路径取回该文件的原始字节。运行以下命令会核对文件大小、SHA-256 和 Full.NET 固定 XLSX 工作表结构，返回去掉表头的数据行数。将结果和旧 Worker 记录一起交给报表与文件负责人复核，命令只读取本地证据和工作簿。

  ```powershell
  ./eng/deploy/reporting-cutover/Measure-ReportingCutoverWorkbook.ps1 `
      -EvidencePath $SavedEvidenceJson -WorkbookPath $OriginalWorkbook
  ```

- 原任务尚未绑定且存在多个就绪文件时，Worker 保留这些文件和待恢复任务，等待核对原证据。创建时间靠后的文件仍需核对。摘要不符或归属不清时也应保留证据。
- 未找到就绪文件时，结合 pending 记录及对象存储和 Worker 日志核对上传结果。缺失元数据或已到期的租约本身不能证明上传未发生。
- 取证结果的 `taskExists` 说明是否找到原任务，`unresolvedFileCount` 统计该租户和任务下仍待确认或尚未绑定的就绪文件。原任务缺失时，只要还有归属匹配的文件就会返回文件证据，任务状态和行数为空。原任务与文件均缺失时，命令返回错误。
- 报表任务仍可恢复且 Files 保留 pending 时，后续领取会等待上传确认为就绪、释放或清理。在待确认期内，报表 Worker 保留原任务和文件。队列排空前需确认 Files 的恢复处理已收敛。
- 由报表与文件负责人记录任务终态、对象处置及授权复核。仍无法确认的任务继续阻断切版。保留数据并维持隔离，避免重跑生成、手改任务状态或补写不可靠的行数。按批准的恢复方案处置后，重新检查排空门禁。
