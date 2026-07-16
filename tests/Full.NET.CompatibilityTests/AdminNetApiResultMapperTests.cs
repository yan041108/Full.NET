using Full.NET.Abstractions.Results;
using Full.NET.Compatibility.AdminNet;
using Full.NET.Hosting.Api;
using Full.NET.Hosting.Observability;
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
        var mapped = new AdminNetApiResultMapper().Map(
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
    }

    [TestMethod]
    public void Conflict_PreservesRealHttp409AndFullNetErrorCode()
    {
        var context = new DefaultHttpContext();
        var mapped = new AdminNetApiResultMapper().Map(
            Result<string>.Failure(new Error(
                "tenancy.identifier-exists",
                "Identifier exists.",
                ErrorType.Conflict)),
            context);

        Assert.AreEqual(StatusCodes.Status409Conflict, ((IStatusCodeHttpResult)mapped).StatusCode);
        var envelope = (AdminNetEnvelope<string>?)((IValueHttpResult)mapped).Value;
        Assert.IsNotNull(envelope);
        Assert.IsFalse(envelope.Success);
        Assert.AreEqual("tenancy.identifier-exists", envelope.Code);
        Assert.AreEqual("Identifier exists.", envelope.Message);
        Assert.IsNull(envelope.Data);
        Assert.IsFalse(string.IsNullOrWhiteSpace(envelope.TraceId));
    }

    [TestMethod]
    public void Registration_IsExplicitAndReplacesOnlyTheApiMapper()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IApiResultMapper, StandardApiResultMapper>();

        services.AddAdminNetCompatibility();

        Assert.AreEqual(1, services.Count(item => item.ServiceType == typeof(IApiResultMapper)));
        using var provider = services.BuildServiceProvider();
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
}
