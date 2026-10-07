using Microsoft.Extensions.DependencyInjection;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Modules.EnterpriseRequest.Features.ImportExport;
using Full.NET.Modules.EnterpriseRequest.Generated;
using Full.NET.Modules.EnterpriseRequest.Persistence;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.ImportExport.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.EnterpriseRequest;

[TestClass]
public sealed class EnterpriseRequestImportRecoveryTests
{
    [TestMethod]
    public async Task Batch_checkpoint_skips_invalid_rows_and_replays_original_line_identity()
    {
        var h = new Harness(); var task = Guid.NewGuid();
        var bytes = EnterpriseRequestImportTests.Workbook([
            ["bad", "invalid", "1,2", h.Applicant.ToString(), h.Unit.ToString()],
            ["first", "valid, title", "1", h.Applicant.ToString(), h.Unit.ToString()],
            ["second", "valid", "2", h.Applicant.ToString(), h.Unit.ToString()]]);
        using var content = new MemoryStream(bytes);
        var result = await new EnterpriseRequestStaticImportSchemaHandler(h.Service).ExecuteBatchAsync(content, bytes.Length, 1, 1,
            new StaticImportPreviewContext(h.Actor, new Dictionary<string, bool>()) { TaskId = task });
        Assert.IsTrue(result.IsSuccess); Assert.HasCount(1, result.Value!.Rows); Assert.AreEqual(4, result.Value.Rows[0].LineNumber);
        Assert.IsTrue(result.Value.Rows[0].Succeeded); Assert.AreEqual(1, h.Coordinator.CommitCount);
        await h.Commands.Received(1).ExecuteAsync(EnterpriseRequestImportSql.Insert,
            Arg.Is<object>(value => (int)((Dictionary<string, object?>)value!)["LineNumber"]! == 4), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Replay_accepts_only_matching_payload_and_never_writes(bool matches)
    {
        var h = new Harness(); var hash = EnterpriseRequestImportService.PayloadHash(h.Request, h.Unit, h.Actor);
        h.Queries.QuerySingleOrDefaultAsync<EnterpriseRequestImportReceiptRecord>(EnterpriseRequestImportSql.Find, Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(new EnterpriseRequestImportReceiptRecord(h.Entity, matches ? hash : new string('B', 64)));
        var result = await h.Service.ImportAsync(Guid.NewGuid(), 2, h.Request, h.Unit, h.Actor, default);
        Assert.AreEqual(matches, result.IsSuccess); if (matches) Assert.AreEqual(h.Entity, result.Value);
        Assert.AreEqual(0, h.Commands.ReceivedCalls().Count()); Assert.AreEqual(0, h.Coordinator.BeginCount);
    }

    [TestMethod]
    public async Task Replay_rechecks_current_entity_organization_and_denies_revoked_authority()
    {
        var h = new Harness();
        h.Queries.QuerySingleOrDefaultAsync<EnterpriseRequestImportReceiptRecord>(EnterpriseRequestImportSql.Find, Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(new EnterpriseRequestImportReceiptRecord(h.Entity, EnterpriseRequestImportService.PayloadHash(h.Request, h.Unit, h.Actor)));
        h.Authorizer.EnsureCanWriteAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Failure(new Error("test.denied", "Denied", ErrorType.Forbidden)));
        var result = await h.Service.ImportAsync(Guid.NewGuid(), 2, h.Request, h.Unit, h.Actor, default);
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(ErrorType.Forbidden, result.Error!.Type);
        await h.Authorizer.Received(1).EnsureCanWriteAsync(h.Tenant.Id!.Value, h.Unit, h.Actor, Arg.Any<CancellationToken>());
        Assert.AreEqual(0, h.Commands.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Domain_failure_rolls_back_receipt_and_does_not_complete_it()
    {
        var h = new Harness();
        h.Authorizer.EnsureCanWriteAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Failure(new Error("test.denied", "Denied", ErrorType.Forbidden)));
        Assert.IsFalse((await h.Service.ImportAsync(Guid.NewGuid(), 2, h.Request, h.Unit, h.Actor, default)).IsSuccess);
        Assert.AreEqual(0, h.Coordinator.CommitCount); Assert.AreEqual(1, h.Coordinator.RollbackCount);
        await h.Commands.DidNotReceive().ExecuteAsync(EnterpriseRequestImportSql.Complete, Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Completion_failure_rolls_back_created_entity_and_receipt()
    {
        var h = new Harness(); h.Commands.ExecuteAsync(EnterpriseRequestImportSql.Complete, Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(0);
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Service.ImportAsync(Guid.NewGuid(), 2, h.Request, h.Unit, h.Actor, default));
        Assert.AreEqual(0, h.Coordinator.CommitCount); Assert.AreEqual(1, h.Coordinator.RollbackCount);
    }

    [TestMethod]
    public async Task Host_context_is_rejected_before_receipt_lookup()
    {
        var h = new Harness(); h.Tenant.SetHost();
        Assert.IsFalse((await h.Service.ImportAsync(Guid.NewGuid(), 2, h.Request, h.Unit, h.Actor, default)).IsSuccess);
        Assert.AreEqual(0, h.Queries.ReceivedCalls().Count()); Assert.AreEqual(0, h.Commands.ReceivedCalls().Count());
    }

    [TestMethod]
    public void Worker_registration_resolves_sample_handler_and_shared_runner_without_http_stack()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddLogging(); services.AddOptions();
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        new Full.NET.Modules.Identity.IdentityModule().AddBackgroundServices(services, configuration);
        new Full.NET.Modules.Organization.OrganizationModule().AddBackgroundServices(services, configuration);
        new Full.NET.Modules.ImportExport.ImportExportModule().AddBackgroundServices(services, configuration);
        new Full.NET.Modules.EnterpriseRequest.EnterpriseRequestModule().AddBackgroundServices(services, configuration);
        services.AddScoped(_ => Substitute.For<IQueryExecutor>());
        services.AddScoped(_ => Substitute.For<ICommandExecutor>());
        services.AddScoped(_ => Substitute.For<ICommandTransaction>());
        services.AddScoped(_ => Substitute.For<ICurrentTenant>());
        services.AddScoped(_ => Substitute.For<ICurrentTenantContextWriter>());
        services.AddScoped(_ => Substitute.For<IActiveTenantContextResolver>());
        services.AddScoped(_ => Substitute.For<Full.NET.Modules.Files.Contracts.ITenantResourceFileStore>());
        services.AddSingleton<IClock>(Substitute.For<IClock>());
        services.AddSingleton<IIdGenerator>(Substitute.For<IIdGenerator>());
        using var provider = services.BuildServiceProvider(); using var scope = provider.CreateScope();
        Assert.IsTrue(scope.ServiceProvider.GetServices<IStaticImportSchemaHandler>().Any(handler => handler.SchemaKey == StaticImportSchemaKeys.DemoEnterpriseRequests));
        Assert.IsTrue(scope.ServiceProvider.GetServices<IStaticImportSchemaHandler>().Any(handler => handler.SchemaKey == StaticImportSchemaKeys.OrganizationTenantPositions));
        Assert.IsNotNull(scope.ServiceProvider.GetRequiredService<Full.NET.Modules.ImportExport.ImportTasks.ImportExportTaskRunner>());
        Assert.IsFalse(services.Any(descriptor => descriptor.ServiceType == typeof(Microsoft.AspNetCore.Authentication.IAuthenticationSchemeProvider)));
    }

    [TestMethod]
    public async Task Unique_conflict_reloads_committed_receipt_only_after_transaction_rollback()
    {
        var h = new Harness(); var hash = EnterpriseRequestImportService.PayloadHash(h.Request, h.Unit, h.Actor); var reads = 0;
        h.Queries.QuerySingleOrDefaultAsync<EnterpriseRequestImportReceiptRecord>(EnterpriseRequestImportSql.Find, Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(_ => { if (++reads == 1) return Task.FromResult<EnterpriseRequestImportReceiptRecord?>(null);
                Assert.AreEqual(1, h.Coordinator.RollbackCount); Assert.IsFalse(h.Coordinator.HasTransaction);
                return Task.FromResult<EnterpriseRequestImportReceiptRecord?>(new(h.Entity, hash)); });
        h.Commands.ExecuteAsync(EnterpriseRequestImportSql.Insert, Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns<int>(_ => throw new DataCommandException(DataCommandFailureKind.UniqueConstraint, new InvalidOperationException()));
        var result = await h.Service.ImportAsync(Guid.NewGuid(), 2, h.Request, h.Unit, h.Actor, default);
        Assert.IsTrue(result.IsSuccess); Assert.AreEqual(h.Entity, result.Value); Assert.AreEqual(2, reads); Assert.AreEqual(0, h.Coordinator.CommitCount);
    }

    [TestMethod]
    public async Task Incomplete_receipt_does_not_retry_business_creation()
    {
        var h = new Harness();
        h.Queries.QuerySingleOrDefaultAsync<EnterpriseRequestImportReceiptRecord>(EnterpriseRequestImportSql.Find, Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(new EnterpriseRequestImportReceiptRecord(null, EnterpriseRequestImportService.PayloadHash(h.Request, h.Unit, h.Actor)));
        var result = await h.Service.ImportAsync(Guid.NewGuid(), 2, h.Request, h.Unit, h.Actor, default);
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(ErrorType.Conflict, result.Error!.Type); Assert.AreEqual(0, h.Commands.ReceivedCalls().Count());
    }

    private sealed class Harness
    {
        internal readonly IQueryExecutor Queries = Substitute.For<IQueryExecutor>();
        internal readonly ICommandExecutor Commands = Substitute.For<ICommandExecutor>();
        internal readonly RecordingDbTransactionCoordinator Coordinator = new();
        internal readonly CurrentTenantAccessor Tenant = new();
        internal readonly IOrganizationOwnedEntityWriteAuthorizer Authorizer = Substitute.For<IOrganizationOwnedEntityWriteAuthorizer>();
        internal readonly Guid Unit = Guid.NewGuid(), Actor = Guid.NewGuid(), Applicant = Guid.NewGuid(), Entity = Guid.NewGuid();
        internal readonly CreateEnterpriseRequestRequest Request;
        internal readonly EnterpriseRequestImportService Service;
        internal Harness()
        {
            Tenant.SetTenant(new TenantContext(Guid.NewGuid(), "test", "Test"));
            Request = new("REQ", "title", "draft", 1m, Applicant);
            var transaction = new DapperCommandTransaction(Coordinator);
            var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(DateTimeOffset.UtcNow);
            var ids = Substitute.For<IIdGenerator>(); ids.NewId().Returns(_ => Guid.CreateVersion7());
            Commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(1);
            Authorizer.EnsureCanWriteAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Result<bool>.Success(true));
            Queries.QuerySingleOrDefaultAsync<EnterpriseRequestRecord>(Arg.Any<SqlStatement>(), Arg.Any<object>(), Arg.Any<CancellationToken>())
                .Returns(new EnterpriseRequestRecord(Entity, Tenant.Id!.Value, Unit, "REQ", "title", "draft", 1m, Applicant, 1,
                    DateTimeOffset.UtcNow, Actor, null, null, false, null, null));
            var queryService = new EnterpriseRequestQueryService(Queries, Options.Create(new DatabaseOptions()),
                Substitute.For<IUserDataScopeResolver>(), Substitute.For<IDataScopeSqlFilterBuilder>());
            var management = new EnterpriseRequestManagementService(Queries, Commands, transaction, queryService, Tenant, clock, ids, Authorizer);
            Service = new(Queries, Commands, transaction, management, queryService, Tenant, clock, ids, Authorizer);
        }
    }
}
