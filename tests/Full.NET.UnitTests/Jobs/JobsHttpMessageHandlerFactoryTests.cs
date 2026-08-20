using Full.NET.Modules.Jobs.Execution;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.Jobs;

[TestClass]
public sealed class JobsHttpMessageHandlerFactoryTests
{
    [TestMethod]
    public void Create_BindsConnectionsThroughSsrfValidatedCallback()
    {
        using var handler = JobsHttpMessageHandlerFactory.Create(
            Options.Create(new JobsHttpOptions()));

        Assert.IsFalse(handler.AllowAutoRedirect);
        Assert.IsFalse(handler.UseProxy);
        Assert.IsNotNull(handler.ConnectCallback);
    }
}
