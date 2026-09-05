using Full.NET.Localization;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Domain;

namespace Full.NET.UnitTests.Notifications;

[TestClass]
public sealed class NotificationTemplateLocaleResolverTests
{
    [TestMethod]
    public void NormalizeLocaleTag_accepts_supported_tags_and_aliases()
    {
        var chinese = NotificationTemplateLocaleResolver.NormalizeLocaleTag("zh-CN");
        var alias = NotificationTemplateLocaleResolver.NormalizeLocaleTag("zh");
        var english = NotificationTemplateLocaleResolver.NormalizeLocaleTag("en-US");

        Assert.IsTrue(chinese.IsSuccess);
        Assert.IsTrue(alias.IsSuccess);
        Assert.IsTrue(english.IsSuccess);
        Assert.AreEqual(LocaleCatalog.Chinese, chinese.Value);
        Assert.AreEqual(LocaleCatalog.Chinese, alias.Value);
        Assert.AreEqual(LocaleCatalog.English, english.Value);
    }

    [TestMethod]
    public void NormalizeLocaleTag_rejects_unsupported_or_invalid_tags()
    {
        var unsupported = NotificationTemplateLocaleResolver.NormalizeLocaleTag("fr-FR");
        var invalid = NotificationTemplateLocaleResolver.NormalizeLocaleTag("x");

        Assert.IsFalse(unsupported.IsSuccess);
        Assert.IsFalse(invalid.IsSuccess);
        Assert.AreEqual(NotificationsErrorCodes.TemplateValidationFailed, unsupported.Error!.Code);
        Assert.AreEqual(NotificationsErrorCodes.TemplateValidationFailed, invalid.Error!.Code);
    }

    [TestMethod]
    public void PickPublishedLocale_prefers_exact_match_then_alias_chain_then_default()
    {
        var published = new[] { LocaleCatalog.Chinese, LocaleCatalog.English };

        Assert.AreEqual(
            LocaleCatalog.English,
            NotificationTemplateLocaleResolver.PickPublishedLocale(
                published,
                LocaleCatalog.English,
                LocaleCatalog.Chinese));

        Assert.AreEqual(
            LocaleCatalog.Chinese,
            NotificationTemplateLocaleResolver.PickPublishedLocale(
                published,
                "zh",
                LocaleCatalog.English));

        Assert.AreEqual(
            LocaleCatalog.English,
            NotificationTemplateLocaleResolver.PickPublishedLocale(
                [LocaleCatalog.English],
                LocaleCatalog.Chinese,
                LocaleCatalog.Chinese));
    }

    [TestMethod]
    public void MissingSupportedLocales_lists_only_supported_tags_not_yet_published()
    {
        var missing = NotificationTemplateLocaleResolver.MissingSupportedLocales([LocaleCatalog.Chinese]);

        CollectionAssert.AreEquivalent(
            new[] { LocaleCatalog.English },
            missing.ToArray());
    }
}
