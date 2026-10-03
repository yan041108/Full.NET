using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Full.NET.UnitTests.Data;

[TestClass]
public sealed class DapperServiceRegistrationTests
{
    [TestMethod]
    public void AddFullNetDapper_RegistersOutboxIdAndClockWithoutBusinessModules()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "SqlServer",
                ["Database:ConnectionString"] = "Server=localhost;Database=fullnet",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddFullNetDapper(configuration, Environments.Development);

        using var provider = services.BuildServiceProvider();
        Assert.IsInstanceOfType<GuidV7IdGenerator>(provider.GetRequiredService<IIdGenerator>());
        Assert.IsInstanceOfType<SystemClock>(provider.GetRequiredService<IClock>());
    }
}
