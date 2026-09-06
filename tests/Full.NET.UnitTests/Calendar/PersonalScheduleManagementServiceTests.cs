using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Calendar.Contracts;
using Full.NET.Modules.Calendar.Features.ManageMyPersonalSchedules;

namespace Full.NET.UnitTests.Calendar;

[TestClass]
public sealed class PersonalScheduleManagementServiceTests
{
    [TestMethod]
    public void ValidateContent_RejectsBlankOrOversizedValues()
    {
        var blank = PersonalScheduleManagementService.ValidateContent("   ");
        Assert.IsFalse(blank.IsSuccess);
        Assert.AreEqual(
            CalendarErrorCodes.PersonalScheduleInvalidContent,
            blank.Error?.Code);

        var oversized = PersonalScheduleManagementService.ValidateContent(
            new string('x', PersonalScheduleManagementService.MaxContentLength + 1));
        Assert.IsFalse(oversized.IsSuccess);
        Assert.AreEqual(
            CalendarErrorCodes.PersonalScheduleInvalidContent,
            oversized.Error?.Code);
    }

    [TestMethod]
    public void ValidateContent_TrimsAcceptedValues()
    {
        var result = PersonalScheduleManagementService.ValidateContent("  团队周会  ");
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("团队周会", result.Value);
    }

    [TestMethod]
    public void IsValidTimeRange_RejectsEndBeforeStart()
    {
        var start = DateTimeOffset.UtcNow;
        var end = start.AddMinutes(-5);
        Assert.IsFalse(PersonalScheduleManagementService.IsValidTimeRange(start, end));
        Assert.IsTrue(PersonalScheduleManagementService.IsValidTimeRange(start, start));
        Assert.IsTrue(PersonalScheduleManagementService.IsValidTimeRange(start, end.AddMinutes(10)));
    }

    [TestMethod]
    public void NormalizeStatusFilter_AcceptsKnownStatusesAndIgnoresUnknownValues()
    {
        Assert.AreEqual(
            PersonalScheduleStatuses.Pending,
            PersonalScheduleManagementService.NormalizeStatusFilter(" pending "));
        Assert.AreEqual(
            PersonalScheduleStatuses.Completed,
            PersonalScheduleManagementService.NormalizeStatusFilter("completed"));
        Assert.IsNull(PersonalScheduleManagementService.NormalizeStatusFilter("archived"));
        Assert.IsNull(PersonalScheduleManagementService.NormalizeStatusFilter(null));
    }

    [TestMethod]
    public async Task CreateAsync_RejectsInvalidTimeRangeBeforeDatabaseWrite()
    {
        var service = CreateService();
        var start = DateTimeOffset.UtcNow;
        var result = await service.CreateAsync(
            Guid.CreateVersion7(),
            new CreatePersonalScheduleRequest(
                "无效区间",
                start,
                start.AddMinutes(-1)));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(
            CalendarErrorCodes.PersonalScheduleInvalidTimeRange,
            result.Error?.Code);
    }

    [TestMethod]
    public async Task SetStatusAsync_RejectsUnknownStatusBeforeDatabaseWrite()
    {
        var service = CreateService();
        var result = await service.SetStatusAsync(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            new SetPersonalScheduleStatusRequest("archived", 1));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(
            CalendarErrorCodes.PersonalScheduleInvalidStatus,
            result.Error?.Code);
    }

    private static PersonalScheduleManagementService CreateService() =>
        new(
            new EmptyQueryExecutor(),
            new EmptyCommandExecutor(),
            new HostTransaction(),
            new HostTenant(),
            new FixedClock(DateTimeOffset.UtcNow),
            new FixedIdGenerator(Guid.CreateVersion7()));

    private sealed class EmptyQueryExecutor : IQueryExecutor
    {
        public Task<IReadOnlyList<T>> QueryAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<T>>(Array.Empty<T>());

        public Task<T?> QuerySingleOrDefaultAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<T?>(default);
    }

    private sealed class EmptyCommandExecutor : ICommandExecutor
    {
        public Task<int> ExecuteAsync(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(0);
    }

    private sealed class HostTransaction : ICommandTransaction
    {
        public Task ExecuteAsync(
            Func<CancellationToken, Task> action,
            CancellationToken cancellationToken = default) =>
            action(cancellationToken);

        public Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken = default) =>
            action(cancellationToken);

        public Task<Result<T>> ExecuteResultAsync<T>(
            Func<CancellationToken, Task<Result<T>>> action,
            CancellationToken cancellationToken = default) =>
            action(cancellationToken);
    }

    private sealed class HostTenant : ICurrentTenant
    {
        public Guid? Id => null;

        public bool IsAvailable => true;

        public bool IsHost => true;

        public string? Identifier => null;

        public string? Name => null;
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    private sealed class FixedIdGenerator(Guid id) : IIdGenerator
    {
        public Guid NewId() => id;
    }
}
