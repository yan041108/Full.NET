using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Regions.Domain;
using Full.NET.Modules.Regions.Persistence;
using Full.NET.Seeding.Abstractions;

namespace Full.NET.Modules.Regions.Seeding;

/// <summary>
/// 为 Host 环境播种中国行政区域基线快照；数据来自嵌入式固定 JSON，按 <see cref="Code"/> 幂等写入。
/// </summary>
/// <param name="queryExecutor">查询当前模块既有基线数据。</param>
/// <param name="commandExecutor">写入当前模块的行政区域记录。</param>
/// <param name="clock">提供基线写入时间。</param>
/// <param name="idGenerator">生成应用端 UUID 标识。</param>
internal sealed partial class RegionsAdministrativeBaselineSeedContributor(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    IClock clock,
    IIdGenerator idGenerator) : IDataSeedContributor
{
    private const string BaselineResourceName =
        "Full.NET.Modules.Regions.Seeding.china-administrative-regions-baseline-v1.json";

    /// <summary>种子贡献者名称。</summary>
    public string Name => "regions.administrative_baseline";

    /// <summary>种子贡献者版本。</summary>
    public int Version => 1;

    /// <summary>仅在 Baseline Profile 下执行。</summary>
    public IReadOnlySet<SeedProfile> Profiles { get; } =
        new HashSet<SeedProfile> { SeedProfile.Baseline };

    /// <summary>无外部种子依赖。</summary>
    public IReadOnlyCollection<string> Dependencies { get; } = [];

    /// <summary>按编码幂等写入基线行政区域节点。</summary>
    /// <param name="context">种子执行上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<SeedContributionResult> SeedAsync(
        SeedContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var payload = await LoadBaselinePayloadAsync(cancellationToken).ConfigureAwait(false);
        var created = 0;
        var skipped = 0;
        var codeToId = new Dictionary<string, Guid>(StringComparer.Ordinal);

        foreach (var existing in await queryExecutor.QueryAsync<AdministrativeRegionCodeLinkRecord>(
                             AdministrativeRegionSql.ListCodeLinks,
                             cancellationToken: cancellationToken)
                         .ConfigureAwait(false))
        {
            codeToId[existing.Code] = existing.Id;
        }

        foreach (var item in payload.Items.OrderBy(item => item.Level).ThenBy(item => item.Code, StringComparer.Ordinal))
        {
            if (!AdministrativeRegionCodeRules.TryNormalize(item.Code, out var code)
                || string.IsNullOrWhiteSpace(item.Name)
                || !AdministrativeRegionCodeRules.IsValidLevel(item.Level))
            {
                skipped++;
                continue;
            }

            if (codeToId.ContainsKey(code))
            {
                skipped++;
                continue;
            }

            Guid? parentId = null;
            if (!string.IsNullOrWhiteSpace(item.ParentCode)
                && AdministrativeRegionCodeRules.TryNormalize(item.ParentCode, out var parentCode)
                && codeToId.TryGetValue(parentCode, out var resolvedParentId))
            {
                parentId = resolvedParentId;
            }

            var id = idGenerator.NewId();
            var now = clock.UtcNow;
            await commandExecutor.ExecuteAsync(
                    AdministrativeRegionSql.Insert,
                    RegionsSqlParameters.Create(
                        ("Id", id),
                        ("ParentId", parentId),
                        ("Code", code),
                        ("Name", item.Name.Trim()),
                        ("ShortName", TrimOptional(item.ShortName)),
                        ("MergerName", TrimOptional(item.MergerName)),
                        ("ZipCode", TrimOptional(item.ZipCode)),
                        ("CityCode", TrimOptional(item.CityCode)),
                        ("Level", item.Level),
                        ("RegionType", TrimOptional(item.RegionType)),
                        ("PinYin", TrimOptional(item.PinYin)),
                        ("Longitude", item.Longitude),
                        ("Latitude", item.Latitude),
                        ("DisplayOrder", item.DisplayOrder ?? 0),
                        ("Remark", null),
                        ("CreatedAtUtc", now),
                        ("Version", 1)),
                    cancellationToken)
                .ConfigureAwait(false);
            codeToId[code] = id;
            created++;
        }

        return created > 0
            ? new SeedContributionResult(created, 0, skipped, "seeding.data.created")
            : new SeedContributionResult(0, 0, skipped, "seeding.data.skipped");
    }

    /// <summary>读取嵌入式行政区域基线，通过静态 JSON 元数据反序列化。</summary>
    /// <param name="cancellationToken">取消当前操作的令牌。</param>
    private static async Task<BaselinePayload> LoadBaselinePayloadAsync(CancellationToken cancellationToken)
    {
        await using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(BaselineResourceName)
            ?? throw new InvalidOperationException($"Missing embedded seed resource: {BaselineResourceName}");
        var payload = await JsonSerializer.DeserializeAsync(
                stream,
                BaselineJsonContext.Default.BaselinePayload,
                cancellationToken)
            .ConfigureAwait(false);
        return payload ?? new BaselinePayload([]);
    }

    private static string? TrimOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private sealed record BaselinePayload(IReadOnlyList<BaselineItem> Items);

    private sealed record BaselineItem(
        string Code,
        string? ParentCode,
        string Name,
        string? ShortName,
        string? MergerName,
        string? ZipCode,
        string? CityCode,
        int Level,
        string? RegionType,
        string? PinYin,
        decimal? Longitude,
        decimal? Latitude,
        int? DisplayOrder);
    /// <summary>内嵌区划种子的闭合 JSON 元数据。</summary>
    [JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
    [JsonSerializable(typeof(BaselinePayload))]
    private partial class BaselineJsonContext : JsonSerializerContext;
}
