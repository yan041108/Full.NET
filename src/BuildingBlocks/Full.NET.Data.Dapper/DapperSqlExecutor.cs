using System.Diagnostics;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using global::Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Data.Dapper;

internal sealed class DapperSqlExecutor(
    DbSession session,
    ICurrentTenant currentTenant,
    IOptions<DatabaseOptions> options,
    ILogger<DapperSqlExecutor> logger) : IQueryExecutor, ICommandExecutor
{
    private readonly DatabaseOptions _options = options.Value;

    public async Task<T?> QuerySingleOrDefaultAsync<T>(
        SqlStatement statement,
        object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var command = await CreateCommandAsync(
            statement,
            parameters,
            cancellationToken).ConfigureAwait(false);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var connection = await session
                .GetOpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
            return await connection.QuerySingleOrDefaultAsync<T>(command).ConfigureAwait(false);
        }
        finally
        {
            LogExecution(statement, stopwatch);
        }
    }

    public async Task<IReadOnlyList<T>> QueryAsync<T>(
        SqlStatement statement,
        object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var command = await CreateCommandAsync(
            statement,
            parameters,
            cancellationToken).ConfigureAwait(false);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var connection = await session
                .GetOpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
            var rows = await connection.QueryAsync<T>(command).ConfigureAwait(false);
            return rows.AsList();
        }
        finally
        {
            LogExecution(statement, stopwatch);
        }
    }

    public async Task<int> ExecuteAsync(
        SqlStatement statement,
        object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var command = await CreateCommandAsync(
            statement,
            parameters,
            cancellationToken).ConfigureAwait(false);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var connection = await session
                .GetOpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
            return await connection.ExecuteAsync(command).ConfigureAwait(false);
        }
        finally
        {
            LogExecution(statement, stopwatch);
        }
    }

    private Task<CommandDefinition> CreateCommandAsync(
        SqlStatement statement,
        object? values,
        CancellationToken cancellationToken)
    {
        SqlScopeGuard.Validate(statement, currentTenant);

        var parameters = new DynamicParameters(values);
        if (statement.Scope == SqlDataScope.TenantRequired)
        {
            parameters.Add("TenantId", currentTenant.Id!.Value);
        }

        return Task.FromResult(new CommandDefinition(
            statement.Text,
            parameters,
            session.Transaction,
            _options.CommandTimeoutSeconds,
            cancellationToken: cancellationToken));
    }

    private void LogExecution(SqlStatement statement, Stopwatch stopwatch)
    {
        stopwatch.Stop();
        DapperLog.StatementExecuted(
            logger,
            statement.Name,
            _options.Provider,
            stopwatch.Elapsed.TotalMilliseconds);
    }
}
