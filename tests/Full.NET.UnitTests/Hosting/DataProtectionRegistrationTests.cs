using Full.NET.Hosting.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Hosting;

[TestClass]
public sealed class DataProtectionRegistrationTests
{
    [TestMethod]
    public void Production_rejects_temporary_key_ring_path()
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(Environments.Production);
        environment.ContentRootPath.Returns(Path.GetFullPath("."));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataProtection:ApplicationName"] = "Full.NET",
                ["DataProtection:KeyRingPath"] = Path.Combine(Path.GetTempPath(), "fullnet-keys"),
                ["DataProtection:CertificatePath"] = Path.Combine(Path.GetTempPath(), "missing.pfx"),
            })
            .Build();

        var services = new ServiceCollection();
        Assert.ThrowsExactly<OptionsValidationException>(() =>
            services.AddFullNetDataProtection(configuration, environment));
    }

    [TestMethod]
    public void Production_requires_certificate_path()
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(Environments.Production);
        environment.ContentRootPath.Returns(Path.GetFullPath("."));
        var rooted = Path.GetFullPath(Path.Combine("App_Data", "dp-keys-prod-test"));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataProtection:ApplicationName"] = "Full.NET",
                ["DataProtection:KeyRingPath"] = rooted,
            })
            .Build();

        var services = new ServiceCollection();
        Assert.ThrowsExactly<OptionsValidationException>(() =>
            services.AddFullNetDataProtection(configuration, environment));
    }

    [TestMethod]
    public void Development_registers_shared_application_name_without_certificate()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "fullnet-dp-reg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var environment = Substitute.For<IHostEnvironment>();
            environment.EnvironmentName.Returns(Environments.Development);
            environment.ContentRootPath.Returns(root);
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["DataProtection:ApplicationName"] = "Full.NET.Shared",
                    ["DataProtection:KeyRingPath"] = "App_Data/data-protection-keys",
                })
                .Build();

            var services = new ServiceCollection();
            services.AddSingleton(environment);
            services.AddFullNetDataProtection(configuration, environment);
            using var provider = services.BuildServiceProvider();
            var options = provider.GetRequiredService<IOptions<DataProtectionOptions>>().Value;
            Assert.AreEqual("Full.NET.Shared", options.ApplicationName);
            Assert.IsTrue(Directory.Exists(
                Path.Combine(root, "App_Data", "data-protection-keys")));
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
                // 测试清理尽力而为。
            }
        }
    }
}
