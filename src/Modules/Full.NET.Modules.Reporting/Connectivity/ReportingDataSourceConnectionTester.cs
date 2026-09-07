using Full.NET.Data.Abstractions;
using Full.NET.Modules.Reporting.Domain;
using Full.NET.Modules.Reporting.Persistence;

namespace Full.NET.Modules.Reporting.Connectivity;

/// <summary>在受控白名单提供程序上执行只读连接测试；禁止接受任意连接串。</summary>
/// <param name="connections">外部数据库连接工厂。</param>
internal sealed class ReportingDataSourceConnectionTester(IExternalDatabaseConnectionFactory connections)
{
    /// <summary>测试数据源连接并执行 <c>SELECT 1</c> 探活。</summary>
    /// <param name="record">已持久化的数据源行。</param>
    /// <param name="password">解保护后的明文密码。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>测试结果消息。</returns>
    public async Task<(bool Succeeded, string Message)> TestAsync(
        ReportingDataSourceRecord record,
        string password,
        CancellationToken cancellationToken = default)
    {
        var request = ReportingExternalConnectionMapper.TryCreate(
            record,
            password,
            "Full.NET-Reporting-Test");
        if (request is null)
        {
            return (false, "Unsupported provider key.");
        }

        var opened = await connections.OpenAsync(request, cancellationToken).ConfigureAwait(false);
        if (!opened.Succeeded || opened.Session is null)
        {
            return (false, opened.ErrorMessage ?? "Failed to open reporting data source.");
        }

        await using (opened.Session)
        {
            var probe = await opened.Session.ExecuteScalarAsync(
                    "SELECT 1",
                    ReportingExecutionPolicy.ConnectionTimeoutSeconds,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!probe.Succeeded)
            {
                return (false, probe.ErrorMessage ?? "Failed to probe reporting data source.");
            }

            if (probe.Value is null)
            {
                return (false, "Connection opened but probe query returned no result.");
            }

            return (true, "Connected successfully. Ensure the account is read-only.");
        }
    }
}
