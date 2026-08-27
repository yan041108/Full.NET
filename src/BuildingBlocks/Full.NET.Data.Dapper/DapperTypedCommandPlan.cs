using System.Data;
using System.Data.Common;
using Full.NET.Data.Abstractions;

namespace Full.NET.Data.Dapper;

/// <summary>
/// 为一条固定 SQL 和强类型参数形状提供按序号更新与 Provider 隔离的单槽命令复用。
/// </summary>
/// <typeparam name="TArgs">编译期闭合的参数类型。</typeparam>
internal abstract class DapperTypedCommandPlan<TArgs>
{
    private readonly string _sql;
    private DbCommand? _sqlServerStorage;
    private DbCommand? _mySqlStorage;

    protected DapperTypedCommandPlan(string sql)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        _sql = sql;
    }

    /// <summary>
    /// 租用命令并用当前强类型参数覆盖全部固定槽位。
    /// </summary>
    public DbCommand GetCommand(
        DbConnection connection,
        DatabaseProvider provider,
        TArgs args)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(args);
        var command = TryTake(provider) ?? CreateCommand(connection);
        try
        {
            command.Connection = connection;
            UpdateParameters(command, args);
            return command;
        }
        catch
        {
            command.Dispose();
            throw;
        }
    }

    /// <summary>
    /// 清除上一次请求的值与会话引用；同 Provider 已有空闲命令时拒绝回收。
    /// </summary>
    public bool TryRecycle(DatabaseProvider provider, DbCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        foreach (DbParameter parameter in command.Parameters)
        {
            parameter.Value = DBNull.Value;
        }

        command.Transaction = null;
        command.Connection = null;
        return provider switch
        {
            DatabaseProvider.SqlServer =>
                TryStore(ref _sqlServerStorage, command),
            DatabaseProvider.MySql =>
                TryStore(ref _mySqlStorage, command),
            _ => throw new ArgumentOutOfRangeException(
                nameof(provider),
                provider,
                "Unsupported database provider."),
        };
    }

    protected abstract void AddParameters(DbCommand command);

    protected abstract void UpdateParameters(DbCommand command, TArgs args);

    protected static void AddParameter(DbCommand command, string name)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = DBNull.Value;
        command.Parameters.Add(parameter);
    }

    protected static object AsValue(object? value) => value ?? DBNull.Value;

    private DbCommand CreateCommand(DbConnection connection)
    {
        var command = connection.CreateCommand();
        try
        {
            command.CommandText = _sql;
            command.CommandType = CommandType.Text;
            AddParameters(command);
            return command;
        }
        catch
        {
            command.Dispose();
            throw;
        }
    }

    private DbCommand? TryTake(DatabaseProvider provider) =>
        provider switch
        {
            DatabaseProvider.SqlServer => Take(ref _sqlServerStorage),
            DatabaseProvider.MySql => Take(ref _mySqlStorage),
            _ => throw new ArgumentOutOfRangeException(
                nameof(provider),
                provider,
                "Unsupported database provider."),
        };

    private static DbCommand? Take(ref DbCommand? storage)
    {
        while (true)
        {
            var command = storage;
            if (command is null)
            {
                return null;
            }

            if (Interlocked.CompareExchange(ref storage, null, command) == command)
            {
                return command;
            }
        }
    }

    private static bool TryStore(ref DbCommand? storage, DbCommand command) =>
        storage is null
        && Interlocked.CompareExchange(ref storage, command, null) is null;
}
