using System.Net;
using Full.NET.Modules.Jobs.Execution;

namespace Full.NET.UnitTests.Jobs;

[TestClass]
public sealed class HttpSsrfGuardTests
{
    [TestMethod]
    public async Task ValidateAsync_BlocksLoopback_WhenPrivateNetworkDisabled()
    {
        var uri = new Uri("http://127.0.0.1/ping");

        var (allowed, reason) = await HttpSsrfGuard.ValidateAsync(uri, allowPrivateNetwork: false, CancellationToken.None);

        Assert.IsFalse(allowed);
        Assert.IsNotNull(reason);
    }

    [TestMethod]
    public async Task ValidateAsync_AllowsLoopback_WhenPrivateNetworkEnabled()
    {
        var uri = new Uri("http://127.0.0.1/ping");

        var (allowed, reason) = await HttpSsrfGuard.ValidateAsync(uri, allowPrivateNetwork: true, CancellationToken.None);

        Assert.IsTrue(allowed);
        Assert.IsNull(reason);
    }

    [TestMethod]
    public async Task ValidateAsync_BlocksPrivateRfc1918_WhenPrivateNetworkDisabled()
    {
        var uri = new Uri("http://10.0.0.1/health");

        var (allowed, reason) = await HttpSsrfGuard.ValidateAsync(uri, allowPrivateNetwork: false, CancellationToken.None);

        Assert.IsFalse(allowed);
        Assert.IsNotNull(reason);
    }

    [TestMethod]
    public async Task ValidateAsync_RejectsUrlWithUserInfo()
    {
        var uri = new Uri("https://user:pass@example.com/health");

        var (allowed, reason) = await HttpSsrfGuard.ValidateAsync(uri, allowPrivateNetwork: false, CancellationToken.None);

        Assert.IsFalse(allowed);
        Assert.Contains("credentials", reason!, StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public async Task ValidateAsync_BlocksMetadataAddress_WhenPrivateNetworkDisabled()
    {
        var uri = new Uri("http://169.254.169.254/latest/meta-data/");

        var (allowed, reason) = await HttpSsrfGuard.ValidateAsync(uri, allowPrivateNetwork: false, CancellationToken.None);

        Assert.IsFalse(allowed);
        Assert.IsNotNull(reason);
    }

    [DataRow("http://0.0.0.0/health")]
    [DataRow("http://[::]/health")]
    [TestMethod]
    public async Task ValidateAsync_BlocksUnspecifiedAddress_WhenPrivateNetworkDisabled(
        string url)
    {
        var (allowed, reason) = await HttpSsrfGuard.ValidateAsync(
            new Uri(url),
            allowPrivateNetwork: false,
            CancellationToken.None);

        Assert.IsFalse(allowed);
        Assert.IsNotNull(reason);
    }

    [DataRow("http://[::ffff:10.0.0.1]/health")]
    [DataRow("http://[::ffff:192.168.1.1]/health")]
    [TestMethod]
    public async Task ValidateAsync_BlocksIpv4MappedPrivateAddress_WhenPrivateNetworkDisabled(
        string url)
    {
        var (allowed, reason) = await HttpSsrfGuard.ValidateAsync(
            new Uri(url),
            allowPrivateNetwork: false,
            CancellationToken.None);

        Assert.IsFalse(allowed);
        Assert.IsNotNull(reason);
    }
}
