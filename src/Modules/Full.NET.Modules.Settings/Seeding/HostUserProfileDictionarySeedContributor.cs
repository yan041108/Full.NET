using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Settings.Features.ManageHostDictItems;
using Full.NET.Modules.Settings.Features.ManageHostDictTypes;
using Full.NET.Modules.Settings.Persistence;
using Full.NET.Seeding.Abstractions;

namespace Full.NET.Modules.Settings.Seeding;

/// <summary>
/// 为 Host 用户档案表单初始化证件、民族、学历与紧急联系人关系等字典基线。
/// </summary>
internal sealed class HostUserProfileDictionarySeedContributor(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    IClock clock,
    IIdGenerator idGenerator) : IDataSeedContributor
{
    internal static class DictTypeCodes
    {
        public const string IdCardType = "identity.id_card_type";
        public const string Ethnicity = "identity.ethnicity";
        public const string EducationLevel = "identity.education_level";
        public const string EmergencyContactRelation = "identity.emergency_contact_relation";
        public const string AccountType = "identity.account_type";
    }

    public string Name => "settings.host_user_profile_dictionaries";

    public int Version => 1;

    public IReadOnlySet<SeedProfile> Profiles { get; } =
        new HashSet<SeedProfile> { SeedProfile.Baseline };

    public IReadOnlyCollection<string> Dependencies { get; } = [];

    public async Task<SeedContributionResult> SeedAsync(
        SeedContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var created = 0;
        var skipped = 0;
        foreach (var definition in Definitions)
        {
            var (typeCreated, typeSkipped) = await EnsureDictTypeAsync(
                    definition,
                    cancellationToken)
                .ConfigureAwait(false);
            created += typeCreated;
            skipped += typeSkipped;
        }

        if (created > 0)
        {
            return new SeedContributionResult(created, 0, skipped, "seeding.data.created");
        }

        return new SeedContributionResult(0, 0, skipped, "seeding.data.skipped");
    }

    private async Task<(int Created, int Skipped)> EnsureDictTypeAsync(
        DictTypeSeedDefinition definition,
        CancellationToken cancellationToken)
    {
        var existing = await queryExecutor.QuerySingleOrDefaultAsync<DictTypeIdentityRecord>(
                DictTypeSql.FindByCode,
                SettingsSqlParameters.Create(("Code", definition.Code)),
                cancellationToken)
            .ConfigureAwait(false);

        Guid dictTypeId;
        var created = 0;
        var skipped = 0;
        if (existing is null)
        {
            dictTypeId = idGenerator.NewId();
            var now = clock.UtcNow;
            await commandExecutor.ExecuteAsync(
                    DictTypeSql.Insert,
                    SettingsSqlParameters.Create(
                        ("Id", dictTypeId),
                        ("Code", definition.Code),
                        ("Name", definition.Name),
                        ("Description", definition.Description),
                        ("DisplayOrder", definition.DisplayOrder),
                        ("IsActive", true),
                        ("CreatedAtUtc", now),
                        ("Version", 1)
                    ),
                    cancellationToken)
                .ConfigureAwait(false);
            created++;
        }
        else
        {
            dictTypeId = existing.Id;
            skipped++;
        }

        foreach (var item in definition.Items)
        {
            var existingItem = await queryExecutor.QuerySingleOrDefaultAsync<DictItemIdentityRecord>(
                    DictItemSql.FindByTypeAndValue,
                    SettingsSqlParameters.Create(
                        ("DictTypeId", dictTypeId),
                        ("Value", item.Value)
                    ),
                    cancellationToken)
                .ConfigureAwait(false);
            if (existingItem is not null)
            {
                skipped++;
                continue;
            }

            await commandExecutor.ExecuteAsync(
                    DictItemSql.Insert,
                    SettingsSqlParameters.Create(
                        ("Id", idGenerator.NewId()),
                        ("DictTypeId", dictTypeId),
                        ("Label", item.Label),
                        ("Value", item.Value),
                        ("Color", item.Color),
                        ("DisplayOrder", item.DisplayOrder),
                        ("IsActive", true),
                        ("CreatedAtUtc", clock.UtcNow),
                        ("Version", 1)
                    ),
                    cancellationToken)
                .ConfigureAwait(false);
            created++;
        }

        return (created, skipped);
    }

    private static readonly DictTypeSeedDefinition[] Definitions =
    [
        new(
            DictTypeCodes.IdCardType,
            "\u8bc1\u4ef6\u7c7b\u578b",
            "Host \u7528\u6237\u6863\u6848\u8bc1\u4ef6\u7c7b\u578b",
            10,
            [
                Item("\u5c45\u6c11\u8eab\u4efd\u8bc1", "id_card", 10),
                Item("\u62a4\u7167", "passport", 20),
                Item("\u6e2f\u6fb3\u5c45\u6c11\u6765\u5f80\u5185\u5730\u901a\u884c\u8bc1", "hk_macau_pass", 30),
                Item("\u53f0\u6e7e\u5c45\u6c11\u6765\u5f80\u5927\u9646\u901a\u884c\u8bc1", "taiwan_pass", 40),
                Item("\u519b\u5b98\u8bc1", "military_id", 50),
                Item("\u5176\u4ed6", "other", 90),
            ]),
        new(
            DictTypeCodes.Ethnicity,
            "\u6c11\u65cf",
            "Host \u7528\u6237\u6863\u6848\u6c11\u65cf",
            20,
            [
                Item("\u6c49\u65cf", "han", 10),
                Item("\u58ee\u65cf", "zhuang", 20),
                Item("\u56de\u65cf", "hui", 30),
                Item("\u6ee1\u65cf", "manchu", 40),
                Item("\u7ef4\u543e\u5c14\u65cf", "uyghur", 50),
                Item("\u82d7\u65cf", "miao", 60),
                Item("\u5f5d\u65cf", "yi", 70),
                Item("\u571f\u5bb6\u65cf", "tujia", 80),
                Item("\u85cf\u65cf", "tibetan", 90),
                Item("\u8499\u53e4\u65cf", "mongol", 100),
                Item("\u4f97\u65cf", "dong", 110),
                Item("\u5e03\u4f9d\u65cf", "buyei", 120),
                Item("\u7476\u65cf", "yao", 130),
                Item("\u671d\u9c9c\u65cf", "korean", 140),
                Item("\u767d\u65cf", "bai", 150),
                Item("\u54c8\u5c3c\u65cf", "hani", 160),
                Item("\u9ece\u65cf", "li", 170),
                Item("\u54c8\u8428\u514b\u65cf", "kazak", 180),
                Item("\u50a3\u65cf", "dai", 190),
                Item("\u7572\u65cf", "she", 200),
                Item("\u5176\u4ed6", "other", 990),
            ]),
        new(
            DictTypeCodes.EducationLevel,
            "\u6587\u5316\u7a0b\u5ea6",
            "Host \u7528\u6237\u6863\u6848\u6587\u5316\u7a0b\u5ea6",
            30,
            [
                Item("\u5c0f\u5b66", "primary", 10),
                Item("\u521d\u4e2d", "junior_high", 20),
                Item("\u9ad8\u4e2d/\u4e2d\u4e13", "senior_high", 30),
                Item("\u5927\u4e13", "associate", 40),
                Item("\u672c\u79d1", "bachelor", 50),
                Item("\u7855\u58eb", "master", 60),
                Item("\u535a\u58eb", "doctor", 70),
                Item("\u5176\u4ed6", "other", 90),
            ]),
        new(
            DictTypeCodes.EmergencyContactRelation,
            "\u8054\u7cfb\u4eba\u5173\u7cfb",
            "Host \u7528\u6237\u7d27\u6025\u8054\u7cfb\u4eba\u5173\u7cfb",
            40,
            [
                Item("\u914d\u5076", "spouse", 10),
                Item("\u7236\u6bcd", "parent", 20),
                Item("\u5b50\u5973", "child", 30),
                Item("\u5144\u5f1f\u59d0\u59b9", "sibling", 40),
                Item("\u5176\u4ed6\u4eb2\u5c5e", "relative", 50),
                Item("\u540c\u4e8b", "colleague", 60),
                Item("\u670b\u53cb", "friend", 70),
                Item("\u5176\u4ed6", "other", 90),
            ]),
        new(
            DictTypeCodes.AccountType,
            "\u8d26\u53f7\u7c7b\u578b",
            "Host \u7528\u6237\u8d26\u53f7\u7c7b\u578b\uff0c\u4e0e identity.account_type \u679a\u4e3e\u76ee\u5f55\u540c\u6b65",
            50,
            [
                Item("\u8d85\u7ea7\u7ba1\u7406\u5458", IdentityAccountTypes.SuperAdmin, 10),
                Item("\u7cfb\u7edf\u7ba1\u7406\u5458", IdentityAccountTypes.SysAdmin, 20),
                Item("\u666e\u901a\u7528\u6237", IdentityAccountTypes.NormalUser, 30),
            ]),
    ];

    private static DictItemSeedDefinition Item(
        string label,
        string value,
        int displayOrder,
        string? color = null) =>
        new(label, value, displayOrder, color);

    private sealed record DictTypeSeedDefinition(
        string Code,
        string Name,
        string Description,
        int DisplayOrder,
        IReadOnlyList<DictItemSeedDefinition> Items);

    private sealed record DictItemSeedDefinition(
        string Label,
        string Value,
        int DisplayOrder,
        string? Color);
}