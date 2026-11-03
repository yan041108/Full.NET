# 报表外部数据库连接授权

报表数据源管理中的主机、端口和证书信任选项来自业务请求。部署方必须通过 `FullNet:ExternalDatabaseAccess:AllowedDestinations` 显式批准外部目标；未配置时所有外部连接均拒绝。目标匹配提供程序、完整主机名和端口，不接受通配符。生产网络仍应使用出站防火墙限制可达地址。

例如，授权一个 SQL Server 报表只读库：

```json
{
  "FullNet": {
    "ExternalDatabaseAccess": {
      "MaxConcurrentSessions": 16,
      "AdmissionTimeoutSeconds": 1,
      "AllowedDestinations": [
        {
          "Provider": "SqlServer",
          "Host": "report-db.example.com",
          "Port": 1433,
          "AllowUntrustedCertificate": false
        }
      ]
    }
  }
}
```

`Provider` 可选 `SqlServer`、`MySql`。数据库账号应由目标库授予只读权限。SQL Server 默认验证服务端证书；只有目标授权中 `AllowUntrustedCertificate=true` 且数据源中启用 `TrustServerCertificate` 时才允许跳过验证。建议为目标配置可信证书。主机名解析可能变化，因此出站防火墙应同步限制目标 IP 范围。

MySQL 外部连接固定使用 `SslMode=VerifyFull`，要求 TLS、可信证书及主机名匹配；`AllowUntrustedCertificate` 不适用于 MySQL。目标库若使用私有 CA，应把 CA 纳入宿主信任链后再授权连接。

连接失败、探活失败和查询失败只返回固定摘要，不返回驱动异常。测试摘要也只持久化固定消息。外部连接的每进程并发上限为 `MaxConcurrentSessions`（默认 16），超过 `AdmissionTimeoutSeconds`（默认 1 秒）仍无空位时快速失败。API 与 Worker 都要配置获准目标，配置变更后重启对应进程；两者共享报表密码保护使用的 Data Protection Key Ring 和应用名。

迁移 `237_ReportingDataSourceTestMessageRedaction` 将既有失败测试摘要覆盖为固定消息。升级期间先运行 Migrator，再开放 API；对旧备份中的历史摘要按组织的数据清理策略处理。

## 租户发布与异步导出

数据源、定义编辑和版本发布由 Host 管理。发布不自动向租户共享：在 Vue 报表定义页打开“租户发布授权”，选择活动租户并授权。数据源属于特定租户时，只能授权给该租户；Host 数据源也必须逐租户显式授权。停用的数据源或定义不出现在租户发布目录。

Host 使用 `reporting.definition_grants.read` 和 `reporting.definition_grants.manage` 读取与修改授权；页面选择目标租户还需要 `tenancy.tenants.read`。新授权提交 `expectedVersion=0`，更新或撤销提交读取的当前版本，冲突返回 409，不覆盖其他管理员的修改。

租户页面使用 `reporting.published_definitions.read` 选择发布版本，`reporting.export_tasks.create/read/download` 分别控制排队、读取任务和下载。列权限仍由发布版本的布局决定。主要接口为：

| 用途 | 接口 |
| --- | --- |
| Host 授权分页 | `GET /api/v1/reporting/definitions/{definitionId}/tenant-grants` |
| Host 启用或撤销 | `PUT /api/v1/reporting/definitions/{definitionId}/tenant-grants/{tenantId}` |
| 当前租户发布目录 | `GET /api/v1/reporting/published-definitions` |
| 排队导出与任务列表 | `POST/GET /api/v1/reporting/export-tasks` |
| 本人结果下载 | `GET /api/v1/reporting/export-tasks/{taskId}/download` |

创建返回 `queued`，由 Worker 领取并推进为 `processing`、`succeeded` 或 `failed`。页面有界刷新当前页，终态停止；隐藏时暂停，上下文变化取消旧读取，超时或预算耗尽后可手动刷新恢复。现行任务没有结果有效期字段，页面不推算未知进度或过期时间。

任务固定发布版本、原会话和创建时可见列权限。每页取数、上传、完成及下载重新校验当前授权；后来增加的权限不扩大旧结果。注销、切换原会话租户、原会话失效、撤销发布或字段权限均可能使旧任务或下载被拒绝。下载只交付本人任务，超级管理员也不能下载同租户其他人的结果。API Key 和签名主体不能替代原用户会话创建该任务。

先运行 Migrator 的配对迁移 `353_ReportingTenantPublication` 及新版 Baseline。导航目录 Baseline 第 3 版修复系统分组资格，并移除早期误挂到工作台的导航动作；自定义目录和业务页面权限保留。API/Worker 不播种。历史导出缺少原会话绑定时失败关闭，保留历史记录和文件引用，不补造会话或清空存量文件。
