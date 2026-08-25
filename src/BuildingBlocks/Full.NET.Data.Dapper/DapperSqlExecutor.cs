using System.Data;
using System.Diagnostics;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using global::Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Data.Dapper;

/// <summary>
/// Dapper SQL 执行器，实现 <see cref="IQueryExecutor"/>、<see cref="ICommandExecutor"/> 与
/// <see cref="IMultiResultQueryExecutor"/>，是所有 SQL 语句的统一执行入口。
/// </summary>
/// <remarks>
/// <para>关键不变量：每次执行前均通过 <see cref="SqlScopeGuard.Validate"/> 进行租户范围与绑定校验，
/// 这是多租户数据隔离的第一道防线（SqlScopeGuard 本身为最后一道）。</para>
/// <para>观测能力：所有操作均通过 <see cref="DapperTelemetry"/> 记录 OpenTelemetry Metrics，
/// 并通过结构化日志输出 Statement 名称、Provider、耗时与错误码。</para>
/// <para>异常映射：Execute 操作会通过 <see cref="DataCommandExceptionMapper"/> 将 Provider 特定的
/// 数据库异常映射为领域异常（如唯一键冲突 → ConcurrencyException）。</para>
/// </remarks>
internal sealed class DapperSqlExecutor(
    DbSession session,
    ICurrentTenant currentTenant,
    IOptions<DatabaseOptions> options,
    ILogger<DapperSqlExecutor> logger)
    : IQueryExecutor, ICommandExecutor, IMultiResultQueryExecutor
{
    private readonly DatabaseOptions _options = options.Value;

    /// <summary>
    /// 异步执行查询并返回单条结果；若无匹配则返回默认值。
    /// </summary>
    /// <typeparam name="T">结果元素类型。</typeparam>
    /// <param name="statement">SQL 语句定义（含名称、文本、数据范围）。</param>
    /// <param name="parameters">匿名对象或 DynamicParameters，可 null。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>单条结果或 default(T)。</returns>
    public async Task<T?> QuerySingleOrDefaultAsync<T>(
        SqlStatement statement,
        object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var commandParameters = CreateParameters(statement, parameters);
        var stopwatch = Stopwatch.StartNew();
        Exception? exception = null;

        try
        {
            await using var connectionLease = await session
                .AcquireConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
            var command = CreateCommand(
                statement,
                commandParameters,
                connectionLease.Transaction,
                cancellationToken);
            var connection = connectionLease.Connection;
#if FULLNET_AOT_COMPILE
            return await DapperAotSqlExecution.QuerySingleOrDefaultAsync<T>(
                connection,
                statement.Name,
                _options.Provider,
                statement.Text,
                command.Parameters as DynamicParameters
                    ?? throw new InvalidOperationException(
                        "Native AOT requires DynamicParameters for SQL execution."),
                connectionLease.Transaction,
                _options.CommandTimeoutSeconds,
                cancellationToken).ConfigureAwait(false);
#else
            return await connection.QuerySingleOrDefaultAsync<T>(command).ConfigureAwait(false);
#endif
        }
        catch (Exception caught)
        {
            exception = caught;
            throw;
        }
        finally
        {
            LogExecution(
                statement,
                DapperOperation.QuerySingle,
                stopwatch,
                exception);
        }
    }

    /// <summary>
    /// 异步执行查询并返回结果集只读列表。
    /// </summary>
    /// <typeparam name="T">结果元素类型。</typeparam>
    /// <param name="statement">SQL 语句定义。</param>
    /// <param name="parameters">匿名对象或 DynamicParameters，可 null。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>匹配行的只读列表；无匹配时返回空集合（非 null）。</returns>
    public async Task<IReadOnlyList<T>> QueryAsync<T>(
        SqlStatement statement,
        object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var commandParameters = CreateParameters(statement, parameters);
        var stopwatch = Stopwatch.StartNew();
        Exception? exception = null;

        try
        {
            await using var connectionLease = await session
                .AcquireConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
            var command = CreateCommand(
                statement,
                commandParameters,
                connectionLease.Transaction,
                cancellationToken);
            var connection = connectionLease.Connection;
#if FULLNET_AOT_COMPILE
            return await DapperAotSqlExecution.QueryAsync<T>(
                connection,
                statement.Name,
                _options.Provider,
                statement.Text,
                command.Parameters as DynamicParameters
                    ?? throw new InvalidOperationException(
                        "Native AOT requires DynamicParameters for SQL execution."),
                connectionLease.Transaction,
                _options.CommandTimeoutSeconds,
                cancellationToken).ConfigureAwait(false);
#else
            var rows = await connection.QueryAsync<T>(command).ConfigureAwait(false);
            return rows.AsList();
#endif
        }
        catch (Exception caught)
        {
            exception = caught;
            throw;
        }
        finally
        {
            LogExecution(statement, DapperOperation.Query, stopwatch, exception);
        }
    }

    /// <summary>
    /// 异步执行非查询 SQL 语句（INSERT / UPDATE / DELETE / DDL），返回受影响行数。
    /// </summary>
    /// <param name="statement">SQL 语句定义。</param>
    /// <param name="parameters">匿名对象或 DynamicParameters，可 null。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>受影响行数。</returns>
    /// <remarks>数据库异常会先经 <see cref="DataCommandExceptionMapper.TryMap"/> 尝试映射为领域异常后再抛出。</remarks>
    public async Task<int> ExecuteAsync(
        SqlStatement statement,
        object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var commandParameters = CreateParameters(statement, parameters);
        var stopwatch = Stopwatch.StartNew();
        Exception? exception = null;

        try
        {
            await using var connectionLease = await session
                .AcquireConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
            var command = CreateCommand(
                statement,
                commandParameters,
                connectionLease.Transaction,
                cancellationToken);
            var connection = connectionLease.Connection;
#if FULLNET_AOT_COMPILE
            var dynamicParameters = command.Parameters as DynamicParameters
                ?? throw new InvalidOperationException(
                    "Native AOT requires DynamicParameters for SQL execution.");
            return await DapperAotSqlExecution.ExecuteAsync(
                connection,
                statement.Name,
                _options.Provider,
                statement.Text,
                dynamicParameters,
                connectionLease.Transaction,
                _options.CommandTimeoutSeconds,
                cancellationToken).ConfigureAwait(false);
#else
            return await connection.ExecuteAsync(command).ConfigureAwait(false);
#endif
        }
        catch (Exception caught)
        {
            exception = caught;
            if (DataCommandExceptionMapper.TryMap(caught, out var mapped))
            {
                throw mapped;
            }

            throw;
        }
        finally
        {
            LogExecution(statement, DapperOperation.Execute, stopwatch, exception);
        }
    }

    /// <summary>
    /// 异步执行包含多个结果集的 SQL 批处理，通过 projector 委托顺序消费每个结果集并投影为最终结果。
    /// </summary>
    /// <typeparam name="TResult">投影后的最终结果类型。</typeparam>
    /// <param name="statement">SQL 语句定义（可含多个 SELECT）。</param>
    /// <param name="parameters">匿名对象或 DynamicParameters，可 null。</param>
    /// <param name="projector">消费 <see cref="IMultiResultReader"/> 的投影委托；必须按顺序完全消费所有结果集。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>投影后的结果。</returns>
    /// <exception cref="InvalidOperationException">当 projector 未完全消费所有结果集时抛出，防止遗漏数据。</exception>
    public async Task<TResult> QueryMultipleAsync<TResult>(
        SqlStatement statement,
        object? parameters,
        Func<IMultiResultReader, CancellationToken, Task<TResult>> projector,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projector);
        var commandParameters = CreateParameters(statement, parameters);
        var stopwatch = Stopwatch.StartNew();
        Exception? exception = null;

        try
        {
            await using var connectionLease = await session
                .AcquireConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
            var command = CreateCommand(
                statement,
                commandParameters,
                connectionLease.Transaction,
                cancellationToken);
            var connection = connectionLease.Connection;
            await using var grid = await connection
                .QueryMultipleAsync(command)
                .ConfigureAwait(false);
            var reader = new DapperMultiResultReader(grid);
            var result = await projector(reader, cancellationToken).ConfigureAwait(false);
            if (!grid.IsConsumed)
            {
                throw new InvalidOperationException(
                    "The multi-result projector must consume every result set in order.");
            }

            return result;
        }
        catch (Exception caught)
        {
            exception = caught;
            throw;
        }
        finally
        {
            LogExecution(
                statement,
                DapperOperation.QueryMultiple,
                stopwatch,
                exception);
        }
    }

    private CommandDefinition CreateCommand(
        SqlStatement statement,
        DynamicParameters parameters,
        IDbTransaction? transaction,
        CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            statement.Text,
            parameters,
            transaction,
            _options.CommandTimeoutSeconds,
            cancellationToken: cancellationToken);
    }

    private DynamicParameters CreateParameters(
        SqlStatement statement,
        object? values)
    {
        SqlScopeGuard.Validate(statement, currentTenant);

        var parameters = CreateDynamicParameters(values);
        if (statement.TenantBinding == SqlTenantBinding.CurrentTenantId)
        {
            parameters.Add("TenantId", currentTenant.Id!.Value);
        }

        return parameters;
    }

    private static DynamicParameters CreateDynamicParameters(object? values)
    {
        switch (values)
        {
            case null:
                return new DynamicParameters();
            case DynamicParameters existing:
                return existing;
            case IReadOnlyDictionary<string, object?> dictionary:
                var mapped = new DynamicParameters();
                foreach (var (key, value) in dictionary)
                {
                    mapped.Add(key, value);
                }

                return mapped;
            default:
#if FULLNET_AOT_COMPILE
                if (DapperAotParameterRegistry.TryBind(values, out var bound))
                {
                    return bound;
                }

                throw new InvalidOperationException(
                    $"Native AOT SQL parameters must be DynamicParameters, IReadOnlyDictionary<string, object?>, or a registered parameter type; received {values.GetType().FullName}.");
#else
                return new DynamicParameters(values);
#endif
        }
    }

    private void LogExecution(
        SqlStatement statement,
        DapperOperation operation,
        Stopwatch stopwatch,
        Exception? exception)
    {
        stopwatch.Stop();
        DapperTelemetry.Record(
            statement.Name,
            _options.Provider,
            operation,
            stopwatch.Elapsed,
            exception);
        if (exception is null)
        {
            DapperLog.StatementExecuted(
                logger,
                statement.Name,
                _options.Provider,
                stopwatch.Elapsed.TotalMilliseconds);
            return;
        }

        DapperLog.StatementFailed(
            logger,
            statement.Name,
            _options.Provider,
            stopwatch.Elapsed.TotalMilliseconds,
            DapperTelemetry.GetDatabaseErrorCode(exception),
            exception);
    }
}
