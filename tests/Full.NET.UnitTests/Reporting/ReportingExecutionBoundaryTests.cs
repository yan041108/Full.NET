using System.Security.Claims;
using Full.NET.Abstractions.Time;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Reporting.Connectivity;
using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.Reporting.Features.ExecuteDefinitions;
using Full.NET.Modules.Reporting.Features.ManageDefinitions;
using Full.NET.Modules.Reporting.Features.PublishedDefinitions;
using Full.NET.Modules.Reporting.Persistence;
using Full.NET.Modules.Reporting.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Reporting;

/// <summary>查询后复核同一版本，且所有已交付外部会话均在取消或失败时释放。</summary>
[TestClass]
public sealed class ReportingExecutionBoundaryTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer, false, "allowed")]
    [DataRow(DatabaseProvider.MySql, false, "allowed")]
    [DataRow(DatabaseProvider.SqlServer, true, "allowed")]
    [DataRow(DatabaseProvider.MySql, true, "allowed")]
    [DataRow(DatabaseProvider.SqlServer, false, "revoked")]
    [DataRow(DatabaseProvider.MySql, false, "revoked")]
    [DataRow(DatabaseProvider.SqlServer, true, "revoked")]
    [DataRow(DatabaseProvider.MySql, true, "revoked")]
    [DataRow(DatabaseProvider.SqlServer, false, "disabled")]
    [DataRow(DatabaseProvider.MySql, false, "disabled")]
    [DataRow(DatabaseProvider.SqlServer, true, "disabled")]
    [DataRow(DatabaseProvider.MySql, true, "disabled")]
    public async Task Query_result_is_released_only_when_the_same_version_remains_available(DatabaseProvider provider, bool host, string kind)
    {
        var fixture = new Fixture(provider, host, kind);
        var result = await fixture.Execute(default);
        Assert.AreEqual(kind == "allowed", result.IsSuccess);
        Assert.IsTrue(fixture.Disposed);
        if (kind == "allowed") Assert.AreEqual("10.0", result.Value!.Rows.Single().Values["EngineVersion"]);
        else Assert.IsNull(result.Value);
        var expected = host ? kind == "disabled" ? new int?[] { 1 } : new int?[] { 1, 1 } : new int?[] { null, 1 };
        CollectionAssert.AreEqual(expected, fixture.Versions.ToArray());
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer, "open")]
    [DataRow(DatabaseProvider.MySql, "open")]
    [DataRow(DatabaseProvider.SqlServer, "query")]
    [DataRow(DatabaseProvider.MySql, "query")]
    [DataRow(DatabaseProvider.SqlServer, "final")]
    [DataRow(DatabaseProvider.MySql, "final")]
    public async Task Ignored_cancellation_propagates_and_disposes_handed_off_session(DatabaseProvider provider, string stage)
    {
        using var cancellation = new CancellationTokenSource();
        var fixture = new Fixture(provider, false, stage, cancellation.Cancel);
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => fixture.Execute(cancellation.Token));
        Assert.IsTrue(fixture.Disposed);
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Failed_open_with_a_session_still_releases_ownership(DatabaseProvider provider)
    {
        var fixture = new Fixture(provider, false, "open-failed");
        var result = await fixture.Execute(default);
        Assert.IsFalse(result.IsSuccess); Assert.IsTrue(fixture.Disposed);
        Assert.AreEqual(0, fixture.QueryCalls);
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer, false)]
    [DataRow(DatabaseProvider.MySql, false)]
    [DataRow(DatabaseProvider.SqlServer, true)]
    [DataRow(DatabaseProvider.MySql, true)]
    public async Task Query_failure_or_exception_still_disposes_the_owned_session(DatabaseProvider provider, bool throws)
    {
        var fixture = new Fixture(provider, false, throws ? "query-throws" : "query-failed");
        if (throws)
        {
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Execute(default));
        }
        else
        {
            var result = await fixture.Execute(default);
            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual(ReportingErrorCodes.ExecutionFailed, result.Error!.Code);
        }
        Assert.AreEqual(1, fixture.QueryCalls);
        Assert.IsTrue(fixture.Disposed);
    }

    [TestMethod]
    [DataRow("request")]
    [DataRow("parameters")]
    [DataRow("parameter")]
    public async Task Invalid_parameter_payload_fails_before_opening_external_connection(string kind)
    {
        var fixture = new Fixture(DatabaseProvider.SqlServer, false, "allowed");
        ExecuteReportingDefinitionRequest? request = kind == "request" ? null : new(null, kind == "parameters" ? null! : [null!]);
        var result = await fixture.Service.ExecuteAsync(fixture.Id, request!, 1, 20, new ClaimsPrincipal());
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(ReportingErrorCodes.ExecutionParametersInvalid, result.Error!.Code);
        Assert.AreEqual(0, fixture.OpenCalls);
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Large_page_does_not_report_more_rows_from_a_wrapped_offset(DatabaseProvider provider)
    {
        var fixture = new Fixture(provider, false, "pagination");
        var result = await fixture.Service.ExecuteAsync(fixture.Id, new(null, []), 429496731, 10, new ClaimsPrincipal());
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(10, result.Value!.Rows.Count);
        Assert.IsFalse(result.Value.HasMore);
        Assert.IsTrue(fixture.Disposed);
    }

    private sealed class Fixture
    {
        public Guid Id { get; } = Guid.CreateVersion7();
        public ReportingDefinitionExecutionService Service { get; }
        public List<int?> Versions { get; } = [];
        public bool Disposed { get; private set; }
        public int OpenCalls { get; private set; }
        public int QueryCalls { get; private set; }
        public Fixture(DatabaseProvider provider, bool host, string kind, Action? cancel = null)
        {
            var context = new CurrentTenantAccessor(); if (host) context.SetHost(); else context.SetTenant(new(Guid.CreateVersion7(), "acme", "Acme"));
            var query = Substitute.For<IQueryExecutor>(); var options = Options.Create(new DatabaseOptions { Provider = provider });
            var sourceId = Guid.CreateVersion7();
            var definition = new ReportingDefinitionRecord { Id = Id, DataSourceId = sourceId, DefinitionKey = "engine", Name = "Engine", IsEnabled = true, LatestPublishedVersionNumber = 1 };
            var version = new ReportingDefinitionVersionRecord { Id = Guid.CreateVersion7(), DefinitionId = Id, DataSourceId = sourceId, VersionNumber = 1, QueryPortKey = "reporting.database_engine_version" };
            if (kind == "pagination")
            {
                version.QueryPortKey = "reporting.schema_inventory";
                version.ParameterSchemaJson = "[{\"parameterKey\":\"topN\",\"displayName\":\"数量\",\"dataTypeKey\":\"integer\",\"isRequired\":true,\"defaultValue\":\"20\"}]";
            }
            query.QuerySingleOrDefaultAsync<ReportingDefinitionRecord>(host ? ReportingDefinitionSql.FindDefinitionById : ReportingTenantGrantSql.FindDefinition,
                Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => definition);
            query.QuerySingleOrDefaultAsync<ReportingDefinitionVersionRecord>(host ? ReportingDefinitionSql.FindVersionByNumber : ReportingTenantGrantSql.ResolveVersion(provider),
                Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(call =>
                {
                    var parameters = (IReadOnlyDictionary<string, object?>)call[1]!;
                    Versions.Add((int?)parameters["VersionNumber"]);
                    if (Versions.Count == 2 && kind == "final") cancel?.Invoke();
                    return Versions.Count == 2 && (kind is "revoked" or "final") ? null : version;
                });
            var protector = new ReportingDataSourceSecretProtector(new EphemeralDataProtectionProvider());
            var source = new ReportingDataSourceRecord { Id = sourceId, IsEnabled = true,
                ProviderKey = provider == DatabaseProvider.SqlServer ? ReportingDataSourceProviderKeys.SqlServer : ReportingDataSourceProviderKeys.MySql,
                ServerHost = "example.invalid", Port = provider == DatabaseProvider.SqlServer ? 1433 : 3306, DatabaseName = "metadata", Username = "readonly", PasswordProtected = protector.Protect("fixture") };
            query.QuerySingleOrDefaultAsync<ReportingDataSourceRecord>(host ? ReportingDataSourceSql.FindById : ReportingTenantGrantSql.FindDataSource,
                Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(source);
            var session = Substitute.For<IExternalDatabaseSession>();
            session.DisposeAsync().Returns(_ => { Disposed = true; return ValueTask.CompletedTask; });
            session.ExecuteQueryAsync(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, object?>>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(_ => { QueryCalls++; if (kind == "query") cancel?.Invoke(); if (kind == "disabled") definition.IsEnabled = false;
                    if (kind == "query-throws") throw new InvalidOperationException("fixture query failure");
                    if (kind == "query-failed") return new ExternalDatabaseQueryResult(false, "fixture query failure", [], []);
                    if (kind == "allowed") definition.LatestPublishedVersionNumber = 2;
                    if (kind == "pagination") return new ExternalDatabaseQueryResult(true, null, ["SchemaName"],
                        Enumerable.Range(1, 10).Select(index => (IReadOnlyDictionary<string, string?>)new Dictionary<string, string?> { ["SchemaName"] = $"schema{index}" }).ToArray());
                    return new ExternalDatabaseQueryResult(true, null, ["EngineVersion"], [new Dictionary<string, string?> { ["EngineVersion"] = "10.0" }]); });
            var factory = Substitute.For<IExternalDatabaseConnectionFactory>();
            factory.OpenAsync(Arg.Any<ExternalDatabaseConnectionRequest>(), Arg.Any<CancellationToken>())
                .Returns(_ => { OpenCalls++; if (kind == "open") cancel?.Invoke(); return new ExternalDatabaseSessionResult(kind != "open-failed", null, session); });
            var permissions = Substitute.For<IIdentityPermissionEvaluator>(); permissions.HasPermission(Arg.Any<ClaimsPrincipal>(), Arg.Any<string>()).Returns(true);
            var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(DateTimeOffset.Parse("2026-10-15T00:00:00Z"));
            Service = new(new(query, new(query, options), context, options), protector, new(factory), clock, permissions);
        }
        public Task<Full.NET.Abstractions.Results.Result<ReportingExecutionPageResponse>> Execute(CancellationToken token) => Service.ExecuteAsync(Id, new(null, []), 1, 20, new ClaimsPrincipal(), token);
    }
}
