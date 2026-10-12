using System.Data;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.UnitTests.Ai;

/// <summary>脱离真实数据库验证原生静态物化，尤其两库 SUM 的 decimal 返回类型。</summary>
[TestClass]
public sealed class AiBudgetRowReaderTests
{
    [TestMethod] [DataRow(false, false)] [DataRow(true, false)] [DataRow(false, true)] [DataRow(true, true)]
    public void Feedback_review_projection_preserves_left_join_versions_and_owner(bool mysql, bool absent)
    {
        using var table = new DataTable(); var id = Guid.CreateVersion7(); var reviewer = Guid.CreateVersion7();
        foreach (var name in new[] { "FeedbackId", "SessionId", "MessageId", "OwnerUserId", "ReviewerUserId" }) table.Columns.Add(name, typeof(Guid));
        foreach (var name in new[] { "RatingKey", "ReasonKey", "CitationIdsJson", "LineageHash", "KnowledgeLineage", "ReviewLineageHash", "DecisionKey" }) table.Columns.Add(name, typeof(string));
        foreach (var name in new[] { "FeedbackVersion", "ReviewFeedbackVersion", "ReviewVersion" }) table.Columns.Add(name, mysql ? typeof(long) : typeof(int));
        var row = table.NewRow();
        foreach (DataColumn column in table.Columns) row[column] = column.DataType == typeof(Guid) ? id : column.DataType == typeof(string) ? "value" : 7;
        row["ReviewerUserId"] = reviewer;
        if (absent) foreach (var name in new[] { "ReviewerUserId", "ReviewLineageHash", "ReviewFeedbackVersion", "ReviewVersion", "DecisionKey" }) row[name] = DBNull.Value;
        table.Rows.Add(row); using var reader = table.CreateDataReader(); Assert.IsTrue(reader.Read());
        var result = AiBudgetRowReaders.ReadAnswerFeedbackReview(reader);
        Assert.AreEqual(id, result.OwnerUserId); Assert.AreEqual(7, result.FeedbackVersion);
        Assert.AreEqual(absent ? null : (int?)7, result.ReviewVersion);
        Assert.AreEqual(absent ? null : (Guid?)reviewer, result.ReviewerUserId);
    }

    [TestMethod] [DataRow(false, false)] [DataRow(true, false)] [DataRow(false, true)] [DataRow(true, true)]
    public void Chat_message_reads_knowledge_marker_independent_of_nullable_lineage(bool mysql, bool absent)
    {
        using var table = new DataTable(); var id = Guid.CreateVersion7(); var now = DateTime.UtcNow;
        foreach (var name in new[] { "Id", "SessionId" }) table.Columns.Add(name, typeof(Guid));
        foreach (var name in new[] { "RoleKey", "Content", "StatusKey", "KnowledgeLineage" }) table.Columns.Add(name, typeof(string));
        foreach (var name in new[] { "PromptTokens", "CompletionTokens" }) table.Columns.Add(name, typeof(int));
        table.Columns.Add("IsKnowledgeAnswer", mysql ? typeof(sbyte) : typeof(bool));
        table.Columns.Add("CreatedAtUtc", mysql ? typeof(DateTime) : typeof(DateTimeOffset));
        table.Rows.Add(id, id, "assistant", "知识正文", "completed", absent ? DBNull.Value : "{}", absent ? DBNull.Value : 2, absent ? DBNull.Value : 3,
            mysql ? (object)(sbyte)1 : true, mysql ? (object)now : new DateTimeOffset(now));
        // 列按名称读取，特意使元数据与标记不相邻；空元数据不允许丢失独立标记。
        table.Rows[0]["KnowledgeLineage"] = absent ? DBNull.Value : "{}";
        using var reader = table.CreateDataReader(); Assert.IsTrue(reader.Read()); var row = AiBudgetRowReaders.ReadChatMessage(reader);
        Assert.AreEqual(id, row.Id); Assert.AreEqual("知识正文", row.Content); Assert.IsTrue(row.IsKnowledgeAnswer);
        Assert.AreEqual(absent ? null : "{}", row.KnowledgeLineage); Assert.AreEqual(absent ? null : (int?)2, row.PromptTokens);
        Assert.AreEqual(new DateTimeOffset(now), row.CreatedAtUtc);
    }
    [TestMethod] [DataRow(false, false)] [DataRow(true, false)] [DataRow(false, true)] [DataRow(true, true)]
    public void Cleanup_projection_maps_provider_time_kind_and_nullable_scope(bool mysql, bool tenant)
    {
        using var table = new DataTable(); var id = Guid.CreateVersion7(); var now = DateTime.UtcNow;
        foreach (var name in new[] { "Id", "KnowledgeBaseId", "DocumentId", "OwnerUserId", "TenantId" }) table.Columns.Add(name, typeof(Guid));
        table.Columns.Add("Kind", mysql ? typeof(long) : typeof(int)); table.Columns.Add("CreatedAtUtc", mysql ? typeof(DateTime) : typeof(DateTimeOffset));
        table.Rows.Add(id, id, id, id, tenant ? (object)id : DBNull.Value, 1, mysql ? (object)now : new DateTimeOffset(now));
        using var reader = table.CreateDataReader(); Assert.IsTrue(reader.Read()); var row = AiBudgetRowReaders.ReadKnowledgeCleanup(reader);
        Assert.AreEqual(id, row.Id); Assert.AreEqual(id, row.KnowledgeBaseId); Assert.AreEqual(id, row.DocumentId); Assert.AreEqual(id, row.OwnerUserId);
        Assert.AreEqual(tenant ? id : (Guid?)null, row.TenantId); Assert.AreEqual(1, row.Kind); Assert.AreEqual(new DateTimeOffset(now), row.CreatedAtUtc);
    }

    [TestMethod] [DataRow(false, false)] [DataRow(true, false)] [DataRow(false, true)] [DataRow(true, true)]
    public void Index_status_maps_optional_joins_and_provider_integer_flags(bool mysql, bool absent)
    {
        using var table = new DataTable(); var id = Guid.CreateVersion7();
        foreach (var name in new[] { "KnowledgeBaseId", "DocumentId", "VersionId", "OwnerUserId", "FileId", "TaskId", "ActiveTaskId", "ActiveLeaseToken", "ModelConfigId", "GenerationModelConfigId" }) table.Columns.Add(name, typeof(Guid));
        foreach (var name in new[] { "DocumentVersion", "KnowledgeBaseVersion", "IsDeleted", "CleanupPending", "HasDerivedData", "IsCurrent", "SizeBytes", "ModelConfigVersion", "GenerationModelVersion" }) table.Columns.Add(name, mysql ? typeof(long) : typeof(int));
        foreach (var name in new[] { "DataClassification", "StateKey", "ActiveReceiptHash", "ActiveManifestHash", "ActivePlanHash", "ContentHash", "ContentType", "ProviderKey", "ModelId" }) table.Columns.Add(name, typeof(string));
        var row = table.NewRow();
        foreach (DataColumn column in table.Columns) row[column] = column.DataType == typeof(Guid) ? id : column.DataType == typeof(string) ? "test" : 1;
        if (absent) foreach (var name in new[] { "TaskId", "ActiveTaskId", "ActiveLeaseToken", "ModelConfigId", "GenerationModelConfigId", "ModelConfigVersion", "GenerationModelVersion", "StateKey", "ActiveReceiptHash", "ActiveManifestHash", "ActivePlanHash", "ProviderKey", "ModelId" }) row[name] = DBNull.Value;
        table.Rows.Add(row); using var reader = table.CreateDataReader(); Assert.IsTrue(reader.Read());
        var result = AiBudgetRowReaders.ReadKnowledgeIndexStatus(reader);
        Assert.AreEqual("test", result.DataClassification);
        Assert.AreEqual(absent ? null : (Guid?)id, result.GenerationModelConfigId);
        Assert.AreEqual(absent ? null : (int?)1, result.GenerationModelVersion);
        Assert.AreEqual(id, result.VersionId); Assert.AreEqual(1, result.DocumentVersion); Assert.AreEqual(1, result.IsCurrent);
        Assert.AreEqual(1, result.CleanupPending); Assert.AreEqual(1L, result.SizeBytes); Assert.AreEqual(absent ? null : (Guid?)id, result.TaskId);
        Assert.AreEqual(absent ? null : (int?)1, result.ModelConfigVersion); Assert.AreEqual(absent ? null : "test", result.ActiveManifestHash);
    }

    [TestMethod] [DataRow(false)] [DataRow(true)]
    public void Enqueue_projection_maps_authoritative_source_and_model_versions(bool mysql)
    {
        using var table = new DataTable();
        var names = new[] { "KnowledgeBaseId", "DocumentId", "VersionId", "OwnerUserId", "ModelConfigId" };
        foreach (var name in names) table.Columns.Add(name, typeof(Guid));
        foreach (var name in new[] { "DocumentVersion", "KnowledgeBaseVersion", "ModelConfigVersion" }) table.Columns.Add(name, mysql ? typeof(long) : typeof(decimal));
        table.Columns.Add("ContentType", typeof(string)); var ids = names.Select(_ => Guid.CreateVersion7()).ToArray();
        table.Rows.Add(ids[0], ids[1], ids[2], ids[3], ids[4], 3, 4, 5, "text/markdown");
        using var reader = table.CreateDataReader(); Assert.IsTrue(reader.Read()); var row = AiBudgetRowReaders.ReadKnowledgeIndexEnqueue(reader);
        Assert.AreEqual(ids[0], row.KnowledgeBaseId); Assert.AreEqual(ids[1], row.DocumentId); Assert.AreEqual(ids[2], row.VersionId);
        Assert.AreEqual(ids[3], row.OwnerUserId); Assert.AreEqual(ids[4], row.ModelConfigId); Assert.AreEqual(3, row.DocumentVersion);
        Assert.AreEqual(4, row.KnowledgeBaseVersion); Assert.AreEqual(5, row.ModelConfigVersion); Assert.AreEqual("text/markdown", row.ContentType);
    }

    [TestMethod] [DataRow(false)] [DataRow(true)]
    public void Worker_projection_maps_original_scope_and_bounded_metadata(bool tenant)
    {
        using var table = new DataTable();
        var names = new[] { "TaskId", "TenantId", "KnowledgeBaseId", "DocumentId", "VersionId", "OwnerUserId" };
        foreach (var name in names) table.Columns.Add(name, typeof(Guid));
        table.Columns.Add("DocumentVersion", typeof(long)); table.Columns.Add("KnowledgeBaseVersion", typeof(decimal));
        table.Columns.Add("ContentType", typeof(string));
        var ids = names.Select(_ => Guid.CreateVersion7()).ToArray();
        table.Rows.Add(ids[0], tenant ? (object)ids[1] : DBNull.Value, ids[2], ids[3], ids[4], ids[5], 2L, 3m, "application/pdf");
        using var reader = table.CreateDataReader(); Assert.IsTrue(reader.Read());
        var row = AiBudgetRowReaders.ReadKnowledgeWorker(reader);
        Assert.AreEqual(ids[0], row.TaskId); Assert.AreEqual(tenant ? ids[1] : (Guid?)null, row.TenantId);
        Assert.AreEqual(ids[2], row.KnowledgeBaseId); Assert.AreEqual(ids[3], row.DocumentId); Assert.AreEqual(ids[4], row.VersionId);
        Assert.AreEqual(ids[5], row.OwnerUserId); Assert.AreEqual(2, row.DocumentVersion); Assert.AreEqual(3, row.KnowledgeBaseVersion);
        Assert.AreEqual("application/pdf", row.ContentType);
    }

    [TestMethod] [DataRow(false)] [DataRow(true)]
    public void Activation_pointer_maps_original_lease_and_frozen_receipt_json(bool mysql)
    {
        using var table = new DataTable(); var now = DateTime.UtcNow;
        var names = new[] { "Id", "DocumentId", "IndexTaskId", "VersionId", "LeaseToken" };
        foreach (var name in names) table.Columns.Add(name, typeof(Guid));
        foreach (var name in new[] { "PlanHash", "ManifestHash", "ReceiptHash", "ReceiptJson" }) table.Columns.Add(name, typeof(string));
        table.Columns.Add("ActivatedAtUtc", mysql ? typeof(DateTime) : typeof(DateTimeOffset));
        var ids = names.Select(_ => Guid.CreateVersion7()).ToArray();
        table.Rows.Add(ids[0], ids[1], ids[2], ids[3], ids[4], "plan", "manifest", "receipt", "[]", mysql ? (object)now : new DateTimeOffset(now));
        using var reader = table.CreateDataReader(); Assert.IsTrue(reader.Read()); var row = AiBudgetRowReaders.ReadKnowledgeActivation(reader);
        Assert.AreEqual(ids[0], row.Id); Assert.AreEqual(ids[1], row.DocumentId); Assert.AreEqual(ids[2], row.IndexTaskId);
        Assert.AreEqual(ids[3], row.VersionId); Assert.AreEqual(ids[4], row.LeaseToken); Assert.AreEqual("[]", row.ReceiptJson);
        Assert.AreEqual("plan", row.PlanHash); Assert.AreEqual("manifest", row.ManifestHash); Assert.AreEqual("receipt", row.ReceiptHash);
        Assert.AreEqual(new DateTimeOffset(now), row.ActivatedAtUtc);
    }

    [TestMethod]
    public void Vector_receipt_maps_exact_operation_ordinal_and_hashes_without_payload()
    {
        using var table = new DataTable(); var id = Guid.CreateVersion7();
        table.Columns.Add("Id", typeof(Guid)); table.Columns.Add("BatchOrdinal", typeof(int)); table.Columns.Add("InputCount", typeof(int));
        table.Columns.Add("RequestHash", typeof(string)); table.Columns.Add("PayloadHash", typeof(string));
        foreach (var name in new[] { "ProviderKey", "ModelId", "FormatKey", "DistanceKey" }) table.Columns.Add(name, typeof(string));
        table.Columns.Add("Dimensions", typeof(int));
        table.Rows.Add(id, 2, 16, "request", "payload", "provider", "model", "format", "cosine", 1536);
        using var reader = table.CreateDataReader(); Assert.IsTrue(reader.Read());
        var row = AiBudgetRowReaders.ReadKnowledgeVectorReceipt(reader);
        Assert.AreEqual(id, row.Id); Assert.AreEqual(2, row.BatchOrdinal); Assert.AreEqual(16, row.InputCount);
        Assert.AreEqual("request", row.RequestHash); Assert.AreEqual("payload", row.PayloadHash);
        Assert.AreEqual("provider", row.ProviderKey); Assert.AreEqual("model", row.ModelId); Assert.AreEqual(1536, row.Dimensions);
        Assert.AreEqual("format", row.FormatKey); Assert.AreEqual("cosine", row.DistanceKey);
    }

    [TestMethod] [DataRow(false)] [DataRow(true)]
    public void Text_manifest_maps_exact_payload_and_provider_time_shape(bool mysql)
    {
        using var table = new DataTable(); var now = DateTime.UtcNow; var id = Guid.CreateVersion7(); byte[] bytes = [1, 2, 3];
        table.Columns.Add("Id", typeof(Guid)); foreach (var name in new[] { "FormatKey", "PayloadHash" }) table.Columns.Add(name, typeof(string));
        table.Columns.Add("Payload", typeof(byte[])); table.Columns.Add("CreatedAtUtc", mysql ? typeof(DateTime) : typeof(DateTimeOffset));
        table.Rows.Add(id, "format", "hash", bytes, mysql ? (object)now : new DateTimeOffset(now));
        using var reader = table.CreateDataReader(); Assert.IsTrue(reader.Read()); var row = AiBudgetRowReaders.ReadKnowledgeTextManifest(reader);
        Assert.AreEqual(id, row.Id); Assert.AreEqual("format", row.FormatKey); Assert.AreEqual("hash", row.PayloadHash);
        CollectionAssert.AreEqual(bytes, row.Payload); Assert.AreEqual(new DateTimeOffset(now), row.CreatedAtUtc);
    }

    [TestMethod]
    public void Index_vector_maps_exact_bytes_and_space_metadata()
    {
        using var table = new DataTable();
        foreach (var name in new[] { "Id", "IndexTaskId", "ModelConfigId" }) table.Columns.Add(name, typeof(Guid));
        foreach (var name in new[] { "BatchOrdinal", "InputCount", "ModelConfigVersion", "Dimensions" }) table.Columns.Add(name, typeof(int));
        foreach (var name in new[] { "RequestHash", "ProviderKey", "ModelId", "FormatKey", "DistanceKey", "PayloadHash" }) table.Columns.Add(name, typeof(string));
        table.Columns.Add("Payload", typeof(byte[])); table.Columns.Add("CreatedAtUtc", typeof(DateTime));
        var id = Guid.CreateVersion7(); var task = Guid.CreateVersion7(); var model = Guid.CreateVersion7(); byte[] bytes = [1, 2, 3];
        var now = new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Unspecified);
        table.Rows.Add(id, task, model, 2, 16, 7, 1536, "request", "provider", "model", "format", "cosine", "payloadhash", bytes, now);
        using var reader = table.CreateDataReader(); Assert.IsTrue(reader.Read()); var row = AiBudgetRowReaders.ReadKnowledgeIndexVector(reader);
        Assert.AreEqual(id, row.Id); Assert.AreEqual(task, row.IndexTaskId); Assert.AreEqual(model, row.ModelConfigId);
        Assert.AreEqual(2, row.BatchOrdinal); Assert.AreEqual(16, row.InputCount); Assert.AreEqual(7, row.ModelConfigVersion);
        Assert.AreEqual(1536, row.Dimensions); Assert.AreEqual("request", row.RequestHash); Assert.AreEqual("provider", row.ProviderKey);
        Assert.AreEqual("model", row.ModelId); Assert.AreEqual("format", row.FormatKey); Assert.AreEqual("cosine", row.DistanceKey);
        Assert.AreEqual("payloadhash", row.PayloadHash); CollectionAssert.AreEqual(bytes, row.Payload);
        Assert.AreEqual(new DateTimeOffset(now, TimeSpan.Zero), row.CreatedAtUtc);
    }

    [TestMethod] [DataRow(false)] [DataRow(true)]
    public void Index_session_maps_frozen_identity_and_nullable_lease(bool tenant)
    {
        using var table = new DataTable();
        foreach (var name in new[] { "Id", "IndexTaskId", "ActorUserId", "TenantId", "SessionId" }) table.Columns.Add(name, typeof(Guid));
        foreach (var name in new[] { "SessionKind", "SecurityStamp", "ActorScope", "EffectiveScope" }) table.Columns.Add(name, typeof(string));
        foreach (var name in new[] { "CreatedAtUtc", "LeaseExpiresAtUtc" }) table.Columns.Add(name, typeof(DateTime));
        var id = Guid.CreateVersion7(); var task = Guid.CreateVersion7(); var user = Guid.CreateVersion7(); var session = Guid.CreateVersion7(); var tenantId = Guid.CreateVersion7();
        var now = new DateTime(2026, 10, 4, 0, 0, 0, DateTimeKind.Unspecified);
        table.Rows.Add(id, task, user, tenant ? tenantId : DBNull.Value, session, "oidc-application", "戳", "host", "effective", now, tenant ? now : DBNull.Value);
        using var reader = table.CreateDataReader(); Assert.IsTrue(reader.Read());
        var row = AiBudgetRowReaders.ReadKnowledgeIndexSession(reader);
        Assert.AreEqual(id, row.Id); Assert.AreEqual(task, row.IndexTaskId); Assert.AreEqual(user, row.ActorUserId); Assert.AreEqual(session, row.SessionId);
        Assert.AreEqual(tenant ? tenantId : (Guid?)null, row.TenantId); Assert.AreEqual("oidc-application", row.SessionKind);
        Assert.AreEqual("戳", row.SecurityStamp); Assert.AreEqual("host", row.ActorScope); Assert.AreEqual("effective", row.EffectiveScope);
        Assert.AreEqual(new DateTimeOffset(now, TimeSpan.Zero), row.CreatedAtUtc);
        Assert.AreEqual(tenant ? row.CreatedAtUtc : (DateTimeOffset?)null, row.LeaseExpiresAtUtc);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Model_config_maps_host_nulls_and_populated_tenant_columns(bool populated)
    {
        using var table = new DataTable();
        foreach (var name in new[] { "Id", "TenantId" }) table.Columns.Add(name, typeof(Guid));
        foreach (var name in new[] { "Name", "ProviderKey", "EndpointBaseUrl", "ModelId", "ApiKeyProtected",
                     "OrganizationId", "LastTestStatusKey", "LastTestMessage" }) table.Columns.Add(name, typeof(string));
        foreach (var name in new[] { "IsDefault", "IsEnabled" }) table.Columns.Add(name, typeof(byte));
        foreach (var name in new[] { "CreatedAtUtc", "UpdatedAtUtc", "LastTestedAtUtc" }) table.Columns.Add(name, typeof(DateTime));
        table.Columns.Add("Version", typeof(int));
        var row = table.NewRow();
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var now = new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Unspecified);
        row["Id"] = id;
        row["Name"] = "模型";
        row["ProviderKey"] = "ollama";
        row["EndpointBaseUrl"] = "https://model.example";
        row["ModelId"] = "model-v1";
        row["IsDefault"] = (byte)0;
        row["IsEnabled"] = (byte)1;
        row["CreatedAtUtc"] = now;
        row["Version"] = 3;
        if (populated)
        {
            row["TenantId"] = tenantId;
            row["ApiKeyProtected"] = "protected";
            row["OrganizationId"] = "org";
            row["LastTestedAtUtc"] = now;
            row["LastTestStatusKey"] = "succeeded";
            row["LastTestMessage"] = "摘要";
            row["UpdatedAtUtc"] = now;
        }
        table.Rows.Add(row);
        using var reader = table.CreateDataReader();
        Assert.IsTrue(reader.Read());
        var model = AiBudgetRowReaders.ReadModelConfig(reader);
        Assert.AreEqual(id, model.Id);
        Assert.AreEqual("模型", model.Name);
        Assert.AreEqual("ollama", model.ProviderKey);
        Assert.AreEqual("https://model.example", model.EndpointBaseUrl);
        Assert.AreEqual("model-v1", model.ModelId);
        Assert.IsFalse(model.IsDefault);
        Assert.IsTrue(model.IsEnabled);
        Assert.AreEqual(3, model.Version);
        Assert.AreEqual(new DateTimeOffset(now, TimeSpan.Zero), model.CreatedAtUtc);
        Assert.AreEqual(populated ? tenantId : (Guid?)null, model.TenantId);
        Assert.AreEqual(populated ? "protected" : null, model.ApiKeyProtected);
        Assert.AreEqual(populated ? "org" : null, model.OrganizationId);
        Assert.AreEqual(populated ? "succeeded" : null, model.LastTestStatusKey);
        Assert.AreEqual(populated ? "摘要" : null, model.LastTestMessage);
        Assert.AreEqual(populated ? model.CreatedAtUtc : (DateTimeOffset?)null, model.LastTestedAtUtc);
        Assert.AreEqual(populated ? model.CreatedAtUtc : (DateTimeOffset?)null, model.UpdatedAtUtc);
    }

    [TestMethod]
    public void Totals_convert_database_decimal_aggregates_without_truncation()
    {
        using var table = new DataTable();
        foreach (var name in new[] { "MonthlyRequests", "MonthlyTokens", "RunRequests", "RunTokens" }) table.Columns.Add(name, typeof(decimal));
        table.Rows.Add(2m, 3000000000m, 1m, 30m);
        using var reader = table.CreateDataReader(); Assert.IsTrue(reader.Read());
        var totals = AiBudgetRowReaders.ReadTotals(reader);
        Assert.AreEqual(2L, totals.MonthlyRequests); Assert.AreEqual(3000000000L, totals.MonthlyTokens);
        Assert.AreEqual(1L, totals.RunRequests); Assert.AreEqual(30L, totals.RunTokens);
    }

    [TestMethod]
    public void Unknown_operation_preserves_nullable_usage_and_price()
    {
        using var table = new DataTable();
        foreach (var name in new[] { "Id", "RunId", "ModelConfigId", "PriceVersionId" }) table.Columns.Add(name, typeof(Guid));
        foreach (var name in new[] { "RequestHash", "QuotaMonthKey", "Currency", "UsageStatus", "Outcome" }) table.Columns.Add(name, typeof(string));
        foreach (var name in new[] { "ReservedTokens", "InputTokens", "OutputTokens", "CachedInputTokens" }) table.Columns.Add(name, typeof(long));
        foreach (var name in new[] { "ReservedCost", "InputPerMillion", "OutputPerMillion", "CachedInputPerMillion" }) table.Columns.Add(name, typeof(decimal));
        table.Columns.Add("LegacyTracked", typeof(byte));
        var row = table.NewRow(); row["Id"] = Guid.NewGuid(); row["ModelConfigId"] = Guid.NewGuid();
        row["RequestHash"] = new string('A', 64); row["QuotaMonthKey"] = "2026-09"; row["UsageStatus"] = "unknown";
        row["Outcome"] = "failed"; row["ReservedTokens"] = 30L; row["LegacyTracked"] = (byte)1;
        table.Rows.Add(row);
        using var reader = table.CreateDataReader(); Assert.IsTrue(reader.Read());
        var value = AiBudgetRowReaders.ReadOperation(reader);
        Assert.IsNull(value.ReservedCost); Assert.IsNull(value.InputTokens); Assert.IsNull(value.PriceVersionId);
        Assert.AreEqual(30L, value.ReservedTokens); Assert.IsTrue(value.LegacyTracked); Assert.AreEqual("unknown", value.UsageStatus);
    }

    [TestMethod]
    public void Mcp_remote_tool_maps_executable_catalog_columns()
    {
        using var table = new DataTable();
        foreach (var name in new[] { "ConnectionId" }) table.Columns.Add(name, typeof(Guid));
        foreach (var name in new[]
                 {
                     "ConnectionKey", "EndpointUrl", "LocalToolName", "RemoteToolName", "InputSchemaJson",
                     "InputSchemaHash", "SideEffectKey", "PermissionCode", "ApprovalStatusKey", "ServiceTokenProtected"
                 })
            table.Columns.Add(name, typeof(string));
        table.Columns.Add("ToolVersion", typeof(int));
        var connectionId = Guid.NewGuid();
        var row = table.NewRow();
        row["ConnectionId"] = connectionId;
        row["ConnectionKey"] = "demo";
        row["EndpointUrl"] = "https://mcp.example";
        row["LocalToolName"] = "local.tool";
        row["RemoteToolName"] = "remote.tool";
        row["ToolVersion"] = 2;
        row["InputSchemaJson"] = "{}";
        row["InputSchemaHash"] = "abc";
        row["SideEffectKey"] = "read";
        row["PermissionCode"] = "ai.agent-tools.view";
        row["ApprovalStatusKey"] = "approved";
        row["ServiceTokenProtected"] = "protected";
        table.Rows.Add(row);
        using var reader = table.CreateDataReader();
        Assert.IsTrue(reader.Read());
        var value = AiBudgetRowReaders.ReadMcpRemoteTool(reader);
        Assert.AreEqual(connectionId, value.ConnectionId);
        Assert.AreEqual("local.tool", value.LocalToolName);
        Assert.AreEqual(2, value.ToolVersion);
        Assert.AreEqual("approved", value.ApprovalStatusKey);
    }

    [TestMethod]
    public void Agent_approval_maps_nullable_columns()
    {
        using var table = new DataTable();
        foreach (var name in new[] { "Id", "RunId", "OperationId", "SessionId", "RequestedBy" }) table.Columns.Add(name, typeof(Guid));
        foreach (var name in new[] { "ScopeKey", "ToolName", "ArgumentsHash", "ArgumentsProtected", "PresentationJson", "DecisionKey" })
            table.Columns.Add(name, typeof(string));
        table.Columns.Add("TenantId", typeof(Guid));
        table.Columns.Add("ApproverId", typeof(Guid));
        table.Columns.Add("ToolVersion", typeof(int));
        table.Columns.Add("PolicyVersion", typeof(int));
        table.Columns.Add("Version", typeof(long));
        table.Columns.Add("ExpiresAtUtc", typeof(DateTimeOffset));
        table.Columns.Add("ConsumedAtUtc", typeof(DateTimeOffset));
        table.Columns.Add("CreatedAtUtc", typeof(DateTimeOffset));
        table.Columns.Add("UpdatedAtUtc", typeof(DateTimeOffset));
        var id = Guid.NewGuid();
        var row = table.NewRow();
        row["Id"] = id;
        row["ScopeKey"] = "host";
        row["TenantId"] = DBNull.Value;
        row["RunId"] = Guid.NewGuid();
        row["OperationId"] = Guid.NewGuid();
        row["SessionId"] = Guid.NewGuid();
        row["ToolName"] = "demo.tool";
        row["ToolVersion"] = 1;
        row["ArgumentsHash"] = "hash";
        row["ArgumentsProtected"] = "protected";
        row["PolicyVersion"] = 2;
        row["PresentationJson"] = "{}";
        row["RequestedBy"] = Guid.NewGuid();
        row["ApproverId"] = DBNull.Value;
        row["DecisionKey"] = "pending";
        row["ExpiresAtUtc"] = DateTimeOffset.UtcNow;
        row["ConsumedAtUtc"] = DBNull.Value;
        row["Version"] = 1L;
        row["CreatedAtUtc"] = DateTimeOffset.UtcNow;
        row["UpdatedAtUtc"] = DateTimeOffset.UtcNow;
        table.Rows.Add(row);
        using var reader = table.CreateDataReader();
        Assert.IsTrue(reader.Read());
        var value = AiBudgetRowReaders.ReadAgentApproval(reader);
        Assert.AreEqual(id, value.Id);
        Assert.IsNull(value.TenantId);
        Assert.IsNull(value.ApproverId);
        Assert.IsNull(value.ConsumedAtUtc);
        Assert.AreEqual("pending", value.DecisionKey);
    }
}
