using System.Globalization;
using System.Resources;
using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Localization;
using Full.NET.Modules.Identity;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.UnitTests.Localization;

[TestClass]
public sealed class ErrorResourceCompletenessTests
{
    private static readonly CultureInfo[] RequiredCultures =
    [
        CultureInfo.GetCultureInfo("zh-CN"),
        CultureInfo.GetCultureInfo("en-US"),
    ];

    [TestMethod]
    public void Common_and_validation_error_codes_have_all_required_resources()
    {
        var manager = new ResourceManager(
            "Full.NET.Hosting.Resources.CommonErrors",
            typeof(StandardApiResultMapper).Assembly);

        AssertResources(
            manager,
            CommonErrorCodes.All
                .Concat(ValidationErrorCodes.All)
                .Concat(LocalizationErrorCodes.All));
    }

    [TestMethod]
    public void Identity_error_codes_have_all_required_resources()
    {
        var manager = new ResourceManager(
            "Full.NET.Modules.Identity.Resources.IdentityErrors",
            typeof(IdentityModule).Assembly);

        AssertResources(manager, IdentityErrorCodes.All);
    }

    [TestMethod]
    public void Tenancy_error_codes_have_all_required_resources()
    {
        // 资源嵌入 Tenancy Core 程序集；TenancyModule 已迁至 .Http，需用 Core 类型定位资源。
        var manager = new ResourceManager(
            "Full.NET.Modules.Tenancy.Resources.TenancyErrors",
            typeof(TenancyErrorCodes).Assembly);

        AssertResources(manager, TenancyErrorCodes.All);
    }

    private static void AssertResources(
        ResourceManager manager,
        IEnumerable<string> codes)
    {
        foreach (var culture in RequiredCultures)
        {
            var resourceSet = manager.GetResourceSet(
                culture,
                createIfNotExists: true,
                tryParents: false);
            Assert.IsNotNull(
                resourceSet,
                $"资源 {manager.BaseName} 缺少精确文化 {culture.Name} 的 ResourceSet。");

            foreach (var code in codes)
            {
                Assert.IsFalse(
                    string.IsNullOrWhiteSpace(
                        resourceSet.GetString(code, ignoreCase: false)),
                    $"资源 {manager.BaseName} 缺少 {culture.Name}/{code}。");
            }
        }
    }
}
