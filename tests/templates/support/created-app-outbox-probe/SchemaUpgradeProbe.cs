using System.Data.Common;

internal static class SchemaUpgradeProbe
{
    internal static async Task<(int Removed, int CommentRemoved)> UnaccountAsync(DbConnection connection, string provider)
    {
        // SQL Server 的列与扩展属性是独立步骤；先移除属性再撤销 journal，复现第二步未完成。
        var commentRemoved = 0;
        if (provider == "SqlServer")
        {
            if (await ReadCommentAsync(connection, provider) != 1)
            {
                throw new InvalidOperationException("Expected the migrated Description comment before recovery setup.");
            }

            await using var propertyCommand = connection.CreateCommand();
            propertyCommand.CommandText = """
                EXEC sys.sp_dropextendedproperty @name=N'MS_Description',
                    @level0type=N'SCHEMA', @level0name=N'dbo',
                    @level1type=N'TABLE', @level1name=N'acme_catalog_product',
                    @level2type=N'COLUMN', @level2name=N'Description'
                """;
            await propertyCommand.ExecuteNonQueryAsync();
            if (await ReadCommentAsync(connection, provider) != 0)
            {
                throw new InvalidOperationException("Description comment was not removed for recovery setup.");
            }

            commentRemoved = 1;
        }

        // 仅测试库撤销精确的 002 journal 行，保留已提交的列以验证未记账 DDL 重跑。
        var scriptName = $"acme.catalog.Migrations.{provider}.002_AddProductDescription.sql";
        await using var command = connection.CreateCommand();
        command.CommandText = provider switch
        {
            "SqlServer" => "DELETE FROM dbo.SchemaVersions WHERE ScriptName = @ScriptName",
            "MySql" => "DELETE FROM schemaversions WHERE ScriptName = @ScriptName",
            _ => throw new InvalidOperationException("Unsupported provider for schema upgrade probe.")
        };
        var parameter = command.CreateParameter();
        parameter.ParameterName = "ScriptName";
        parameter.Value = scriptName;
        command.Parameters.Add(parameter);
        var removed = await command.ExecuteNonQueryAsync();
        if (removed != 1)
        {
            throw new InvalidOperationException($"Expected one schema upgrade journal row, found {removed}.");
        }

        return (removed, commentRemoved);
    }

    internal static async Task<int> ReadCommentAsync(DbConnection connection, string provider)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = provider switch
        {
            "SqlServer" => """
                SELECT COUNT(1) FROM sys.extended_properties
                WHERE class = 1
                  AND major_id = OBJECT_ID(N'dbo.acme_catalog_product')
                  AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.acme_catalog_product'), N'Description', 'ColumnId')
                  AND name = N'MS_Description'
                  AND CONVERT(nvarchar(4000), value) = N'商品的可选说明，存量商品保持空值。'
                """,
            "MySql" => """
                SELECT COUNT(1) FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_SCHEMA = DATABASE()
                  AND TABLE_NAME = 'acme_catalog_product'
                  AND COLUMN_NAME = 'Description'
                  AND COLUMN_COMMENT = '商品的可选说明，存量商品保持空值。'
                """,
            _ => throw new InvalidOperationException("Unsupported provider for schema upgrade metadata probe.")
        };
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
}
