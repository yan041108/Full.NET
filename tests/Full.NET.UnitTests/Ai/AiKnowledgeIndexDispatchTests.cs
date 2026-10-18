using System.Data;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>派发复核不得使用旧租约，等待数据库期间到期或取消也必须关闭。</summary>
[TestClass]
public sealed class AiKnowledgeIndexDispatchTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 1, 0, 0, TimeSpan.Zero);

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Dispatch_reads_current_snapshot_in_trusted_scope(bool tenantScope)
    {
        var (store, query, clock) = Create(tenantScope);
        clock.UtcNow.Returns(Now);
        var binding = Binding(); var task = Guid.CreateVersion7(); var lease = Guid.CreateVersion7();
        var expected = Snapshot(binding, task, lease);
        query.QuerySingleOrDefaultAsync<AiKnowledgeIndexDispatchRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(expected);
        using var cancellation = new CancellationTokenSource();
        Assert.AreSame(expected, await store.ReadDispatchAsync(binding, task, lease, cancellation.Token));
        var args = query.ReceivedCalls().Single().GetArguments();
        var sql = (SqlStatement)args[0]!;
        Assert.AreEqual(tenantScope ? SqlDataScope.TenantRequired : SqlDataScope.HostOnly, sql.Scope);
        Assert.AreEqual(tenantScope ? SqlTenantBinding.CurrentTenantId : SqlTenantBinding.None, sql.TenantBinding);
        var parameters = (Dictionary<string, object?>)args[1]!;
        Assert.IsFalse(parameters.ContainsKey("TenantId"));
        Assert.AreEqual(task, parameters["TaskId"]); Assert.AreEqual(lease, parameters["LeaseToken"]);
        Assert.AreEqual(Now, parameters["Now"]); Assert.AreEqual(cancellation.Token, args[2]);
    }

    [TestMethod]
    [DataRow("task")]
    [DataRow("document")]
    [DataRow("source")]
    [DataRow("document-version")]
    [DataRow("knowledge-version")]
    [DataRow("token")]
    [DataRow("expiry")]
    [DataRow("grant-expiry")]
    public async Task Mismatched_or_expired_snapshot_is_rejected(string invalid)
    {
        var (store, query, clock) = Create(false); clock.UtcNow.Returns(Now);
        var binding = Binding(); var task = Guid.CreateVersion7(); var lease = Guid.CreateVersion7();
        query.QuerySingleOrDefaultAsync<AiKnowledgeIndexDispatchRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(Snapshot(binding, task, lease, invalid));
        Assert.IsNull(await store.ReadDispatchAsync(binding, task, lease, CancellationToken.None));
    }

    [TestMethod]
    public async Task Lease_expiring_during_database_read_is_rejected()
    {
        var (store, query, clock) = Create(false); clock.UtcNow.Returns(Now, Now.AddSeconds(30));
        var binding = Binding(); var task = Guid.CreateVersion7(); var lease = Guid.CreateVersion7();
        query.QuerySingleOrDefaultAsync<AiKnowledgeIndexDispatchRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(Snapshot(binding, task, lease));
        Assert.IsNull(await store.ReadDispatchAsync(binding, task, lease, CancellationToken.None));
    }

    [TestMethod]
    public async Task Grant_expiring_during_database_read_is_rejected_even_with_a_live_lease()
    {
        var (store, query, clock) = Create(false); clock.UtcNow.Returns(Now);
        var binding = Binding(); var task = Guid.CreateVersion7(); var lease = Guid.CreateVersion7();
        var expected = Snapshot(binding, task, lease, "grant-future");
        query.QuerySingleOrDefaultAsync<AiKnowledgeIndexDispatchRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(_ => { clock.UtcNow.Returns(Now.AddSeconds(6)); return expected; });
        Assert.IsNull(await store.ReadDispatchAsync(binding, task, lease, default));
    }

    [TestMethod]
    public async Task Cancellation_during_database_read_is_propagated()
    {
        var (store, query, clock) = Create(false); clock.UtcNow.Returns(Now);
        var binding = Binding(); var task = Guid.CreateVersion7(); var lease = Guid.CreateVersion7();
        using var cancellation = new CancellationTokenSource();
        query.QuerySingleOrDefaultAsync<AiKnowledgeIndexDispatchRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(_ => { cancellation.Cancel(); return Snapshot(binding, task, lease); });
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => store.ReadDispatchAsync(binding, task, lease, cancellation.Token));
    }

    [TestMethod]
    [DataRow("task")]
    [DataRow("token")]
    [DataRow("binding")]
    public async Task Invalid_identity_is_rejected_before_read(string invalid)
    {
        var (store, query, _) = Create(false);
        var binding = Binding();
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => store.ReadDispatchAsync(invalid == "binding" ? binding with { VersionId = Guid.Empty } : binding,
            invalid == "task" ? Guid.Empty : Guid.CreateVersion7(), invalid == "token" ? Guid.Empty : Guid.CreateVersion7(), CancellationToken.None));
        Assert.IsFalse(query.ReceivedCalls().Any());
    }

    [TestMethod]
    public async Task Missing_authority_returns_no_snapshot()
    {
        var (store, _, _) = Create(false);
        Assert.IsNull(await store.ReadDispatchAsync(Binding(), Guid.CreateVersion7(), Guid.CreateVersion7(), CancellationToken.None));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Native_reader_preserves_source_and_model_snapshot(bool mysql)
    {
        using var table = new DataTable();
        foreach (var column in new[] { "TaskId", "DocumentId", "VersionId", "ModelConfigId", "LeaseToken", "FileId" }) table.Columns.Add(column, typeof(Guid));
        foreach (var column in new[] { "DocumentVersion", "KnowledgeBaseVersion", "ModelConfigVersion" }) table.Columns.Add(column, typeof(int));
        table.Columns.Add("ContentHash", typeof(string)); table.Columns.Add("ContentType", typeof(string));
        foreach (var column in new[] { "DataClassification", "ModelProviderKey", "ModelId", "ModelEndpointBaseUrl", "ModelOrganizationId" }) table.Columns.Add(column, typeof(string));
        table.Columns.Add("SizeBytes", typeof(long)); table.Columns.Add("LeaseExpiresAtUtc", mysql ? typeof(DateTime) : typeof(DateTimeOffset));
        table.Columns.Add("GrantExpiresAtUtc", mysql ? typeof(DateTime) : typeof(DateTimeOffset));
        var data = table.NewRow();
        foreach (var column in new[] { "TaskId", "DocumentId", "VersionId", "ModelConfigId", "LeaseToken", "FileId" }) data[column] = Guid.CreateVersion7();
        data["DocumentVersion"] = 9; data["KnowledgeBaseVersion"] = 6; data["ModelConfigVersion"] = 3;
        data["ContentHash"] = "trusted-hash"; data["ContentType"] = "text/plain"; data["SizeBytes"] = 123L;
        data["DataClassification"] = "internal"; data["ModelProviderKey"] = "openai_compatible"; data["ModelId"] = "text-embedding-3-small";
        data["ModelEndpointBaseUrl"] = "https://api.openai.com/v1"; data["ModelOrganizationId"] = mysql ? DBNull.Value : "organization";
        data["LeaseExpiresAtUtc"] = mysql ? Now.UtcDateTime : (object)Now; data["GrantExpiresAtUtc"] = DBNull.Value; table.Rows.Add(data);
        using var reader = table.CreateDataReader(); Assert.IsTrue(reader.Read());
        var row = AiBudgetRowReaders.ReadKnowledgeIndexDispatch(reader);
        Assert.AreEqual((Guid)data["TaskId"], row.TaskId); Assert.AreEqual((Guid)data["DocumentId"], row.DocumentId);
        Assert.AreEqual((Guid)data["VersionId"], row.VersionId); Assert.AreEqual((Guid)data["ModelConfigId"], row.ModelConfigId);
        Assert.AreEqual((Guid)data["FileId"], row.FileId); Assert.AreEqual((Guid)data["LeaseToken"], row.LeaseToken);
        Assert.AreEqual(9, row.DocumentVersion); Assert.AreEqual(6, row.KnowledgeBaseVersion); Assert.AreEqual(3, row.ModelConfigVersion);
        Assert.AreEqual("trusted-hash", row.ContentHash); Assert.AreEqual("text/plain", row.ContentType);
        Assert.AreEqual(123L, row.SizeBytes); Assert.AreEqual(Now, row.LeaseExpiresAtUtc);
        Assert.AreEqual("internal", row.DataClassification); Assert.AreEqual("openai_compatible", row.ModelProviderKey);
        Assert.AreEqual("text-embedding-3-small", row.ModelId); Assert.AreEqual("https://api.openai.com/v1", row.ModelEndpointBaseUrl);
        Assert.AreEqual(mysql ? null : "organization", row.ModelOrganizationId);
    }

    [TestMethod] [DataRow("dispatch-host")] [DataRow("dispatch-tenant")]
    [DataRow("sqlserver-host")] [DataRow("sqlserver-tenant")] [DataRow("mysql-host")] [DataRow("mysql-tenant")]
    public void Every_dispatch_and_activation_projection_satisfies_the_native_reader(string shape)
    {
        var statement = shape switch
        {
            "dispatch-host" => AiKnowledgeIndexTaskSql.DispatchHost, "dispatch-tenant" => AiKnowledgeIndexTaskSql.DispatchTenant,
            "sqlserver-host" => AiKnowledgeIndexActivationSql.AuthoritySqlServerHost, "sqlserver-tenant" => AiKnowledgeIndexActivationSql.AuthoritySqlServerTenant,
            "mysql-host" => AiKnowledgeIndexActivationSql.AuthorityMySqlHost, _ => AiKnowledgeIndexActivationSql.AuthorityMySqlTenant
        };
        // 从生产 SELECT 的实际列形状构造行，避免手工补列让原生读取回归被 JIT 默认值掩盖。
        var head = statement.Text[..statement.Text.IndexOf(" FROM ", StringComparison.Ordinal)]["SELECT ".Length..];
        var columns = new List<string>(); var start = 0; var depth = 0;
        for (var index = 0; index <= head.Length; index++)
        {
            if (index < head.Length && head[index] == '(') depth++;
            if (index < head.Length && head[index] == ')') depth--;
            if (index < head.Length && (head[index] != ',' || depth != 0)) continue;
            columns.Add(System.Text.RegularExpressions.Regex.Match(head[start..index].Trim(), @"[A-Za-z_][A-Za-z_0-9]*$").Value); start = index + 1;
        }
        var values = new Dictionary<string, object>
        {
            ["TaskId"] = Guid.CreateVersion7(), ["DocumentId"] = Guid.CreateVersion7(), ["VersionId"] = Guid.CreateVersion7(),
            ["ModelConfigId"] = Guid.CreateVersion7(), ["LeaseToken"] = Guid.CreateVersion7(), ["FileId"] = Guid.CreateVersion7(),
            ["DocumentVersion"] = 1, ["KnowledgeBaseVersion"] = 1, ["ModelConfigVersion"] = 1,
            ["ContentHash"] = "hash", ["ContentType"] = "application/pdf", ["SizeBytes"] = 123L, ["LeaseExpiresAtUtc"] = Now,
            ["DataClassification"] = "internal", ["ModelProviderKey"] = "openai_compatible", ["ModelId"] = "text-embedding-3-small",
            ["ModelEndpointBaseUrl"] = "https://api.openai.com/v1", ["ModelOrganizationId"] = DBNull.Value, ["GrantExpiresAtUtc"] = DBNull.Value
        };
        using var table = new DataTable(); foreach (var column in columns) table.Columns.Add(column, typeof(object));
        var data = table.NewRow(); foreach (var column in columns) data[column] = values[column]; table.Rows.Add(data);
        using var reader = table.CreateDataReader(); Assert.IsTrue(reader.Read());
        var row = AiBudgetRowReaders.ReadKnowledgeIndexDispatch(reader);
        Assert.AreEqual("internal", row.DataClassification); Assert.AreEqual("https://api.openai.com/v1", row.ModelEndpointBaseUrl);
        Assert.AreEqual("openai_compatible", row.ModelProviderKey); Assert.AreEqual("text-embedding-3-small", row.ModelId); Assert.IsNull(row.ModelOrganizationId);
    }

    private static AiKnowledgeIndexDispatchRecord Snapshot(AiKnowledgeIndexTaskBinding binding, Guid task, Guid lease, string invalid = "") => new()
    {
        TaskId = invalid == "task" ? Guid.CreateVersion7() : task,
        DocumentId = invalid == "document" ? Guid.CreateVersion7() : binding.DocumentId,
        VersionId = invalid == "source" ? Guid.CreateVersion7() : binding.VersionId,
        DocumentVersion = invalid == "document-version" ? binding.DocumentVersion + 1 : binding.DocumentVersion,
        KnowledgeBaseVersion = invalid == "knowledge-version" ? binding.KnowledgeBaseVersion + 1 : binding.KnowledgeBaseVersion,
        LeaseToken = invalid == "token" ? Guid.CreateVersion7() : lease,
        LeaseExpiresAtUtc = invalid == "expiry" ? Now : Now.AddSeconds(30),
        GrantExpiresAtUtc = invalid == "grant-expiry" ? Now : invalid == "grant-future" ? Now.AddSeconds(5) : null,
        ModelConfigId = Guid.CreateVersion7(), ModelConfigVersion = 3, FileId = Guid.CreateVersion7(),
        ContentHash = "trusted-hash", ContentType = "text/plain", SizeBytes = 123,
    };
    private static AiKnowledgeIndexTaskBinding Binding() => new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 1, 1, Guid.CreateVersion7());
    private static (AiKnowledgeIndexTaskStore Store, IQueryExecutor Query, IClock Clock) Create(bool tenantScope)
    {
        var tenant = new CurrentTenantAccessor();
        if (tenantScope) tenant.SetTenant(new TenantContext(Guid.CreateVersion7(), "test", "Test")); else tenant.SetHost();
        var query = Substitute.For<IQueryExecutor>(); var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(Now);
        return (new AiKnowledgeIndexTaskStore(query, Substitute.For<ICommandExecutor>(), tenant, clock, Substitute.For<IIdGenerator>()), query, clock);
    }
}
