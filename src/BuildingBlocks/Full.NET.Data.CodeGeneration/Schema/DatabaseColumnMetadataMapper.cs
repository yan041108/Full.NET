namespace Full.NET.Data.CodeGeneration.Schema;

/// <summary>
/// 把数据库列元数据映射为 Full.NET 标量列；无法安全表达的类型必须失败而不是猜测。
/// </summary>
public static class DatabaseColumnMetadataMapper
{
    /// <summary>
    /// 按原始列顺序映射整张表的列元数据；遇到无法安全表达的物理类型会抛出 NotSupportedException。
    /// </summary>
    /// <param name="provider">元数据方言，决定 SQL Server 与 MySQL 的类型映射分支。</param>
    /// <param name="metadata">从 INFORMATION_SCHEMA 读出的原始列元数据。</param>
    /// <returns>按 OrdinalPosition 排序后的 Full.NET 标量列集合。</returns>
    public static IReadOnlyList<FullNetColumn> Map(
        DatabaseMetadataProvider provider,
        IReadOnlyList<DatabaseColumnMetadata> metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        return metadata
            .OrderBy(column => column.OrdinalPosition)
            .Select(column => MapColumn(provider, column))
            .ToArray();
    }

    /// <summary>
    /// 尝试映射单列；不支持的物理类型返回 false，供目录跳过而不是阻断整张表列表。
    /// </summary>
    public static bool TryMap(
        DatabaseMetadataProvider provider,
        DatabaseColumnMetadata metadata,
        out FullNetColumn column)
    {
        try
        {
            column = MapColumn(provider, metadata);
            return true;
        }
        catch (NotSupportedException)
        {
            column = null!;
            return false;
        }
    }

    private static FullNetColumn MapColumn(
        DatabaseMetadataProvider provider,
        DatabaseColumnMetadata metadata)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(metadata.Name);
        var scalarType = provider switch
        {
            DatabaseMetadataProvider.SqlServer => MapSqlServer(metadata),
            DatabaseMetadataProvider.MySql => MapMySql(metadata),
            _ => throw new ArgumentOutOfRangeException(
                nameof(provider),
                provider,
                null),
        };
        int? maxLength = scalarType == FullNetScalarType.String
            ? GetStringLength(metadata)
            : null;
        var numericPrecision = scalarType == FullNetScalarType.Decimal
            ? metadata.NumericPrecision
            : null;
        var numericScale = scalarType == FullNetScalarType.Decimal
            ? metadata.NumericScale
            : null;

        return new FullNetColumn(
            metadata.Name,
            metadata.Name,
            ToJsonPropertyName(metadata.Name),
            scalarType,
            metadata.IsNullable,
            maxLength,
            numericPrecision,
            numericScale);
    }

    private static FullNetScalarType MapSqlServer(
        DatabaseColumnMetadata metadata) =>
        metadata.DataType.ToLowerInvariant() switch
        {
            "uniqueidentifier" => FullNetScalarType.Uuid,
            "varchar" or "nvarchar" or "char" or "nchar" =>
                FullNetScalarType.String,
            "int" => FullNetScalarType.Int32,
            "bigint" => FullNetScalarType.Int64,
            "bit" => FullNetScalarType.Boolean,
            "datetime" or "datetime2" or "datetimeoffset" =>
                FullNetScalarType.DateTimeUtc,
            "decimal" or "numeric" => FullNetScalarType.Decimal,
            _ => Unsupported(metadata),
        };

    private static FullNetScalarType MapMySql(
        DatabaseColumnMetadata metadata)
    {
        var dataType = metadata.DataType.ToLowerInvariant();
        if (dataType == "binary")
        {
            return string.Equals(
                    metadata.ColumnType,
                    "binary(16)",
                    StringComparison.OrdinalIgnoreCase)
                && IsIdentifier(metadata.Name)
                    ? FullNetScalarType.Uuid
                    : Unsupported(metadata);
        }

        if (dataType == "tinyint")
        {
            return string.Equals(
                metadata.ColumnType,
                "tinyint(1)",
                StringComparison.OrdinalIgnoreCase)
                    ? FullNetScalarType.Boolean
                    : Unsupported(metadata);
        }

        return dataType switch
        {
            "varchar" or "char" => FullNetScalarType.String,
            "int" or "integer" => FullNetScalarType.Int32,
            "bigint" => FullNetScalarType.Int64,
            "datetime" or "timestamp" => FullNetScalarType.DateTimeUtc,
            "decimal" or "numeric" => FullNetScalarType.Decimal,
            _ => Unsupported(metadata),
        };
    }

    private static int GetStringLength(DatabaseColumnMetadata metadata)
    {
        if (metadata.MaxLength is not > 0 or > int.MaxValue)
        {
            throw new NotSupportedException(
                $"字段 {metadata.Name} 的字符串长度无法安全导入。");
        }

        return checked((int)metadata.MaxLength.Value);
    }

    private static bool IsIdentifier(string name) =>
        string.Equals(name, "Id", StringComparison.Ordinal)
        || name.EndsWith("Id", StringComparison.Ordinal);

    private static string ToJsonPropertyName(string name) =>
        string.Concat(
            char.ToLowerInvariant(name[0]).ToString(),
            name.AsSpan(1));

    private static FullNetScalarType Unsupported(
        DatabaseColumnMetadata metadata) =>
        throw new NotSupportedException(
            $"字段 {metadata.Name} 的数据库类型 {metadata.ColumnType} 不受支持。");
}
