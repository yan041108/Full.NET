using Full.NET.Modules.Reporting.Contracts;

namespace Full.NET.Modules.Reporting.Domain;

/// <summary>报表参数 Schema 与静态 Query Port 参数定义的一致性校验。</summary>
internal static class ReportingParameterSchemaValidator
{
    /// <summary>校验参数 Schema 是否完全覆盖 Query Port 所需参数且无多余键。</summary>
    /// <param name="queryPort">静态 Query Port 定义。</param>
    /// <param name="parameterSchema">草稿参数 Schema。</param>
    /// <returns>失败消息；成功时为 <see langword="null"/>。</returns>
    public static string? Validate(
        ReportingQueryPortDefinition queryPort,
        IReadOnlyList<ReportingParameterSchemaEntry> parameterSchema)
    {
        var schemaByKey = parameterSchema
            .GroupBy(entry => entry.ParameterKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        if (schemaByKey.Count != parameterSchema.Count)
        {
            return "Parameter schema contains duplicate parameter keys.";
        }

        foreach (var portParameter in queryPort.Parameters)
        {
            if (!schemaByKey.TryGetValue(portParameter.ParameterKey, out var schemaEntry))
            {
                return $"Missing parameter schema entry '{portParameter.ParameterKey}'.";
            }

            if (!string.Equals(schemaEntry.DataTypeKey, portParameter.DataTypeKey, StringComparison.Ordinal))
            {
                return $"Parameter '{portParameter.ParameterKey}' data type must be '{portParameter.DataTypeKey}'.";
            }

            if (portParameter.IsRequired && !schemaEntry.IsRequired)
            {
                return $"Parameter '{portParameter.ParameterKey}' must remain required.";
            }
        }

        foreach (var schemaEntry in parameterSchema)
        {
            if (queryPort.Parameters.All(parameter =>
                    !string.Equals(parameter.ParameterKey, schemaEntry.ParameterKey, StringComparison.Ordinal)))
            {
                return $"Parameter '{schemaEntry.ParameterKey}' is not declared by the selected query port.";
            }

            if (string.IsNullOrWhiteSpace(schemaEntry.DisplayName))
            {
                return $"Parameter '{schemaEntry.ParameterKey}' display name is required.";
            }
        }

        return null;
    }
}
