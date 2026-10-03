using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Auditing.Contracts;
using Full.NET.Modules.Auditing.Features.QueryHostOperationLogs;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.Auditing;

[TestClass]
public sealed class HostOperationLogDetailsQueryTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 0, 0, 0, TimeSpan.Zero);

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer, "auditing.operation_log.details.sql_server")]
    [DataRow(DatabaseProvider.MySql, "auditing.operation_log.details.my_sql")]
    public async Task Detail_query_filters_expiry_and_returns_only_known_fields(
        DatabaseProvider provider,
        string statementName)
    {
        var id = Guid.CreateVersion7();
        var query = new RecordingQueryExecutor(new HostOperationLogDetailsQueryService.DetailRow
        {
            Id = id,
            ContextJson = "{\"schemaVersion\":1,\"clientIp\":\"2001:db8::1\",\"clientPort\":5000,\"unapproved\":\"secret\"}",
            DetailsExpiresAtUtc = Now.AddHours(1),
        });
        var service = CreateService(query, provider);

        var result = await service.GetByIdAsync(id, CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsNotNull(result.Value);
        Assert.AreEqual(id, result.Value.Id);
        Assert.AreEqual("2001:db8::1", result.Value.Context.ClientIp);
        Assert.AreEqual(5000, result.Value.Context.ClientPort);
        Assert.AreEqual(1, result.Value.Context.SchemaVersion);
        Assert.AreEqual(statementName, query.Statement?.Name);
        Assert.AreEqual(SqlDataScope.HostOnly, query.Statement?.Scope);
        StringAssert.Contains(query.Statement!.Text, "DetailsExpiresAtUtc > @NowUtc");
        Assert.IsFalse(typeof(OperationLogDetailsContextV1).GetProperties()
            .Any(property => property.Name == "Unapproved"));
    }

    [TestMethod]
    public async Task Missing_expired_or_unknown_schema_details_return_not_found()
    {
        var id = Guid.CreateVersion7();
        foreach (var row in new HostOperationLogDetailsQueryService.DetailRow?[]
        {
            null,
            new() { Id = Guid.CreateVersion7(), ContextJson = "{\"schemaVersion\":1}",
                DetailsExpiresAtUtc = Now.AddHours(1) },
            new() { Id = id, ContextJson = "{\"schemaVersion\":1}",
                DetailsExpiresAtUtc = Now },
            new() { Id = id, ContextJson = "{\"schemaVersion\":2}",
                DetailsExpiresAtUtc = Now.AddHours(1) },
            new() { Id = id, ContextJson = "broken",
                DetailsExpiresAtUtc = Now.AddHours(1) },
            new() { Id = id,
                ContextJson = "{\"schemaVersion\":1,\"requestCaptureState\":\"captured\"}",
                DetailsExpiresAtUtc = Now.AddHours(1) },
            new() { Id = id,
                ContextJson = "{\"schemaVersion\":1,\"responseCaptureState\":\"unknown\"}",
                DetailsExpiresAtUtc = Now.AddHours(1) },
        })
        {
            var result = await CreateService(new RecordingQueryExecutor(row),
                DatabaseProvider.SqlServer).GetByIdAsync(id, CancellationToken.None);
            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual(AuditingErrorCodes.OperationLogNotFound, result.Error?.Code);
        }
    }

    private static HostOperationLogDetailsQueryService CreateService(
        IQueryExecutor query,
        DatabaseProvider provider) =>
        new(query, new FixedClock(), Options.Create(new DatabaseOptions
        {
            Provider = provider,
        }));

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class RecordingQueryExecutor(object? result) : IQueryExecutor
    {
        public SqlStatement? Statement { get; private set; }

        public Task<T?> QuerySingleOrDefaultAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            Statement = statement;
            return Task.FromResult((T?)result);
        }

        public Task<IReadOnlyList<T>> QueryAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Unexpected list query.");
    }
}
