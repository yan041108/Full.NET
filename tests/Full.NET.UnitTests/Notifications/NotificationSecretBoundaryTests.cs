using Microsoft.Extensions.Configuration;
using Full.NET.Modules.Notifications.Providers.Smtp;

namespace Full.NET.UnitTests.Notifications;

/// <summary>验证进程环境变量不会因为数据库传入引用就自动获得通知提供程序访问权。</summary>
[TestClass]
public sealed class NotificationSecretBoundaryTests
{
    /// <summary>登记只授权单一提供程序，其他渠道不能复用同一个引用读取秘密。</summary>
    /// <param name="providerTypeKey">实际请求解析的提供程序。</param>
    /// <param name="allowed">是否与运维登记的提供程序匹配。</param>
    [TestMethod]
    [DataRow("email.smtp", true)]
    [DataRow("sms.aliyun", false)]
    [DataRow("email.smtp:0", false)]
    public async Task Registration_is_bound_to_exact_provider(string providerTypeKey, bool allowed)
    {
        var name = $"FULLNET_TEST_SECRET_{Guid.NewGuid():N}";
        try
        {
            Environment.SetEnvironmentVariable(name, "test-only-secret");
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Notifications:SecretReferences:email.smtp:0"] = $"env://{name}"
            }).Build();
            var resolver = new EnvironmentNotificationSecretResolver(configuration);
            var value = await resolver.ResolveAsync(providerTypeKey, $"env://{name}", CancellationToken.None);
            Assert.AreEqual(allowed ? "test-only-secret" : null, value);
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    /// <summary>没有运维登记的随机引用必须拒绝，测试只创建自己的临时秘密。</summary>
    [TestMethod]
    public async Task Unregistered_environment_reference_is_denied()
    {
        var name = $"FULLNET_TEST_SECRET_{Guid.NewGuid():N}";
        try
        {
            Environment.SetEnvironmentVariable(name, "test-only-secret");
            var resolver = new EnvironmentNotificationSecretResolver(new ConfigurationBuilder().Build());
            Assert.IsNull(await resolver.ResolveAsync("email.smtp", $"env://{name}", CancellationToken.None));
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }
}
