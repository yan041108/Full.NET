using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.CodeGeneration.Contracts;
using Full.NET.Modules.CodeGeneration.Features.ManageHostRuns;
using Full.NET.Modules.CodeGeneration.Persistence;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.CodeGeneration;

[TestClass]
public sealed class CodeGenerationRunQueryServiceTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer, "codegen.run.page.sql_server")]
    [DataRow(DatabaseProvider.MySql, "codegen.run.page.my_sql")]
    public async Task List_clamps_page_and_uses_one_multi_result_round_trip(
        DatabaseProvider provider,
        string statementName)
    {
        var executor = new RecordingMultiResultQueryExecutor(
            9,
            CreateRecord());
        var service = new CodeGenerationRunQueryService(
            Substitute.For<IQueryExecutor>(),
            executor,
            Options.Create(new DatabaseOptions { Provider = provider }));

        var result = await service.ListAsync(
            int.MaxValue,
            500,
            CodeGenerationRunStatuses.Succeeded);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(int.MaxValue, result.Value!.Page);
        Assert.AreEqual(100, result.Value.PageSize);
        Assert.AreEqual(9, result.Value.Total);
        Assert.HasCount(1, result.Value.Items);
        Assert.AreEqual(1, executor.CallCount);
        Assert.AreEqual(statementName, executor.Statement!.Name);
        Assert.AreEqual(
            ((long)int.MaxValue - 1) * 100,
            Read<long>(executor.Parameters!, "Offset"));
        Assert.AreEqual(
            CodeGenerationRunStatuses.Succeeded,
            Read<string>(executor.Parameters!, "Status"));
    }

    [TestMethod]
    public async Task List_rejects_unknown_status_before_querying()
    {
        var executor = Substitute.For<IMultiResultQueryExecutor>();
        var service = new CodeGenerationRunQueryService(
            Substitute.For<IQueryExecutor>(),
            executor,
            Options.Create(new DatabaseOptions
            {
                Provider = DatabaseProvider.SqlServer,
            }));

        var result = await service.ListAsync(1, 20, "queued");

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(
            CodeGenerationRunErrorCodes.InvalidQuery,
            result.Error!.Code);
        await executor.DidNotReceiveWithAnyArgs()
            .QueryMultipleAsync<object>(
                default!,
                default,
                default!,
                default);
    }

    [TestMethod]
    public async Task List_accepts_running_apply_status()
    {
        var executor = new RecordingMultiResultQueryExecutor(
            1,
            CreateRecord());
        var service = new CodeGenerationRunQueryService(
            Substitute.For<IQueryExecutor>(),
            executor,
            Options.Create(new DatabaseOptions
            {
                Provider = DatabaseProvider.SqlServer,
            }));

        var result = await service.ListAsync(
            1,
            20,
            CodeGenerationRunStatuses.Running);

        Assert.IsTrue(result.IsSuccess, result.Error?.Code);
        Assert.AreEqual(
            CodeGenerationRunStatuses.Running,
            Read<string>(executor.Parameters!, "Status"));
    }

    [TestMethod]
    public async Task Get_returns_not_found_without_fabricating_run()
    {
        var service = new CodeGenerationRunQueryService(
            Substitute.For<IQueryExecutor>(),
            Substitute.For<IMultiResultQueryExecutor>(),
            Options.Create(new DatabaseOptions
            {
                Provider = DatabaseProvider.SqlServer,
            }));

        var result = await service.GetByIdAsync(Guid.CreateVersion7());

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(
            CodeGenerationRunErrorCodes.NotFound,
            result.Error!.Code);
        Assert.AreEqual(ErrorType.NotFound, result.Error.Type);
    }

    private static CodeGenerationRunRecord CreateRecord() =>
        new()
        {
            Id = Guid.CreateVersion7(),
            OperationKind = CodeGenerationRunOperationKinds.Preview,
            Status = CodeGenerationRunStatuses.Succeeded,
            ModuleKey = "catalog",
            EntityKey = "product",
            SchemaSha256 = new string('a', 64),
            ArtifactCount = 8,
            ManifestSha256 = new string('b', 64),
            RequestedByUserId = Guid.CreateVersion7(),
            StartedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1),
            FinishedAtUtc = DateTimeOffset.UtcNow,
        };

    private static T Read<T>(object parameters, string name) =>
        (T)parameters.GetType().GetProperty(name)!.GetValue(parameters)!;

    private sealed class RecordingMultiResultQueryExecutor(
        long total,
        CodeGenerationRunRecord row) : IMultiResultQueryExecutor
    {
        public int CallCount { get; private set; }

        public SqlStatement? Statement { get; private set; }

        public object? Parameters { get; private set; }

        public Task<TResult> QueryMultipleAsync<TResult>(
            SqlStatement statement,
            object? parameters,
            Func<IMultiResultReader, CancellationToken, Task<TResult>> projector,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            Statement = statement;
            Parameters = parameters;
            return projector(
                new ScriptedMultiResultReader(total, row),
                cancellationToken);
        }
    }

    private sealed class ScriptedMultiResultReader(
        long total,
        CodeGenerationRunRecord row) : IMultiResultReader
    {
        public Task<T?> ReadSingleOrDefaultAsync<T>() =>
            Task.FromResult((T?)(object)total);

        public Task<IReadOnlyList<T>> ReadAsync<T>() =>
            Task.FromResult<IReadOnlyList<T>>([(T)(object)row]);
    }
}
