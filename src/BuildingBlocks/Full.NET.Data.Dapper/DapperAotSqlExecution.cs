#if FULLNET_AOT_COMPILE
using System.Data;
using System.Data.Common;
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
        string sql,
        DynamicParameters parameters,
        IDbTransaction? transaction,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(
            connection,
            sql,
            parameters,
            transaction,
            commandTimeoutSeconds);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        if (IsScalarType(typeof(T)))
        {
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return default;
            }

            return ReadScalar<T>(reader, 0);
        }

        if (!DapperAotMaterializerRegistry.TryGetReader<T>(out var readRow))
        {
            throw new InvalidOperationException(
                $"Native AOT has no row materializer registered for {typeof(T).FullName}.");
        }

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return default;
        }

        return readRow(reader);
    }

    public static async Task<IReadOnlyList<T>> QueryAsync<T>(
        DbConnection connection,
        string sql,
        DynamicParameters parameters,
        IDbTransaction? transaction,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(
            connection,
            sql,
            parameters,
            transaction,
            commandTimeoutSeconds);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        if (IsScalarType(typeof(T)))
        {
            var scalarRows = new List<T>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                scalarRows.Add(ReadScalar<T>(reader, 0));
            }

            return scalarRows;
        }

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

        return rows;
    }

    public static async Task<int> ExecuteAsync(
        DbConnection connection,
        string sql,
        DynamicParameters parameters,
        IDbTransaction? transaction,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(
            connection,
            sql,
            parameters,
            transaction,
            commandTimeoutSeconds);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static DbCommand CreateCommand(
        DbConnection connection,
        string sql,
        DynamicParameters parameters,
        IDbTransaction? transaction,
        int commandTimeoutSeconds)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandType = CommandType.Text;
        command.CommandTimeout = commandTimeoutSeconds;
        if (transaction is DbTransaction dbTransaction)
        {
            command.Transaction = dbTransaction;
        }

        BindParameters(command, parameters);
        return command;
    }

    private static void BindParameters(IDbCommand command, DynamicParameters parameters)
    {
        foreach (var name in parameters.ParameterNames)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            var value = parameters.Get<object>(name);
            parameter.Value = value ?? DBNull.Value;
            command.Parameters.Add(parameter);
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
}
#endif
