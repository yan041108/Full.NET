using Full.NET.Modules.Reporting.Contracts;

namespace Full.NET.Modules.Reporting.Domain;

/// <summary>将执行请求参数绑定为静态 Query Port 可接受的强类型值。</summary>
internal static class ReportingExecutionParameterBinder
{
    /// <summary>绑定并校验参数；失败时返回错误消息。</summary>
    public static (bool Succeeded, string? ErrorMessage, IReadOnlyDictionary<string, object?> Values) Bind(
        ReportingQueryPortDefinition queryPort,
        IReadOnlyList<ReportingParameterSchemaEntry> parameterSchema,
        IReadOnlyList<ReportingExecutionParameterValue> parameters)
    {
        var schemaError = ReportingParameterSchemaValidator.Validate(queryPort, parameterSchema);
        if (schemaError is not null)
        {
            return (false, schemaError, new Dictionary<string, object?>(StringComparer.Ordinal));
        }

        var valuesByKey = parameters
            .GroupBy(parameter => parameter.ParameterKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.Ordinal);
        if (valuesByKey.Count != parameters.Count)
        {
            return (false, "Duplicate execution parameter keys are not allowed.", new Dictionary<string, object?>(StringComparer.Ordinal));
        }

        var bound = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var portParameter in queryPort.Parameters)
        {
            if (!valuesByKey.TryGetValue(portParameter.ParameterKey, out var rawValue))
            {
                if (!string.IsNullOrWhiteSpace(portParameter.DefaultValue))
                {
                    rawValue = portParameter.DefaultValue;
                }
                else if (portParameter.IsRequired)
                {
                    return (false, $"Missing required parameter '{portParameter.ParameterKey}'.", bound);
                }
                else
                {
                    continue;
                }
            }

            if (!TryConvert(portParameter, rawValue, out var converted, out var convertError))
            {
                return (false, convertError, bound);
            }

            bound[portParameter.ParameterKey] = converted;
        }

        return (true, null, bound);
    }

    private static bool TryConvert(
        ReportingQueryPortParameterDefinition portParameter,
        string? rawValue,
        out object? converted,
        out string? errorMessage)
    {
        converted = null;
        errorMessage = null;
        if (string.Equals(portParameter.DataTypeKey, ReportingParameterDataTypeKeys.Integer, StringComparison.Ordinal))
        {
            if (!int.TryParse(rawValue, out var integer))
            {
                errorMessage = $"Parameter '{portParameter.ParameterKey}' must be an integer.";
                return false;
            }

            if (portParameter.Minimum.HasValue && integer < portParameter.Minimum.Value)
            {
                errorMessage = $"Parameter '{portParameter.ParameterKey}' must be >= {portParameter.Minimum.Value}.";
                return false;
            }

            if (portParameter.Maximum.HasValue && integer > portParameter.Maximum.Value)
            {
                errorMessage = $"Parameter '{portParameter.ParameterKey}' must be <= {portParameter.Maximum.Value}.";
                return false;
            }

            if (string.Equals(portParameter.ParameterKey, "topN", StringComparison.Ordinal))
            {
                integer = Math.Clamp(integer, 1, ReportingExecutionPolicy.MaxTopN);
            }

            converted = integer;
            return true;
        }

        if (string.Equals(portParameter.DataTypeKey, ReportingParameterDataTypeKeys.Boolean, StringComparison.Ordinal))
        {
            if (!bool.TryParse(rawValue, out var boolean))
            {
                errorMessage = $"Parameter '{portParameter.ParameterKey}' must be a boolean.";
                return false;
            }

            converted = boolean;
            return true;
        }

        converted = rawValue?.Trim();
        return true;
    }
}
