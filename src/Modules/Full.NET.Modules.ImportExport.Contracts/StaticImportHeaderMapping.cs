using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Full.NET.Modules.ImportExport.Contracts;

/// <summary>两个工作簿消费者共用的纯表头策略，不读取文件、不执行转换或业务授权。</summary>
public static class StaticImportHeaderMapping
{
    /// <summary>显式映射模式最多读取的来源列数。</summary>
    public const int MaximumSourceColumns = 32;
    /// <summary>来源表头规范化后的最大字符数。</summary>
    public const int MaximumSourceHeaderLength = 128;
    /// <summary>规范 JSON 配置的 UTF-8 字节上限。</summary>
    public const int MaximumConfigurationBytes = 16 * 1024;

    /// <summary>对有序静态目标表头取指纹；它不表示业务校验规则的版本。</summary>
    /// <param name="headerColumns">代码注册的非空、唯一、最多三十二个目标列。</param>
    /// <returns>小写十六进制 SHA-256 指纹。</returns>
    /// <exception cref="ArgumentException">注册表头为空、重复或超出边界。</exception>
    public static string Fingerprint(IReadOnlyList<string> headerColumns)
    {
        ArgumentNullException.ThrowIfNull(headerColumns);
        if (!ValidTargets(headerColumns)) throw new ArgumentException("静态目标表头必须唯一且有界。", nameof(headerColumns));
        var bytes = Encoding.UTF8.GetBytes("header-map-v1\0" + string.Join('\0', headerColumns));
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    /// <summary>只接受当前完整目标集合，按静态目标顺序归一化，不补齐或猜测缺失映射。</summary>
    /// <param name="expectedHeaders">当前代码注册的目标表头。</param>
    /// <param name="request">客户端看到的目标集合及字面来源名称。</param>
    /// <param name="normalized">成功时返回独立只读副本，失败时为空。</param>
    /// <returns>字段、唯一性、长度与序列化体积均合法时为真。</returns>
    public static bool TryNormalize(IReadOnlyList<string> expectedHeaders, ImportColumnMappingRequest? request,
        [NotNullWhen(true)] out ImportColumnMappingRequest? normalized)
    {
        ArgumentNullException.ThrowIfNull(expectedHeaders); normalized = null;
        if (!ValidTargets(expectedHeaders) || request?.HeaderColumns is null || request.Columns is null
            || !request.HeaderColumns.SequenceEqual(expectedHeaders, StringComparer.Ordinal) || request.Columns.Count != expectedHeaders.Count)
            return false;
        var targets = new HashSet<string>(expectedHeaders, StringComparer.Ordinal);
        var aliases = new HashSet<string>(StringComparer.Ordinal);
        var columns = new Dictionary<string, ImportColumnMapping>(StringComparer.Ordinal);
        foreach (var mapping in request.Columns)
        {
            if (mapping is null || mapping.TargetColumn is null || !targets.Contains(mapping.TargetColumn)) return false;
            var name = mapping.SourceHeader?.Trim();
            if (string.IsNullOrEmpty(name) || name.Length > MaximumSourceHeaderLength || name.Any(char.IsControl)
                || !aliases.Add(name) || !columns.TryAdd(mapping.TargetColumn, new(mapping.TargetColumn, name))) return false;
        }
        var value = new ImportColumnMappingRequest(Array.AsReadOnly(expectedHeaders.ToArray()),
            Array.AsReadOnly(expectedHeaders.Select(header => columns[header]).ToArray()));
        var json = JsonSerializer.Serialize(value, ImportHeaderMappingJsonContext.Default.ImportColumnMappingRequest);
        if (Encoding.UTF8.GetByteCount(json) > MaximumConfigurationBytes) return false;
        normalized = value; return true;
    }

    /// <summary>将文件首行绑定到目标顺序；重复或缺失来源表头拒绝执行，不按位置回退。</summary>
    /// <param name="expectedHeaders">当前静态目标表头。</param>
    /// <param name="sourceHeaders">首行按物理列顺序读取的至多三十二个单元格。</param>
    /// <param name="configuration">已经选定的映射；方法仍会重新验证当前目标集合。</param>
    /// <returns>与目标列顺序相同的、从零开始的来源列位置。</returns>
    /// <exception cref="StaticImportHeaderMappingException">映射不完整、来源表头重复、缺失或超出解析预算。</exception>
    public static IReadOnlyList<int> Bind(IReadOnlyList<string> expectedHeaders, IReadOnlyList<string> sourceHeaders,
        ImportColumnMappingRequest configuration)
    {
        ArgumentNullException.ThrowIfNull(sourceHeaders);
        if (sourceHeaders.Count is < 1 or > MaximumSourceColumns || !TryNormalize(expectedHeaders, configuration, out var normalized))
            throw new StaticImportHeaderMappingException();
        var positions = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < sourceHeaders.Count; index++)
        {
            var header = sourceHeaders[index]?.Trim(); if (string.IsNullOrEmpty(header)) continue;
            if (header.Length > MaximumSourceHeaderLength || header.Any(char.IsControl) || !positions.TryAdd(header, index)) throw new StaticImportHeaderMappingException();
        }
        var layout = new int[normalized.Columns.Count];
        for (var index = 0; index < normalized.Columns.Count; index++)
            if (!positions.TryGetValue(normalized.Columns[index].SourceHeader, out layout[index])) throw new StaticImportHeaderMappingException();
        return Array.AsReadOnly(layout);
    }

    private static bool ValidTargets(IReadOnlyList<string> headers) => headers.Count is >= 1 and <= MaximumSourceColumns
        && headers.All(header => !string.IsNullOrWhiteSpace(header) && header.Length <= 64 && header == header.Trim() && !header.Any(char.IsControl))
        && headers.Distinct(StringComparer.Ordinal).Count() == headers.Count;
}

/// <summary>工作簿表头与受控映射不符；上层将其映射为稳定错误码，不把文件文字带入错误消息。</summary>
public sealed class StaticImportHeaderMappingException : Exception
{
    /// <summary>创建不携带用户内容的表头绑定错误。</summary>
    public StaticImportHeaderMappingException() : base("工作簿表头与当前列映射不符。") { }
    /// <summary>创建指定诊断信息的表头绑定错误。</summary>
    public StaticImportHeaderMappingException(string message) : base(message) { }
    /// <summary>保留底层原因供受控诊断，传输层仍只输出稳定错误码。</summary>
    public StaticImportHeaderMappingException(string message, Exception innerException) : base(message, innerException) { }
}

// 合同内的体积检查也保持静态序列化闭包，不依赖 API 宿主的反射选项。
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(ImportColumnMappingRequest))]
internal partial class ImportHeaderMappingJsonContext : JsonSerializerContext;
