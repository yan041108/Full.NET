using System.Globalization;
using Full.NET.Abstractions.Results;
using Full.NET.Compatibility.AdminNet;
using Full.NET.Hosting.Api;
using Full.NET.Hosting.Observability;
using Full.NET.Localization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Full.NET.CompatibilityTests;

[TestClass]
public sealed class AdminNetApiResultMapperTests
{
    [TestMethod]
    public void Success_UsesRealHttp200AndCompatibilityEnvelope()
    {
        var context = new DefaultHttpContext();
        var mapped = CreateMapper("zh-CN").Map(
            Result<string>.Success("ok"),
            context);

        Assert.AreEqual(StatusCodes.Status200OK, ((IStatusCodeHttpResult)mapped).StatusCode);
        var envelope = (AdminNetEnvelope<string>?)((IValueHttpResult)mapped).Value;
        Assert.IsNotNull(envelope);
        Assert.IsTrue(envelope.Success);
        Assert.AreEqual("success", envelope.Code);
        Assert.IsNull(envelope.Message);
        Assert.AreEqual("ok", envelope.Data);
        Assert.AreEqual(context.TraceIdentifier, envelope.TraceId);
        Assert.IsFalse(context.Response.Headers.ContainsKey("Content-Language"));
        Assert.IsFalse(context.Response.Headers.ContainsKey("Vary"));
    }

    [TestMethod]
    public void Conflict_PreservesRealHttp409AndFullNetErrorCode()
    {
        var context = new DefaultHttpContext();
        var mapped = CreateMapper("en-US").Map(
            Result<string>.Failure(new Error(
                Code: "tenancy.identifier-exists",
                Message: "Identifier exists.",
                Type: ErrorType.Conflict)),
            context);

        Assert.AreEqual(StatusCodes.Status409Conflict, ((IStatusCodeHttpResult)mapped).StatusCode);
        var envelope = (AdminNetEnvelope<string>?)((IValueHttpResult)mapped).Value;
        Assert.IsNotNull(envelope);
        Assert.IsFalse(envelope.Success);
        Assert.AreEqual("tenancy.identifier-exists", envelope.Code);
        Assert.AreEqual(
            "A tenant with this identifier already exists.",
            envelope.Message);
        Assert.IsNull(envelope.Data);
        Assert.IsFalse(string.IsNullOrWhiteSpace(envelope.TraceId));
    }

    [TestMethod]
    public void Failure_localizes_only_message_and_preserves_envelope_shape()
    {
        var error = new Error(
            Code: "tenancy.identifier-exists",
            Message: "A tenant with this identifier already exists.",
            Type: ErrorType.Conflict);
        var chineseContext = new DefaultHttpContext();
        var englishContext = new DefaultHttpContext();

        var chinese = (AdminNetEnvelope<string>?)((IValueHttpResult)
            CreateMapper("zh-CN").Map(Result<string>.Failure(error), chineseContext)).Value;
        var english = (AdminNetEnvelope<string>?)((IValueHttpResult)
            CreateMapper("en-US").Map(Result<string>.Failure(error), englishContext)).Value;

        Assert.IsNotNull(chinese);
        Assert.IsNotNull(english);
        Assert.AreEqual(chinese.Code, english.Code);
        Assert.AreEqual("已存在使用该标识的租户。", chinese.Message);
        Assert.AreEqual(
            "A tenant with this identifier already exists.",
            english.Message);
        Assert.AreEqual("zh-CN", chineseContext.Response.Headers.ContentLanguage.ToString());
        Assert.AreEqual("en-US", englishContext.Response.Headers.ContentLanguage.ToString());
        StringAssert.Contains(
            chineseContext.Response.Headers.Vary.ToString(),
            "Accept-Language",
            StringComparison.OrdinalIgnoreCase);
        CollectionAssert.AreEquivalent(
            new[] { "Success", "Code", "Message", "Data", "TraceId" },
            typeof(AdminNetEnvelope<string>).GetProperties().Select(property => property.Name).ToArray());
    }

    [TestMethod]
    public void Registration_IsExplicitAndReplacesOnlyTheApiMapper()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddFullNetServiceDefaults();

        builder.Services.AddAdminNetCompatibility();

        Assert.AreEqual(
            1,
            builder.Services.Count(item => item.ServiceType == typeof(IApiResultMapper)));
        using var provider = builder.Services.BuildServiceProvider();
        Assert.IsInstanceOfType<AdminNetApiResultMapper>(
            provider.GetRequiredService<IApiResultMapper>());
    }

    [TestMethod]
    public void ServiceDefaults_DoNotEnableCompatibilityImplicitly()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddFullNetServiceDefaults();

        Assert.IsFalse(builder.Services.Any(descriptor =>
            descriptor.ServiceType == typeof(IApiResultMapper)
            && descriptor.ImplementationType == typeof(AdminNetApiResultMapper)));
    }

    private static AdminNetApiResultMapper CreateMapper(string locale)
    {
        var source = new DictionaryResourceSource(locale);
        return new AdminNetApiResultMapper(
            new ResourceErrorMessageLocalizer([source], new NamedMessageFormatter()),
            new StubLocaleContext(locale));
    }

    private sealed class StubLocaleContext(string locale) : ILocaleContext
    {
        public string CurrentLocale => locale;
    }

    private sealed class DictionaryResourceSource(string locale) : IErrorResourceSource
    {
        public string Prefix => "tenancy.";

        public bool TryGetTemplate(
            string code,
            CultureInfo culture,
            out string template)
        {
            template = string.Equals(locale, "zh-CN", StringComparison.Ordinal)
                ? "已存在使用该标识的租户。"
                : "A tenant with this identifier already exists.";
            return string.Equals(
                code,
                "tenancy.identifier-exists",
                StringComparison.Ordinal);
        }
    }
}
