using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Calendar;

namespace Full.NET.IntegrationTests.Api;

[TestClass]
public sealed class CalendarApiMySqlTests
{
    [TestMethod]
    public async Task Personal_schedule_lifecycle_with_mysql()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await CalendarPersonalScheduleAssertions.VerifyAsync(factory);
    }
}
