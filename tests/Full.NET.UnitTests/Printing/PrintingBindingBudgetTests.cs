using System.Collections;
using System.Security.Claims;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Modules.EnterpriseRequest.Features.Printing;
using Full.NET.Modules.Printing.Contracts;
using Full.NET.Modules.Printing.Domain;
using Full.NET.Modules.Printing.Features.PreviewTemplates;
using NSubstitute;

namespace Full.NET.UnitTests.Printing;

[TestClass]
public sealed class PrintingBindingBudgetTests
{
    [TestMethod]
    public async Task Record_binding_reads_only_declared_fields_and_overwrites_server_fields()
    {
        var fixture = new Fixture();
        fixture.Record.ResolveAsync(Arg.Any<Guid>(), Arg.Any<ClaimsPrincipal>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyDictionary<string, string?>>.Success(new SelectiveDictionary(Fixture.Values())));
        var result = await fixture.Service.ResolveAsync(EnterpriseRequestPrintingSchemaContributor.SchemaKey, fixture.Principal, recordId: Guid.CreateVersion7());
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("Actor", result.Value!["printedByDisplayName"]);
        Assert.AreEqual(fixture.Now.ToString("O"), result.Value["printedAtUtc"]);
        Assert.IsFalse(result.Value.ContainsKey("private.extra"));
        Assert.HasCount(6, result.Value);
    }

    [TestMethod]
    [DataRow("record")]
    [DataRow("profile")]
    [DataRow("actor")]
    public async Task Oversize_projected_binding_returns_validation_failure(string kind)
    {
        var fixture = new Fixture(); var value = new string('x', (int)(PrintingLayoutPolicy.MaxBindingTextBytes / 2) + 1);
        var values = Fixture.Values(); values["title"] = value;
        if (kind == "record") fixture.Record.ResolveAsync(Arg.Any<Guid>(), Arg.Any<ClaimsPrincipal>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyDictionary<string, string?>>.Success(values));
        else if (kind == "profile") fixture.Profile.ResolveAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new PrintingTenantProfileBinding(value, "acme", "acme.example"));
        else fixture.Principal = new(new ClaimsIdentity([new Claim("preferred_username", value)], "test"));
        var result = await fixture.Service.ResolveAsync(kind == "record" ? EnterpriseRequestPrintingSchemaContributor.SchemaKey : PrintingFormSchemaKeys.TenantProfileCard,
            fixture.Principal, recordId: kind == "record" ? Guid.CreateVersion7() : null);
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(PrintingErrorCodes.BindingFailed, result.Error!.Code); Assert.IsNull(result.Value);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Ignored_binding_source_cancellation_cannot_return_values(bool record)
    {
        var fixture = new Fixture(); using var cancellation = new CancellationTokenSource();
        if (record) fixture.Record.ResolveAsync(Arg.Any<Guid>(), Arg.Any<ClaimsPrincipal>(), Arg.Any<CancellationToken>())
            .Returns(_ => { cancellation.Cancel(); return Result<IReadOnlyDictionary<string, string?>>.Success(Fixture.Values()); });
        else fixture.Profile.ResolveAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(_ => { cancellation.Cancel(); return new PrintingTenantProfileBinding("Acme", "acme", "acme.example"); });
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => fixture.Service.ResolveAsync(record ? EnterpriseRequestPrintingSchemaContributor.SchemaKey : PrintingFormSchemaKeys.TenantProfileCard,
            fixture.Principal, cancellation.Token, record ? Guid.CreateVersion7() : null));
    }

    [TestMethod]
    [DataRow("result")]
    [DataRow("value")]
    [DataRow("error")]
    public async Task Invalid_record_source_result_returns_binding_failure(string kind)
    {
        var fixture = new Fixture();
        fixture.Record.ResolveAsync(Arg.Any<Guid>(), Arg.Any<ClaimsPrincipal>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(kind == "result" ? null! : kind == "value" ? Result<IReadOnlyDictionary<string, string?>>.Success(null!)
                : Result<IReadOnlyDictionary<string, string?>>.Failure(null!)));
        var result = await fixture.Service.ResolveAsync(EnterpriseRequestPrintingSchemaContributor.SchemaKey, fixture.Principal, recordId: Guid.CreateVersion7());
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(PrintingErrorCodes.BindingFailed, result.Error!.Code); Assert.IsNull(result.Value);
    }

    private sealed class Fixture
    {
        public IPrintingRecordBindingSource Record { get; } = Substitute.For<IPrintingRecordBindingSource>();
        public IPrintingTenantProfileBindingSource Profile { get; } = Substitute.For<IPrintingTenantProfileBindingSource>();
        public ClaimsPrincipal Principal { get; set; } = new(new ClaimsIdentity([new Claim("preferred_username", "Actor")], "test"));
        public DateTimeOffset Now { get; } = DateTimeOffset.Parse("2026-10-15T00:00:00Z");
        public PrintingFormBindingService Service { get; }
        public Fixture()
        {
            var tenant = new CurrentTenantAccessor(); tenant.SetTenant(new(Guid.CreateVersion7(), "acme", "Acme"));
            var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(Now);
            Record.FormSchemaKey.Returns(EnterpriseRequestPrintingSchemaContributor.SchemaKey);
            Record.ResolveAsync(Arg.Any<Guid>(), Arg.Any<ClaimsPrincipal>(), Arg.Any<CancellationToken>()).Returns(Result<IReadOnlyDictionary<string, string?>>.Success(Values()));
            Profile.ResolveAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new PrintingTenantProfileBinding("Acme", "acme", "acme.example"));
            Service = new(tenant, clock, Profile, new([new EnterpriseRequestPrintingSchemaContributor()]), [Record]);
        }
        public static Dictionary<string, string?> Values() => new()
        {
            ["requestNumber"] = "R-1", ["title"] = "Title", ["status"] = "Draft", ["totalAmount"] = "1.00",
            ["printedByDisplayName"] = "forged", ["printedAtUtc"] = "forged", ["private.extra"] = "private",
        };
    }

    /// <summary>业务源不能要求框架枚举其未声明字段；仅声明字段的键读取应足以完成投影。</summary>
    private sealed class SelectiveDictionary(IReadOnlyDictionary<string, string?> values) : IReadOnlyDictionary<string, string?>
    {
        public int Count => int.MaxValue;
        public IEnumerable<string> Keys => throw new InvalidOperationException("不应枚举额外键。");
        public IEnumerable<string?> Values => throw new InvalidOperationException("不应枚举额外值。");
        public string? this[string key] => values[key];
        public bool ContainsKey(string key) => values.ContainsKey(key);
        public bool TryGetValue(string key, out string? value) => values.TryGetValue(key, out value);
        public IEnumerator<KeyValuePair<string, string?>> GetEnumerator() => throw new InvalidOperationException("不应复制业务源全量字段。");
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
