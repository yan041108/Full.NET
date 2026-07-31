using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.CodeGeneration.Contracts;
using Full.NET.Modules.CodeGeneration.Features.ManageHostTemplates;
using Full.NET.Modules.CodeGeneration.Features.NormalizeCrudSchema;
using Full.NET.Modules.CodeGeneration.Persistence;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.CodeGeneration;

[TestClass]
public sealed class CodeGenerationTemplateServiceTests
{
    private static readonly Guid ActorUserId = Guid.Parse(
        "0198f36e-f7a7-7c52-9cbb-774e67411204");
    private static readonly Guid TemplateId = Guid.Parse(
        "0198f36e-f7a7-7c52-9cbb-774e67411205");
    private static readonly DateTimeOffset Now = new(
        2026,
        7,
        30,
        8,
        0,
        0,
        TimeSpan.Zero);

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer, "codegen.template.page.sql_server")]
    [DataRow(DatabaseProvider.MySql, "codegen.template.page.my_sql")]
    public async Task List_clamps_page_and_uses_one_multi_result_round_trip(
        DatabaseProvider provider,
        string statementName)
    {
        var row = CreateRecord();
        var executor = new RecordingMultiResultQueryExecutor(17, row);
        var service = new CodeGenerationTemplateQueryService(
            Substitute.For<IQueryExecutor>(),
            executor,
            new CodeGenerationSchemaNormalizer(),
            Options.Create(new DatabaseOptions { Provider = provider }));

        var result = await service.ListAsync(-3, 500);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(1, result.Value!.Page);
        Assert.AreEqual(100, result.Value.PageSize);
        Assert.AreEqual(17, result.Value.Total);
        Assert.HasCount(1, result.Value.Items);
        Assert.AreEqual(1, executor.CallCount);
        Assert.AreEqual(statementName, executor.Statement!.Name);
        Assert.AreEqual(
            0L,
            ReadParameter<long>(executor.Parameters!, "Offset"));
        Assert.AreEqual(
            100,
            ReadParameter<int>(executor.Parameters!, "PageSize"));
    }

    [TestMethod]
    public async Task List_keeps_extreme_page_offset_outside_int_overflow()
    {
        var executor = new RecordingMultiResultQueryExecutor(
            0,
            CreateRecord());
        var service = new CodeGenerationTemplateQueryService(
            Substitute.For<IQueryExecutor>(),
            executor,
            new CodeGenerationSchemaNormalizer(),
            Options.Create(new DatabaseOptions
            {
                Provider = DatabaseProvider.SqlServer,
            }));

        await service.ListAsync(int.MaxValue, 100);

        Assert.AreEqual(
            ((long)int.MaxValue - 1) * 100,
            ReadParameter<long>(executor.Parameters!, "Offset"));
    }

    [TestMethod]
    public async Task Read_rejects_schema_when_persisted_hash_does_not_match()
    {
        var record = CreateRecord(new string('0', 64));
        var query = Substitute.For<IQueryExecutor>();
        query.QuerySingleOrDefaultAsync<CodeGenerationTemplateRecord>(
                CodeGenerationTemplateSql.FindById,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(record);
        var service = new CodeGenerationTemplateQueryService(
            query,
            Substitute.For<IMultiResultQueryExecutor>(),
            new CodeGenerationSchemaNormalizer(),
            Options.Create(new DatabaseOptions
            {
                Provider = DatabaseProvider.SqlServer,
            }));

        await Assert.ThrowsExactlyAsync<System.Text.Json.JsonException>(
            () => service.GetByIdAsync(TemplateId));
    }

    [TestMethod]
    public async Task Create_trims_fields_and_persists_only_canonical_schema()
    {
        var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(
                Arg.Any<SqlStatement>(),
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(1);
        var service = CreateManagementService(
            Substitute.For<IQueryExecutor>(),
            command);

        var result = await service.CreateAsync(
            ActorUserId,
            new CreateCodeGenerationTemplateRequest(
                "  Product CRUD  ",
                "  Host product template  ",
                CreateSchema()));

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("Product CRUD", result.Value!.Name);
        Assert.AreEqual("Host product template", result.Value.Description);
        Assert.AreEqual(TemplateId, result.Value.Id);
        Assert.AreEqual(ActorUserId, result.Value.CreatedByUserId);
        Assert.AreEqual(1, result.Value.Version);
        Assert.AreEqual(64, result.Value.SchemaSha256.Length);
        await command.Received(1).ExecuteAsync(
            CodeGenerationTemplateSql.Insert,
            Arg.Is<object>(parameters =>
                parameters != null
                && ReadParameter<string>(parameters, "Name") == "Product CRUD"
                && ReadParameter<string>(
                    parameters,
                    "Description") == "Host product template"
                && ReadParameter<string>(
                    parameters,
                    "SchemaJson").Contains(
                        "\"dataScope\":\"host.only\"",
                        StringComparison.Ordinal)
                && ReadParameter<string>(
                    parameters,
                    "SchemaSha256") == result.Value.SchemaSha256),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Update_uses_trusted_actor_and_matching_version()
    {
        var query = Substitute.For<IQueryExecutor>();
        query.QuerySingleOrDefaultAsync<CodeGenerationTemplateRecord>(
                CodeGenerationTemplateSql.FindById,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(CreateRecord());
        var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(
                CodeGenerationTemplateSql.Update,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(1);
        var service = CreateManagementService(query, command);

        var result = await service.UpdateAsync(
            TemplateId,
            ActorUserId,
            new UpdateCodeGenerationTemplateRequest(
                " Updated ",
                null,
                CreateSchema(),
                Version: 4));

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("Updated", result.Value!.Name);
        Assert.AreEqual(5, result.Value.Version);
        Assert.AreEqual(ActorUserId, result.Value.UpdatedByUserId);
        Assert.AreEqual(Now, result.Value.UpdatedAtUtc);
        await command.Received(1).ExecuteAsync(
            CodeGenerationTemplateSql.Update,
            Arg.Is<object>(parameters =>
                parameters != null
                && ReadParameter<long>(parameters, "Version") == 4
                && ReadParameter<Guid>(
                    parameters,
                    "UpdatedByUserId") == ActorUserId),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Delete_distinguishes_not_found_from_version_conflict()
    {
        var missingQuery = Substitute.For<IQueryExecutor>();
        var missing = await CreateManagementService(
                missingQuery,
                Substitute.For<ICommandExecutor>())
            .DeleteAsync(
                TemplateId,
                ActorUserId,
                new DeleteCodeGenerationTemplateRequest(4));
        Assert.IsFalse(missing.IsSuccess);
        Assert.AreEqual(
            CodeGenerationTemplateErrorCodes.NotFound,
            missing.Error!.Code);

        var existingQuery = Substitute.For<IQueryExecutor>();
        existingQuery.QuerySingleOrDefaultAsync<CodeGenerationTemplateRecord>(
                CodeGenerationTemplateSql.FindById,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(CreateRecord());
        var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(
                CodeGenerationTemplateSql.SoftDelete,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(0);
        var conflict = await CreateManagementService(existingQuery, command)
            .DeleteAsync(
                TemplateId,
                ActorUserId,
                new DeleteCodeGenerationTemplateRequest(4));

        Assert.IsFalse(conflict.IsSuccess);
        Assert.AreEqual(
            CodeGenerationTemplateErrorCodes.VersionConflict,
            conflict.Error!.Code);

        var deleteCommand = Substitute.For<ICommandExecutor>();
        deleteCommand.ExecuteAsync(
                CodeGenerationTemplateSql.SoftDelete,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(1);
        var deleted = await CreateManagementService(
                existingQuery,
                deleteCommand)
            .DeleteAsync(
                TemplateId,
                ActorUserId,
                new DeleteCodeGenerationTemplateRequest(4));

        Assert.IsTrue(deleted.IsSuccess);
        await deleteCommand.Received(1).ExecuteAsync(
            CodeGenerationTemplateSql.SoftDelete,
            Arg.Is<object>(parameters =>
                parameters != null
                && ReadParameter<Guid>(
                    parameters,
                    "DeletedByUserId") == ActorUserId
                && ReadParameter<DateTimeOffset>(
                    parameters,
                    "DeletedAtUtc") == Now),
            Arg.Any<CancellationToken>());
    }

    private static CodeGenerationTemplateManagementService
        CreateManagementService(
            IQueryExecutor query,
            ICommandExecutor command) =>
        new(
            query,
            command,
            new PassThroughTransaction(),
            new CodeGenerationSchemaNormalizer(),
            new FixedClock(Now),
            new FixedIdGenerator(TemplateId));

    private static CodeGenerationTemplateRecord CreateRecord(
        string? schemaSha256 = null)
    {
        var normalized = new CodeGenerationSchemaNormalizer()
            .Normalize(CreateSchema());
        Assert.IsTrue(normalized.IsSuccess);
        return new CodeGenerationTemplateRecord
        {
            Id = TemplateId,
            Name = "Product CRUD",
            Description = "Host product template",
            SchemaJson = normalized.Value!.CanonicalJson,
            SchemaSha256 = schemaSha256 ?? normalized.Value.SchemaSha256,
            CreatedAtUtc = Now.AddDays(-1),
            CreatedByUserId = Guid.CreateVersion7(),
            UpdatedAtUtc = Now.AddHours(-1),
            UpdatedByUserId = Guid.CreateVersion7(),
            Version = 4,
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
                new(
                    "Id",
                    "Id",
                    "id",
                    "Uuid",
                    false,
                    null,
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

    private static T ReadParameter<T>(object parameters, string name) =>
        (T)parameters.GetType().GetProperty(name)!.GetValue(parameters)!;

    private sealed class RecordingMultiResultQueryExecutor(
        long total,
        CodeGenerationTemplateRecord row) : IMultiResultQueryExecutor
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
        CodeGenerationTemplateRecord row) : IMultiResultReader
    {
        public Task<T?> ReadSingleOrDefaultAsync<T>() =>
            Task.FromResult((T?)(object)total);

        public Task<IReadOnlyList<T>> ReadAsync<T>() =>
            Task.FromResult<IReadOnlyList<T>>([(T)(object)row]);
    }

    private sealed class PassThroughTransaction : ICommandTransaction
    {
        public Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken) =>
            action(cancellationToken);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow => utcNow;
    }

    private sealed class FixedIdGenerator(Guid id) : IIdGenerator
    {
        public Guid NewId() => id;
    }
}
