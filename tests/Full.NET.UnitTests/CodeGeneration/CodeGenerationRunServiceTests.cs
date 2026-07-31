using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.CodeGeneration.Contracts;
using Full.NET.Modules.CodeGeneration.Features.ManageHostRuns;
using Full.NET.Modules.CodeGeneration.Features.ManageHostTemplates;
using Full.NET.Modules.CodeGeneration.Features.NormalizeCrudSchema;
using Full.NET.Modules.CodeGeneration.Features.PreviewCrudGeneration;
using Full.NET.Modules.CodeGeneration.Persistence;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.CodeGeneration;

[TestClass]
public sealed class CodeGenerationRunServiceTests
{
    private static readonly Guid ActorUserId = Guid.Parse(
        "0198f36e-f7a7-7c52-9cbb-774e67411211");
    private static readonly Guid RunId = Guid.Parse(
        "0198f36e-f7a7-7c52-9cbb-774e67411212");
    private static readonly DateTimeOffset Now = new(
        2026,
        7,
        31,
        5,
        0,
        0,
        TimeSpan.Zero);

    [TestMethod]
    public async Task Inline_preview_persists_only_deterministic_success_summary()
    {
        var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(
                CodeGenerationRunSql.Insert,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(1);
        var service = CreateService(command);

        var result = await service.PreviewAsync(
            ActorUserId,
            new CodeGenerationRunPreviewRequest(
                TemplateId: null,
                TemplateVersion: null,
                Schema: CreateSchema()));

        Assert.IsTrue(
            result.IsSuccess,
            result.Error?.Code ?? "missing error code");
        Assert.AreEqual(RunId, result.Value!.RunId);
        Assert.IsTrue(result.Value.Preview.Artifacts.Count > 0);
        await command.Received(1).ExecuteAsync(
            CodeGenerationRunSql.Insert,
            Arg.Is<object>(parameters =>
                parameters != null
                && Read<string>(parameters, "Status")
                    == CodeGenerationRunStatuses.Succeeded
                && Read<string>(parameters, "OperationKind")
                    == CodeGenerationRunOperationKinds.Preview
                && Read<string>(parameters, "ModuleKey") == "catalog"
                && Read<string>(parameters, "EntityKey") == "product"
                && Read<string>(parameters, "SchemaSha256").Length == 64
                && Read<int>(parameters, "ArtifactCount")
                    == result.Value.Preview.Artifacts.Count
                && Read<string>(parameters, "ManifestSha256").Length == 64
                && Read<string?>(parameters, "ErrorCode") == null
                && Read<Guid>(parameters, "RequestedByUserId")
                    == ActorUserId),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Invalid_source_shape_persists_stable_failure_without_input()
    {
        var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(
                CodeGenerationRunSql.Insert,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(1);
        var service = CreateService(command);

        var result = await service.PreviewAsync(
            ActorUserId,
            new CodeGenerationRunPreviewRequest(
                TemplateId: null,
                TemplateVersion: null,
                Schema: null));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(
            CodeGenerationRunErrorCodes.InvalidSource,
            result.Error!.Code);
        await command.Received(1).ExecuteAsync(
            CodeGenerationRunSql.Insert,
            Arg.Is<object>(parameters =>
                parameters != null
                && Read<string>(parameters, "Status")
                    == CodeGenerationRunStatuses.Failed
                && Read<int>(parameters, "ArtifactCount") == 0
                && Read<string?>(parameters, "SchemaSha256") == null
                && Read<string?>(parameters, "ManifestSha256") == null
                && Read<string>(parameters, "ErrorCode")
                    == CodeGenerationRunErrorCodes.InvalidSource),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Incomplete_template_source_persists_no_template_reference(
        bool hasTemplateId)
    {
        var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(
                CodeGenerationRunSql.Insert,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(1);
        var service = CreateService(command);

        var result = await service.PreviewAsync(
            ActorUserId,
            new CodeGenerationRunPreviewRequest(
                hasTemplateId ? RunId : null,
                hasTemplateId ? null : 1,
                Schema: null));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(
            CodeGenerationRunErrorCodes.InvalidSource,
            result.Error!.Code);
        await command.Received(1).ExecuteAsync(
            CodeGenerationRunSql.Insert,
            Arg.Is<object>(parameters =>
                parameters != null
                && Read<Guid?>(parameters, "TemplateId") == null
                && Read<long?>(parameters, "TemplateVersion") == null),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Missing_template_persists_stable_failure()
    {
        var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(
                CodeGenerationRunSql.Insert,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(1);
        var service = CreateService(command);

        var result = await service.PreviewAsync(
            ActorUserId,
            new CodeGenerationRunPreviewRequest(RunId, 4, null));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(
            CodeGenerationTemplateErrorCodes.NotFound,
            result.Error!.Code);
        await command.Received(1).ExecuteAsync(
            CodeGenerationRunSql.Insert,
            Arg.Is<object>(parameters =>
                parameters != null
                && Read<Guid?>(parameters, "TemplateId") == RunId
                && Read<long?>(parameters, "TemplateVersion") == 4
                && Read<string>(parameters, "ErrorCode")
                    == CodeGenerationTemplateErrorCodes.NotFound),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Stale_template_version_persists_conflict_without_preview()
    {
        var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(
                CodeGenerationRunSql.Insert,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(1);
        var query = Substitute.For<IQueryExecutor>();
        query.QuerySingleOrDefaultAsync<CodeGenerationTemplateRecord>(
                CodeGenerationTemplateSql.FindById,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(CreateTemplateRecord(version: 4));
        var service = CreateService(command, query);

        var result = await service.PreviewAsync(
            ActorUserId,
            new CodeGenerationRunPreviewRequest(RunId, 3, null));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(
            CodeGenerationRunErrorCodes.TemplateVersionConflict,
            result.Error!.Code);
        await command.Received(1).ExecuteAsync(
            CodeGenerationRunSql.Insert,
            Arg.Is<object>(parameters =>
                parameters != null
                && Read<string>(parameters, "Status")
                    == CodeGenerationRunStatuses.Failed
                && Read<string>(parameters, "ErrorCode")
                    == CodeGenerationRunErrorCodes.TemplateVersionConflict),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Current_template_version_generates_and_persists_reference()
    {
        var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(
                CodeGenerationRunSql.Insert,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(1);
        var query = Substitute.For<IQueryExecutor>();
        query.QuerySingleOrDefaultAsync<CodeGenerationTemplateRecord>(
                CodeGenerationTemplateSql.FindById,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(CreateTemplateRecord(version: 4));
        var service = CreateService(command, query);

        var result = await service.PreviewAsync(
            ActorUserId,
            new CodeGenerationRunPreviewRequest(RunId, 4, null));

        Assert.IsTrue(result.IsSuccess);
        await command.Received(1).ExecuteAsync(
            CodeGenerationRunSql.Insert,
            Arg.Is<object>(parameters =>
                parameters != null
                && Read<Guid?>(parameters, "TemplateId") == RunId
                && Read<long?>(parameters, "TemplateVersion") == 4
                && Read<string>(parameters, "Status")
                    == CodeGenerationRunStatuses.Succeeded),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Cancelled_preview_does_not_create_run_record()
    {
        var command = Substitute.For<ICommandExecutor>();
        var service = CreateService(command);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            service.PreviewAsync(
                ActorUserId,
                new CodeGenerationRunPreviewRequest(
                    null,
                    null,
                    CreateSchema()),
                cancellation.Token));

        await command.DidNotReceiveWithAnyArgs().ExecuteAsync(
            default!,
            default,
            default);
    }

    [TestMethod]
    public async Task Preview_never_returns_success_when_record_was_not_inserted()
    {
        var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(
                CodeGenerationRunSql.Insert,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(0);
        var service = CreateService(command);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            service.PreviewAsync(
                ActorUserId,
                new CodeGenerationRunPreviewRequest(
                    null,
                    null,
                    CreateSchema())));
    }

    private static CodeGenerationRunService CreateService(
        ICommandExecutor command,
        IQueryExecutor? query = null)
    {
        var normalizer = new CodeGenerationSchemaNormalizer();
        return new CodeGenerationRunService(
            command,
            new CodeGenerationTemplateQueryService(
                query ?? Substitute.For<IQueryExecutor>(),
                Substitute.For<IMultiResultQueryExecutor>(),
                normalizer,
                Options.Create(new DatabaseOptions
                {
                    Provider = DatabaseProvider.SqlServer,
                })),
            new CodeGenerationPreviewService(normalizer),
            normalizer,
            new FixedClock(Now),
            new FixedIdGenerator(RunId));
    }

    private static CodeGenerationTemplateRecord CreateTemplateRecord(
        long version)
    {
        var normalized = new CodeGenerationSchemaNormalizer()
            .Normalize(CreateSchema());
        Assert.IsTrue(normalized.IsSuccess);
        return new CodeGenerationTemplateRecord
        {
            Id = RunId,
            Name = "Product CRUD",
            SchemaJson = normalized.Value!.CanonicalJson,
            SchemaSha256 = normalized.Value.SchemaSha256,
            CreatedAtUtc = Now.AddDays(-1),
            CreatedByUserId = ActorUserId,
            Version = version,
        };
    }

    private static CodeGenerationPreviewRequest CreateSchema() =>
        new(
            "acme",
            "catalog",
            "product",
            "acme_catalog_product",
            "Acme.Modules.Catalog",
            "Product",
            "products",
            "products",
            "HostOnly",
            true,
            [
                new("Id", "Id", "id", "Uuid", false, null, null, null),
                new(
                    "Name",
                    "Name",
                    "displayName",
                    "String",
                    false,
                    200,
                    null,
                    null),
                new(
                    "IsActive",
                    "IsActive",
                    "isActive",
                    "Boolean",
                    false,
                    null,
                    null,
                    null),
                new(
                    "Version",
                    "Version",
                    "version",
                    "Int64",
                    false,
                    null,
                    null,
                    null),
            ]);

    private static T Read<T>(object parameters, string name) =>
        (T)parameters.GetType().GetProperty(name)!.GetValue(parameters)!;

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow => utcNow;
    }

    private sealed class FixedIdGenerator(Guid id) : IIdGenerator
    {
        public Guid NewId() => id;
    }
}
