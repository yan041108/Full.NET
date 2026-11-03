using System.Text.RegularExpressions;
using Full.NET.Data.CodeGeneration.Schema;

namespace Full.NET.Data.CodeGeneration.Generation;

/// <summary>将内置 SQL 参数模板输出为静态字典，并为结果行产生显式物化路径。</summary>
internal static class CrudStaticSqlGenerator
{
    // 只转换内置模板中的扁平参数初始化器，不解析调用方提供的 C#，未知形状直接拒绝。
    internal static string StaticParameters(string source) => Regex.Replace(source,
        @"new\s*\{(?<values>[^{}]*)\}", match =>
        {
            var entries = match.Groups["values"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var rendered = entries.Select(entry =>
            {
                var equal = entry.IndexOf('=');
                var expression = equal >= 0 ? entry[(equal + 1)..].Trim() : entry;
                var name = equal >= 0 ? entry[..equal].Trim() : entry[(entry.LastIndexOf('.') + 1)..].Trim();
                if (name.Length == 0 || !name.All(character => char.IsAsciiLetterOrDigit(character) || character == '_'))
                    throw new InvalidOperationException("内置 SQL 参数模板不是受支持的静态形状。");
                return $"[\"{name}\"] = {expression}";
            });
            return "new Dictionary<string, object?> { " + string.Join(", ", rendered) + " }";
        }, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    internal static string Materializer(FullNetCrudSchema schema) => $$"""

        // 物化器在模块注册时同步安装，禁止首个请求依赖反射推导 Record 构造函数。
        internal static class {{schema.ClrTypeName}}GeneratedMaterializer
        {
        #if FULLNET_AOT_COMPILE
            internal static void Register() =>
                Full.NET.Data.Dapper.DapperAotMaterializerRegistry.Register<{{schema.ClrTypeName}}Record>(Read);
        #endif

            internal static {{schema.ClrTypeName}}Record Read(System.Data.Common.DbDataReader reader) => new(
        {{string.Join(",\n", schema.Columns.Select(column => "        " + Read(column)))}});
        }
        """;

    private static string Read(FullNetColumn column)
    {
        var index = $"reader.GetOrdinal(\"{column.ClrPropertyName}\")";
        var value = column.ScalarType switch
        {
            FullNetScalarType.Uuid => $"reader.GetGuid({index})",
            FullNetScalarType.String => $"reader.GetString({index})",
            FullNetScalarType.Boolean => $"Convert.ToBoolean(reader.GetValue({index}), System.Globalization.CultureInfo.InvariantCulture)",
            FullNetScalarType.Int32 => $"Convert.ToInt32(reader.GetValue({index}), System.Globalization.CultureInfo.InvariantCulture)",
            FullNetScalarType.Int64 => $"Convert.ToInt64(reader.GetValue({index}), System.Globalization.CultureInfo.InvariantCulture)",
            FullNetScalarType.Decimal => $"Convert.ToDecimal(reader.GetValue({index}), System.Globalization.CultureInfo.InvariantCulture)",
            FullNetScalarType.DateTimeUtc => $"(reader.GetFieldType({index}) == typeof(DateTimeOffset) ? reader.GetFieldValue<DateTimeOffset>({index}).ToUniversalTime() : new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime({index}), DateTimeKind.Utc)))",
            _ => throw new ArgumentException("未知物化标量类型。"),
        };
        return column.IsNullable ? $"reader.IsDBNull({index}) ? null : {value}" : value;
    }
}
