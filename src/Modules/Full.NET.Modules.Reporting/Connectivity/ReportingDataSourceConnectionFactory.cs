using Full.NET.Data.Abstractions;
using Full.NET.Modules.Reporting.Persistence;

namespace Full.NET.Modules.Reporting.Connectivity;

/// <summary>从受控数据源记录打开外部只读会话；具体驱动由数据边界创建。</summary>
/// <param name="connections">外部数据库连接工厂。</param>
internal sealed class ReportingDataSourceConnectionFactory(IExternalDatabaseConnectionFactory connections)
{
    /// <summary>打开外部只读会话。</summary>
    /// <param name="record">已持久化的数据源行。</param>
    /// <param name="password">解保护后的明文密码。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>打开结果；失败时不抛出驱动异常。</returns>
    public Task<ExternalDatabaseSessionResult> OpenAsync(
        ReportingDataSourceRecord record,
        string password,
        CancellationToken cancellationToken)
    {
        var request = ReportingExternalConnectionMapper.TryCreate(
            record,
            password,
            "Full.NET-Reporting-Execute");
        if (request is null)
        {
            return Task.FromResult(
                new ExternalDatabaseSessionResult(false, "Unsupported provider key.", null));
        }

        return connections.OpenAsync(request, cancellationToken);
    }
}
