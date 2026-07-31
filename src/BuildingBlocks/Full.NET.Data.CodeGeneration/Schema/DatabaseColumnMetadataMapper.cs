namespace Full.NET.Data.CodeGeneration.Schema;

internal static class DatabaseColumnMetadataMapper
{
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
