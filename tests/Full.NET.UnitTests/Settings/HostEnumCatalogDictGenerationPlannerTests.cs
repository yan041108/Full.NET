using Full.NET.Modules.Settings.Contracts;
using Full.NET.Modules.Settings.Features.ManageHostDictItems;
using Full.NET.Modules.Settings.Features.ManageHostDictTypes;
using Full.NET.Modules.Settings.Features.QueryHostEnumCatalogs;

namespace Full.NET.UnitTests.Settings;

/// <summary>
/// 枚举目录生成 Host 字典的计划器单元测试。
/// </summary>
[TestClass]
public sealed class HostEnumCatalogDictGenerationPlannerTests
{
    private static readonly Guid DictTypeId = Guid.Parse("018f1234-5678-7abc-8def-0123456789ab");

    [TestMethod]
    public void Plan_WhenDictTypeMissing_MarksAllMembersAsCreate()
    {
        var catalog = CreateCatalog(
            "settings.config_value_kind",
            [
                Member("string", "字符串", 0),
                Member("secret", "密钥", 1),
            ]);

        var preview = HostEnumCatalogDictGenerationPlanner.Plan(catalog, null, []);

        Assert.IsTrue(preview.WillCreateDictType);
        Assert.IsFalse(preview.DictTypeExists);
        Assert.AreEqual(2, preview.Items.Count);
        Assert.IsTrue(preview.Items.All(
            item => item.Action == EnumCatalogDictGenerationItemActions.Create));
        Assert.AreEqual(0, preview.UnmanagedItems.Count);
    }

    [TestMethod]
    public void Plan_WhenLabelMatches_SkipsExistingItem()
    {
        var catalog = CreateCatalog(
            "identity.account_type",
            [Member("normal_user", "普通用户", 30)]);

        var preview = HostEnumCatalogDictGenerationPlanner.Plan(
            catalog,
            ExistingType(),
            [ExistingItem("normal_user", "普通用户", 30)]);

        var item = preview.Items.Single();
        Assert.AreEqual(EnumCatalogDictGenerationItemActions.SkipExists, item.Action);
        Assert.AreEqual("普通用户", item.ExistingLabel);
    }

    [TestMethod]
    public void Plan_WhenLabelDiffers_ReportsConflictWithoutOverwrite()
    {
        var catalog = CreateCatalog(
            "identity.account_type",
            [Member("normal_user", "普通用户", 30)]);

        var preview = HostEnumCatalogDictGenerationPlanner.Plan(
            catalog,
            ExistingType(),
            [ExistingItem("normal_user", "人工标签", 30)]);

        var item = preview.Items.Single();
        Assert.AreEqual(EnumCatalogDictGenerationItemActions.ConflictLabel, item.Action);
        Assert.AreEqual("人工标签", item.ExistingLabel);
        Assert.AreEqual("普通用户", item.ProposedLabel);
    }

    [TestMethod]
    public void Plan_WhenValueInvalid_MarksInvalidValue()
    {
        var catalog = CreateCatalog(
            "demo.invalid",
            [Member("X", "非法值", 10)]);

        var preview = HostEnumCatalogDictGenerationPlanner.Plan(catalog, null, []);

        var item = preview.Items.Single();
        Assert.AreEqual(EnumCatalogDictGenerationItemActions.InvalidValue, item.Action);
    }

    [TestMethod]
    public void Plan_ListsUnmanagedDictItemsWithoutDeletingThem()
    {
        var catalog = CreateCatalog(
            "identity.account_type",
            [Member("normal_user", "普通用户", 30)]);

        var preview = HostEnumCatalogDictGenerationPlanner.Plan(
            catalog,
            ExistingType(),
            [
                ExistingItem("normal_user", "普通用户", 30),
                ExistingItem("legacy", "遗留项", 90),
            ]);

        Assert.AreEqual(1, preview.UnmanagedItems.Count);
        Assert.AreEqual("legacy", preview.UnmanagedItems[0].Value);
        Assert.AreEqual("遗留项", preview.UnmanagedItems[0].Label);
    }

    private static EnumCatalogDefinition CreateCatalog(
        string key,
        IReadOnlyList<EnumCatalogMemberDefinition> members) =>
        new(key, "显示名称", "描述", members);

    private static EnumCatalogMemberDefinition Member(
        string code,
        string label,
        int displayOrder) =>
        new(code, label, displayOrder);

    private static DictTypeIdentityRecord ExistingType() =>
        new(DictTypeId, "identity.account_type", "账号类型", "描述", 50, true, 1);

    private static DictItemIdentityRecord ExistingItem(
        string value,
        string label,
        int displayOrder) =>
        new(Guid.NewGuid(), DictTypeId, label, value, null, displayOrder, true, 1);
}
