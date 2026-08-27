#if FULLNET_AOT_COMPILE
using System.Data;
using System.Data.Common;
using Full.NET.Data.Abstractions;
using global::Dapper;

namespace Full.NET.Data.Dapper;

/// <summary>
/// Native AOT 下绕过 SqlMapper POCO 反射物化的 ADO.NET 查询执行。
/// </summary>
internal static class DapperAotSqlExecution
{
    public static bool IsScalarType(Type type)
    {
        var scalarType = Nullable.GetUnderlyingType(type) ?? type;
        return scalarType == typeof(long)
            || scalarType == typeof(int)
            || scalarType == typeof(bool)
            || scalarType == typeof(Guid)
            || scalarType == typeof(string)
            || scalarType == typeof(decimal)
            || scalarType == typeof(double)
            || scalarType == typeof(float)
            || scalarType == typeof(short)
            || scalarType == typeof(byte);
    }

    public static async Task<T?> QuerySingleOrDefaultAsync<T>(
        DbConnection connection,
        string statementName,
        DatabaseProvider provider,
        string sql,
        DynamicParameters parameters,
        IDbTransaction? transaction,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        var commandRental = CreateCommandRental(
            connection,
            statementName,
            provider,
            sql,
            parameters,
            transaction,
            commandTimeoutSeconds);
        var reusable = false;
        try
        {
            T? result;
            await using (var reader = await commandRental.Command
                .ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                if (IsScalarType(typeof(T)))
                {
                    result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
                        ? ReadScalar<T>(reader, 0)
                        : default;
                }
                else
                {
                    if (!DapperAotMaterializerRegistry.TryGetReader<T>(out var readRow))
                    {
                        throw new InvalidOperationException(
                            $"Native AOT has no row materializer registered for {typeof(T).FullName}.");
                    }

                    result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
                        ? readRow(reader)
                        : default;
                }
            }

            reusable = true;
            return result;
        }
        finally
        {
            await commandRental.ReleaseAsync(reusable).ConfigureAwait(false);
        }
    }

    public static async Task<IReadOnlyList<T>> QueryAsync<T>(
        DbConnection connection,
        string statementName,
        DatabaseProvider provider,
        string sql,
        DynamicParameters parameters,
        IDbTransaction? transaction,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        var commandRental = CreateCommandRental(
            connection,
            statementName,
            provider,
            sql,
            parameters,
            transaction,
            commandTimeoutSeconds);
        var reusable = false;
        try
        {
            IReadOnlyList<T> result;
            await using (var reader = await commandRental.Command
                .ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                if (IsScalarType(typeof(T)))
                {
                    var scalarRows = new List<T>();
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        scalarRows.Add(ReadScalar<T>(reader, 0));
                    }

                    result = scalarRows;
                }
                else
                {
                    if (!DapperAotMaterializerRegistry.TryGetReader<T>(out var readRow))
                    {
                        throw new InvalidOperationException(
                            $"Native AOT has no row materializer registered for {typeof(T).FullName}.");
                    }

                    var rows = new List<T>();
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        rows.Add(readRow(reader));
                    }

                    result = rows;
                }
            }

            reusable = true;
            return result;
        }
        finally
        {
            await commandRental.ReleaseAsync(reusable).ConfigureAwait(false);
        }
    }

    public static async Task<int> ExecuteAsync(
        DbConnection connection,
        string statementName,
        DatabaseProvider provider,
        string sql,
        DynamicParameters parameters,
        IDbTransaction? transaction,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        var commandRental = CreateCommandRental(
            connection,
            statementName,
            provider,
            sql,
            parameters,
            transaction,
            commandTimeoutSeconds);
        var reusable = false;
        try
        {
            var affectedRows = await commandRental.Command
                .ExecuteNonQueryAsync(cancellationToken)
                .ConfigureAwait(false);
            reusable = true;
            return affectedRows;
        }
        finally
        {
            await commandRental.ReleaseAsync(reusable).ConfigureAwait(false);
        }
    }

    public static async Task<TResult> QueryMultipleAsync<TResult>(
        DbConnection connection,
        string statementName,
        DatabaseProvider provider,
        string sql,
        DynamicParameters parameters,
        IDbTransaction? transaction,
        int commandTimeoutSeconds,
        Func<IMultiResultReader, CancellationToken, Task<TResult>> projector,
        CancellationToken cancellationToken)
    {
        var commandRental = CreateCommandRental(
            connection,
            statementName,
            provider,
            sql,
            parameters,
            transaction,
            commandTimeoutSeconds);
        var reusable = false;
        try
        {
            TResult result;
            await using (var reader = await commandRental.Command
                .ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                var multiResultReader = new AotMultiResultReader(
                    reader,
                    cancellationToken);
                result = await projector(multiResultReader, cancellationToken)
                    .ConfigureAwait(false);
                if (!multiResultReader.IsConsumed)
                {
                    throw new InvalidOperationException(
                        "The multi-result projector must consume every result set in order.");
                }
            }

            reusable = true;
            return result;
        }
        finally
        {
            await commandRental.ReleaseAsync(reusable).ConfigureAwait(false);
        }
    }

    private static CommandRental CreateCommandRental(
        DbConnection connection,
        string statementName,
        DatabaseProvider provider,
        string sql,
        DynamicParameters parameters,
        IDbTransaction? transaction,
        int commandTimeoutSeconds)
    {
        // AOT 不走 SqlMapper 的集合展开；必须先把 IN @Ids 展开为标量占位符再绑定。
        var (expandedSql, expandedParameters) = DapperAotEnumerableParameterExpander.Expand(
            sql,
            parameters);
        var dbTransaction = transaction as DbTransaction;
        DapperAotCommandFactory? factory = null;
        DbCommand? command = null;
        try
        {
            if (ReferenceEquals(expandedParameters, parameters)
                && DapperAotStaticCommandPlanRegistry.TryGetFactory(
                    statementName,
                    provider,
                    out factory))
            {
                command = factory.GetCommand(
                    connection,
                    expandedSql,
                    CommandType.Text,
                    expandedParameters);
            }
            else
            {
                command = connection.CreateCommand();
                command.CommandText = expandedSql;
                command.CommandType = CommandType.Text;
                BindParameters(command, expandedParameters);
            }

            command.Connection = connection;
            command.Transaction = dbTransaction;
            command.CommandTimeout = commandTimeoutSeconds;
            return new CommandRental(command, factory);
        }
        catch
        {
            command?.Dispose();
            throw;
        }
    }

    private static void BindParameters(
        IDbCommand command,
        DynamicParameters parameters)
    {
        foreach (var name in parameters.ParameterNames)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = parameters.Get<object>(name) ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }
    }

    private readonly record struct CommandRental(
        DbCommand Command,
        DapperAotCommandFactory? Factory)
    {
        public ValueTask ReleaseAsync(bool reusable)
        {
            if (reusable
                && Factory is not null
                && Factory.TryRecycle(Command))
            {
                return ValueTask.CompletedTask;
            }

            return Command.DisposeAsync();
        }
    }

    private static T ReadScalar<T>(DbDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return default!;
        }

        var value = reader.GetValue(ordinal);
        if (value is T typed)
        {
            return typed;
        }

        var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        return (T)Convert.ChangeType(value, targetType);
    }

    private static T ReadRow<T>(DbDataReader reader)
    {
        if (IsScalarType(typeof(T)))
        {
            return ReadScalar<T>(reader, 0);
        }

        if (!DapperAotMaterializerRegistry.TryGetReader<T>(out var readRow))
        {
            throw new InvalidOperationException(
                $"Native AOT has no row materializer registered for {typeof(T).FullName}.");
        }

        return readRow(reader);
    }

    /// <summary>
    /// Native AOT 多结果读取器只允许按顺序消费，并在每次读取完成后推进到下一结果集。
    /// </summary>
    private sealed class AotMultiResultReader(
        DbDataReader reader,
        CancellationToken cancellationToken) : IMultiResultReader
    {
        private int _reading;

        public bool IsConsumed { get; private set; }

        public async Task<T?> ReadSingleOrDefaultAsync<T>()
        {
            EnterRead();
            try
            {
                var hasRow = await reader.ReadAsync(cancellationToken)
                    .ConfigureAwait(false);
                var result = hasRow ? ReadRow<T>(reader) : default;
                if (hasRow
                    && await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    throw new InvalidOperationException(
                        "The current result set contains more than one row.");
                }

                await AdvanceAsync().ConfigureAwait(false);
                return result;
            }
            finally
            {
                ExitRead();
            }
        }

        public async Task<IReadOnlyList<T>> ReadAsync<T>()
        {
            EnterRead();
            try
            {
                var rows = new List<T>();
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    rows.Add(ReadRow<T>(reader));
                }

                await AdvanceAsync().ConfigureAwait(false);
                return rows;
            }
            finally
            {
                ExitRead();
            }
        }

        private async Task AdvanceAsync()
        {
            IsConsumed = !await reader.NextResultAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        private void EnterRead()
        {
            if (IsConsumed)
            {
                throw new InvalidOperationException(
                    "All multi-result sets have already been consumed.");
            }

            if (Interlocked.Exchange(ref _reading, 1) != 0)
            {
                throw new InvalidOperationException(
                    "Multi-result sets must be consumed sequentially.");
            }
        }

        private void ExitRead() => Volatile.Write(ref _reading, 0);
    }
}
#endif
