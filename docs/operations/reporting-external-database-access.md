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

连接失败、探活失败和查询失败只返回固定摘要，不返回驱动异常。测试摘要也只持久化固定消息。外部连接的每进程并发上限为 `MaxConcurrentSessions`（默认 16），超过 `AdmissionTimeoutSeconds`（默认 1 秒）仍无空位时快速失败。配置变更后重启 API 进程。

迁移 `237_ReportingDataSourceTestMessageRedaction` 将既有失败测试摘要覆盖为固定消息。升级期间先运行 Migrator，再开放 API；对旧备份中的历史摘要按组织的数据清理策略处理。
