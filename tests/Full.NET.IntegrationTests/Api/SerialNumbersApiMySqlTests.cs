using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.SerialNumbers;

namespace Full.NET.IntegrationTests.Api;

/// <summary>集成测试：SerialNumbersApiMySql。</summary>
[TestClass]
public sealed class SerialNumbersApiMySqlTests
{
    [TestMethod]
    public async Task Allocation_is_atomic_with_mysql()
    {
        var clock = new SerialNumberAllocationAssertions.MutableClock(
            DateTimeOffset.UtcNow);
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync(),
            configureTestServices: services =>
                SerialNumberAllocationAssertions.ConfigureClock(
                    services,
                    clock));

        await SerialNumberRuleManagementAssertions.VerifyAsync(factory);
        await SerialNumberAllocationAssertions.VerifyAsync(factory, clock);
    }
}
