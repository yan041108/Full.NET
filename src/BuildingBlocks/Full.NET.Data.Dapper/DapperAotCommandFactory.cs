using System.Data;
using System.Data.Common;
using global::Dapper;

namespace Full.NET.Data.Dapper;

/// <summary>
/// 为一个固定参数顺序提供 Dapper.AOT 官方命令创建、原地更新与单槽回收能力。
/// </summary>
internal sealed class DapperAotCommandFactory : CommandFactory<DynamicParameters>
{
    private readonly string[] _parameterNames;
    private DbCommand? _storage;

    public DapperAotCommandFactory(IEnumerable<string> parameterNames)
    {
        ArgumentNullException.ThrowIfNull(parameterNames);
        _parameterNames = parameterNames.ToArray();
        if (_parameterNames.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                "Command parameter names must not be empty.",
                nameof(parameterNames));
        }
    }

    public override DbCommand GetCommand(
        DbConnection connection,
        string sql,
        CommandType commandType,
        DynamicParameters args) =>
        TryReuseInterlocked(
            ref _storage,
            sql,
            commandType,
            args)
        ?? base.GetCommand(connection, sql, commandType, args);

    public override void AddParameters(
        in UnifiedCommand command,
        DynamicParameters args)
    {
        foreach (var name in _parameterNames)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = AsValue(args.Get<object>(name));
            command.Parameters.Add(parameter);
        }
    }

    public override void UpdateParameters(
        in UnifiedCommand command,
        DynamicParameters args)
    {
        for (var index = 0; index < _parameterNames.Length; index++)
        {
            command.Parameters[index].Value = AsValue(
                args.Get<object>(_parameterNames[index]));
        }
    }

    public override bool TryRecycle(DbCommand command)
    {
        foreach (DbParameter parameter in command.Parameters)
        {
            // 空闲命令不得保留上一请求的大字符串、二进制载荷或租户标识。
            parameter.Value = DBNull.Value;
        }

        command.Transaction = null;
        command.Connection = null;

        return TryRecycleInterlocked(
            ref _storage,
            command);
    }
}
