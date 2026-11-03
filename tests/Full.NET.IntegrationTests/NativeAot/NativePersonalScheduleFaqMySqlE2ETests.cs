using Full.NET.Data.Abstractions;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>浏览器桥接绑定修复后的MySQL独立补验；不重跑已通过的SQL Server身份。</summary>
[TestClass] [DoNotParallelize] [TestCategory("NativeAotE2E")]
public sealed class NativePersonalScheduleFaqMySqlE2ETests
{
    [TestMethod]
    public Task Native_business_faq_mysql_and_formal_browser_preserve_owner_only_boundary() =>
        NativeHumanKnowledgePublicationE2ETests.VerifyAsync(DatabaseProvider.MySql, true,
            verifyPersonalScheduleAnswer: true, verifyPersonalScheduleFaq: true);
}
