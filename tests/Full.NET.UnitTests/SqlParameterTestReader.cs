using Dapper;

namespace Full.NET.UnitTests;

/// <summary>
/// 在单元测试中统一读取匿名对象、字典与 Dapper 参数袋，避免测试绑定具体参数载体。
/// </summary>
internal static class SqlParameterTestReader
{
    /// <summary>
    /// 读取必需的 SQL 参数并转换为目标类型。
    /// </summary>
    /// <typeparam name="T">参数值的目标类型。</typeparam>
    /// <param name="parameters">业务代码传给 SQL 执行器的参数载体。</param>
    /// <param name="name">不带前缀的参数名。</param>
    /// <returns>指定名称对应的参数值。</returns>
    /// <exception cref="InvalidOperationException">参数载体为空、参数不存在或类型不匹配时抛出。</exception>
    internal static T ReadSqlParameter<T>(object? parameters, string name)
    {
        var value = ReadOptionalSqlParameter(parameters, name, out var found);
        if (!found)
        {
            throw new InvalidOperationException($"Parameter '{name}' was not found.");
        }

        if (value is null && default(T) is null)
        {
            return default!;
        }

        return value is T typed
            ? typed
            : throw new InvalidOperationException(
                $"Parameter '{name}' is not of type '{typeof(T).FullName}'.");
    }

    /// <summary>
    /// 尝试读取可选 SQL 参数，并区分“参数不存在”和“参数值为 null”。
    /// </summary>
    /// <param name="parameters">业务代码传给 SQL 执行器的参数载体。</param>
    /// <param name="name">不带前缀的参数名。</param>
    /// <param name="found">返回时指示参数名是否存在。</param>
    /// <returns>参数值；参数不存在或值为 null 时返回 <see langword="null"/>。</returns>
    internal static object? ReadOptionalSqlParameter(
        object? parameters,
        string name,
        out bool found)
    {
        if (parameters is IReadOnlyDictionary<string, object?> readOnly
            && readOnly.TryGetValue(name, out var dictionaryValue))
        {
            found = true;
            return dictionaryValue;
        }

        if (parameters is DynamicParameters dynamicParameters
            && dynamicParameters.ParameterNames.Contains(name, StringComparer.Ordinal))
        {
            found = true;
            return dynamicParameters.Get<object?>(name);
        }

        var property = parameters?.GetType().GetProperty(name);
        found = property is not null;
        return property?.GetValue(parameters);
    }

    /// <summary>
    /// 将 SQL 参数载体投影为只读字典，供需要同时断言多项参数的测试使用。
    /// </summary>
    /// <param name="parameters">业务代码传给 SQL 执行器的参数载体。</param>
    /// <returns>按参数名索引的参数快照。</returns>
    internal static IReadOnlyDictionary<string, object?> ReadSqlParameters(object? parameters)
    {
        if (parameters is IReadOnlyDictionary<string, object?> readOnly)
        {
            return readOnly;
        }

        if (parameters is DynamicParameters dynamicParameters)
        {
            return dynamicParameters.ParameterNames.ToDictionary(
                static name => name,
                name => dynamicParameters.Get<object?>(name),
                StringComparer.Ordinal);
        }

        return parameters?.GetType()
            .GetProperties()
            .ToDictionary(
                static property => property.Name,
                property => property.GetValue(parameters),
                StringComparer.Ordinal)
            ?? new Dictionary<string, object?>(StringComparer.Ordinal);
    }
}
