using System.Data.Common;
using System.Globalization;
using Full.NET.Modules.Reporting.Domain;

namespace Full.NET.Modules.Reporting.Connectivity;

/// <summary>在外部只读数据源上执行受审查 SQL；失败即停，不返回部分结果。</summary>
internal static class ReportingExternalQueryExecutor
{
    /// <summary>执行 SQL 并读取全部结果行。</summary>
    public static async Task<(bool Succeeded, string? ErrorMessage, IReadOnlyList<string> ColumnKeys, IReadOnlyList<IReadOnlyDictionary<string, string?>> Rows)> ExecuteAsync(
        DbConnection connection,
        string sql,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = ReportingExecutionPolicy.CommandTimeoutSeconds;
            foreach (var (name, value) in parameters)
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = name.StartsWith("@", StringComparison.Ordinal) ? name : $"@{name}";
                parameter.Value = value ?? DBNull.Value;
                command.Parameters.Add(parameter);
            }

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var columnKeys = Enumerable.Range(0, reader.FieldCount)
                .Select(reader.GetName)
                .ToArray();
            var rows = new List<IReadOnlyDictionary<string, string?>>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var row = new Dictionary<string, string?>(StringComparer.Ordinal);
                for (var index = 0; index < columnKeys.Length; index++)
                {
                    row[columnKeys[index]] = FormatCellValue(reader, index);
                }

                rows.Add(row);
            }

            return (true, null, columnKeys, rows);
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException or TimeoutException)
        {
            return (false, SanitizeMessage(ex.Message), [], []);
        }
    }

    private static string? FormatCellValue(DbDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        var value = reader.GetValue(ordinal);
        var text = value switch
        {
            DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture),
        };
        if (text is null)
        {
            return null;
        }

        return text.Length <= ReportingExecutionPolicy.MaxCellValueLength
            ? text
            : text[..ReportingExecutionPolicy.MaxCellValueLength];
    }

    private static string SanitizeMessage(string message) =>
        message.Length <= 512 ? message : message[..512];
}
