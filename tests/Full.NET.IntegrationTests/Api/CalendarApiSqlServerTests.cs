using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Calendar;

namespace Full.NET.IntegrationTests.Api;

/// <summary>集成测试：CalendarApiSqlServer。</summary>
[TestClass]
public sealed class CalendarApiSqlServerTests
{
    [TestMethod]
    public async Task Personal_schedule_lifecycle_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

        await CalendarPersonalScheduleAssertions.VerifyAsync(factory);
    }
}
