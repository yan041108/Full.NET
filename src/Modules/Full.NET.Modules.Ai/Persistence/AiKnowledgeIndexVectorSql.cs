using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>持久数据仍受 AI 来源和原会话守卫；预算已结算不代替当前授权。</summary>
internal static class AiKnowledgeIndexVectorSql
{
    private const string Joins = " FROM fn_ai_knowledge_index_batch AS batch INNER JOIN fn_ai_knowledge_index_task ON fn_ai_knowledge_index_task.Id=batch.IndexTaskId INNER JOIN fn_ai_knowledge_index_session AS binding ON binding.IndexTaskId=fn_ai_knowledge_index_task.Id INNER JOIN fn_ai_operation_budget AS ledger ON ledger.Id=batch.Id";
    private const string Held = " WHERE fn_ai_knowledge_index_task.Id=@TaskId AND fn_ai_knowledge_index_task.StateKey='running' AND fn_ai_knowledge_index_task.LeaseToken=@LeaseToken AND fn_ai_knowledge_index_task.LeaseExpiresAtUtc>@Now AND batch.BatchOrdinal=@BatchOrdinal AND ledger.ScopeKey=@ScopeKey AND ledger.ModelConfigId=fn_ai_knowledge_index_task.ModelConfigId AND ledger.RequestHash=batch.RequestHash AND ledger.UsageStatus IN ('known','unknown') AND ledger.Outcome='succeeded' AND binding.ActorUserId=@OwnerUserId AND binding.SessionId=@SessionId AND binding.SessionKind=@SessionKind AND binding.SecurityStamp=@SecurityStamp AND binding.ActorScope=@ActorScope AND binding.EffectiveScope=@EffectiveScope AND EXISTS (SELECT 1 FROM fn_ai_knowledge_version AS source WHERE source.Id=fn_ai_knowledge_index_task.VersionId AND source.FileId=@FileId AND source.ContentHash=@ContentHash AND source.ContentType=@ContentType AND source.SizeBytes=@SizeBytes)";
    private const string Insert = "INSERT INTO fn_ai_knowledge_index_vector (Id,ProviderKey,ModelId,Dimensions,FormatKey,DistanceKey,PayloadHash,Payload,CreatedAtUtc) SELECT batch.Id,@ProviderKey,@ModelId,@Dimensions,@FormatKey,@DistanceKey,@PayloadHash,@Payload,@Now" + Joins + Held + " AND batch.Id=@OperationId AND batch.RequestHash=@RequestHash AND batch.InputCount=@InputCount AND ledger.ProviderKey=@ProviderKey AND ledger.ModelId=@ModelId";
    private const string Read = "SELECT vector.Id,batch.IndexTaskId,batch.BatchOrdinal,batch.RequestHash,batch.InputCount,fn_ai_knowledge_index_task.ModelConfigId,fn_ai_knowledge_index_task.ModelConfigVersion,vector.ProviderKey,vector.ModelId,vector.Dimensions,vector.FormatKey,vector.DistanceKey,vector.PayloadHash,vector.Payload,vector.CreatedAtUtc" + Joins + " INNER JOIN fn_ai_knowledge_index_vector AS vector ON vector.Id=batch.Id" + Held + " AND vector.ProviderKey=ledger.ProviderKey AND vector.ModelId=ledger.ModelId";
    private const string Host = " AND binding.TenantId IS NULL" + AiKnowledgeIndexTaskSql.OwnerExists + AiKnowledgeIndexTaskSql.Host + AiKnowledgeIndexTaskSql.Live + ")";
    private const string Tenant = " AND binding.TenantId=@TenantId" + AiKnowledgeIndexTaskSql.OwnerExists + AiKnowledgeIndexTaskSql.Tenant + AiKnowledgeIndexTaskSql.Live + ")";
    internal static readonly SqlStatement InsertHost = new("ai.knowledge_index_vector.insert_host", Insert + Host + " FOR SHARE", SqlDataScope.HostOnly);
    internal static readonly SqlStatement InsertTenant = new("ai.knowledge_index_vector.insert_tenant", Insert + Tenant + " FOR SHARE", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement FindHost = new("ai.knowledge_index_vector.find_host", Read + Host, SqlDataScope.HostOnly);
    internal static readonly SqlStatement FindTenant = new("ai.knowledge_index_vector.find_tenant", Read + Tenant, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    // 既有账本摘要使用数据库默认排序规则；SQL Server 列间比较显式采用批次的二进制规则。
    private const string SqlServerHash = "ledger.RequestHash COLLATE Latin1_General_100_BIN2=batch.RequestHash";
    internal static readonly SqlStatement InsertSqlServerHost = new("ai.knowledge_index_vector.insert_sqlserver_host", Insert.Replace("ledger.RequestHash=batch.RequestHash", SqlServerHash).Replace("INNER JOIN fn_ai_knowledge_index_task ON", "INNER JOIN fn_ai_knowledge_index_task WITH(UPDLOCK,HOLDLOCK) ON") + Host, SqlDataScope.HostOnly);
    internal static readonly SqlStatement InsertSqlServerTenant = new("ai.knowledge_index_vector.insert_sqlserver_tenant", Insert.Replace("ledger.RequestHash=batch.RequestHash", SqlServerHash).Replace("INNER JOIN fn_ai_knowledge_index_task ON", "INNER JOIN fn_ai_knowledge_index_task WITH(UPDLOCK,HOLDLOCK) ON") + Tenant, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement FindSqlServerHost = new("ai.knowledge_index_vector.find_sqlserver_host", Read.Replace("ledger.RequestHash=batch.RequestHash", SqlServerHash) + Host, SqlDataScope.HostOnly);
    internal static readonly SqlStatement FindSqlServerTenant = new("ai.knowledge_index_vector.find_sqlserver_tenant", Read.Replace("ledger.RequestHash=batch.RequestHash", SqlServerHash) + Tenant, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    // PDF 必须绑定完整清单的实际原字节和已结算操作；不能只信任应用先前读取的摘要。
    private const string PdfManifestHash = "CAST(manifest.PayloadHash AS BINARY)=CAST(@PdfManifestHash AS BINARY) AND CAST(manifest.PayloadHash AS BINARY)=CAST(UPPER(SHA2(manifest.Payload,256)) AS BINARY)";
    private const string PdfManifestSqlServerHash = "manifest.PayloadHash COLLATE Latin1_General_100_BIN2=@PdfManifestHash AND manifest.PayloadHash COLLATE Latin1_General_100_BIN2=CONVERT(varchar(64),HASHBYTES('SHA2_256',manifest.Payload),2)";
    private const string PdfHeld = " AND ledger.SettledAtUtc IS NOT NULL AND EXISTS (SELECT 1 FROM fn_ai_knowledge_text_manifest AS manifest WHERE manifest.Id=fn_ai_knowledge_index_task.Id AND CAST(manifest.FormatKey AS BINARY)=CAST('knowledge-pdf-manifest-v1' AS BINARY) AND OCTET_LENGTH(manifest.FormatKey)=25 AND OCTET_LENGTH(manifest.Payload) BETWEEN 8 AND 131072 AND " + PdfManifestHash + ")";
    private const string PdfInsert = Insert + PdfHeld + " AND CAST(@FormatKey AS BINARY)=CAST('knowledge-pdf-vector-batch-v1' AS BINARY) AND OCTET_LENGTH(@FormatKey)=29";
    private const string PdfRead = Read + PdfHeld + " AND CAST(vector.FormatKey AS BINARY)=CAST('knowledge-pdf-vector-batch-v1' AS BINARY) AND OCTET_LENGTH(vector.FormatKey)=29";
    private static string PdfSqlServer(string sql) => sql.Replace("ledger.RequestHash=batch.RequestHash", SqlServerHash)
        .Replace(PdfManifestHash, PdfManifestSqlServerHash).Replace("OCTET_LENGTH(", "DATALENGTH(")
        .Replace("DATALENGTH(@FormatKey)", "DATALENGTH(CONVERT(varchar(64),@FormatKey))")
        .Replace("CAST(manifest.FormatKey AS BINARY)=CAST('knowledge-pdf-manifest-v1' AS BINARY)", "manifest.FormatKey COLLATE Latin1_General_100_BIN2='knowledge-pdf-manifest-v1'")
        .Replace("CAST(@FormatKey AS BINARY)=CAST('knowledge-pdf-vector-batch-v1' AS BINARY)", "@FormatKey COLLATE Latin1_General_100_BIN2='knowledge-pdf-vector-batch-v1'")
        .Replace("CAST(vector.FormatKey AS BINARY)=CAST('knowledge-pdf-vector-batch-v1' AS BINARY)", "vector.FormatKey COLLATE Latin1_General_100_BIN2='knowledge-pdf-vector-batch-v1'");
    internal static readonly SqlStatement InsertPdfHost = new("ai.knowledge_index_pdf_vector.insert_host", PdfInsert + Host + " FOR SHARE", SqlDataScope.HostOnly);
    internal static readonly SqlStatement InsertPdfTenant = new("ai.knowledge_index_pdf_vector.insert_tenant", PdfInsert + Tenant + " FOR SHARE", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement InsertPdfSqlServerHost = new("ai.knowledge_index_pdf_vector.insert_sqlserver_host", PdfSqlServer(PdfInsert).Replace("INNER JOIN fn_ai_knowledge_index_task ON", "INNER JOIN fn_ai_knowledge_index_task WITH(UPDLOCK,HOLDLOCK) ON") + Host, SqlDataScope.HostOnly);
    internal static readonly SqlStatement InsertPdfSqlServerTenant = new("ai.knowledge_index_pdf_vector.insert_sqlserver_tenant", PdfSqlServer(PdfInsert).Replace("INNER JOIN fn_ai_knowledge_index_task ON", "INNER JOIN fn_ai_knowledge_index_task WITH(UPDLOCK,HOLDLOCK) ON") + Tenant, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement FindPdfHost = new("ai.knowledge_index_pdf_vector.find_host", PdfRead + Host, SqlDataScope.HostOnly);
    internal static readonly SqlStatement FindPdfTenant = new("ai.knowledge_index_pdf_vector.find_tenant", PdfRead + Tenant, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement FindPdfSqlServerHost = new("ai.knowledge_index_pdf_vector.find_sqlserver_host", PdfSqlServer(PdfRead) + Host, SqlDataScope.HostOnly);
    internal static readonly SqlStatement FindPdfSqlServerTenant = new("ai.knowledge_index_pdf_vector.find_sqlserver_tenant", PdfSqlServer(PdfRead) + Tenant, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    // 读取上限加一；原始批次数也必须相等，未保存或未结算的额外批次不能被关联过滤隐藏。
    // 在最终数据库读取点复算载荷摘要；仅改字节而保留旧摘要也必须拒绝，结果不返回载荷。
    private const string MySqlPayloadHash = "CAST(vector.PayloadHash AS BINARY)=CAST(UPPER(SHA2(vector.Payload,256)) AS BINARY)";
    private const string SqlServerPayloadHash = "vector.PayloadHash COLLATE Latin1_General_100_BIN2=CONVERT(varchar(64),HASHBYTES('SHA2_256',vector.Payload),2)";
    private static readonly string Receipts = "SELECT batch.Id,batch.BatchOrdinal,batch.RequestHash,batch.InputCount,vector.PayloadHash,vector.ProviderKey,vector.ModelId,vector.Dimensions,vector.FormatKey,vector.DistanceKey" + Joins
        + " INNER JOIN fn_ai_knowledge_index_vector AS vector ON vector.Id=batch.Id" + Held.Replace(" AND batch.BatchOrdinal=@BatchOrdinal", string.Empty)
        + " AND vector.ProviderKey=ledger.ProviderKey AND vector.ModelId=ledger.ModelId"
        + " AND " + MySqlPayloadHash
        + " AND (SELECT COUNT(*) FROM fn_ai_knowledge_index_batch AS all_batches WHERE all_batches.IndexTaskId=fn_ai_knowledge_index_task.Id)=@ExpectedBatchCount";
    internal static readonly SqlStatement ReceiptsHost = new("ai.knowledge_index_vector.receipts_host", Receipts + Host + " ORDER BY batch.BatchOrdinal LIMIT 2049", SqlDataScope.HostOnly);
    internal static readonly SqlStatement ReceiptsTenant = new("ai.knowledge_index_vector.receipts_tenant", Receipts + Tenant + " ORDER BY batch.BatchOrdinal LIMIT 2049", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement ReceiptsSqlServerHost = new("ai.knowledge_index_vector.receipts_sqlserver_host", Receipts.Replace("SELECT batch.Id,", "SELECT TOP (2049) batch.Id,").Replace("ledger.RequestHash=batch.RequestHash", SqlServerHash).Replace(MySqlPayloadHash, SqlServerPayloadHash) + Host + " ORDER BY batch.BatchOrdinal", SqlDataScope.HostOnly);
    internal static readonly SqlStatement ReceiptsSqlServerTenant = new("ai.knowledge_index_vector.receipts_sqlserver_tenant", Receipts.Replace("SELECT batch.Id,", "SELECT TOP (2049) batch.Id,").Replace("ledger.RequestHash=batch.RequestHash", SqlServerHash).Replace(MySqlPayloadHash, SqlServerPayloadHash) + Tenant + " ORDER BY batch.BatchOrdinal", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    // PDF 全量回执追加完整清单及成功结算约束，复用原始批次数守卫与实际载荷 SHA 检查。
    private static readonly string PdfReceipts = Receipts + PdfHeld
        + " AND CAST(vector.FormatKey AS BINARY)=CAST('knowledge-pdf-vector-batch-v1' AS BINARY) AND OCTET_LENGTH(vector.FormatKey)=29";
    private static readonly string PdfSqlServerReceipts = PdfSqlServer(PdfReceipts)
        .Replace("SELECT batch.Id,", "SELECT TOP (2049) batch.Id,").Replace(MySqlPayloadHash, SqlServerPayloadHash);
    internal static readonly SqlStatement ReceiptsPdfHost = new("ai.knowledge_index_pdf_vector.receipts_host", PdfReceipts + Host + " ORDER BY batch.BatchOrdinal LIMIT 2049", SqlDataScope.HostOnly);
    internal static readonly SqlStatement ReceiptsPdfTenant = new("ai.knowledge_index_pdf_vector.receipts_tenant", PdfReceipts + Tenant + " ORDER BY batch.BatchOrdinal LIMIT 2049", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement ReceiptsPdfSqlServerHost = new("ai.knowledge_index_pdf_vector.receipts_sqlserver_host", PdfSqlServerReceipts + Host + " ORDER BY batch.BatchOrdinal", SqlDataScope.HostOnly);
    internal static readonly SqlStatement ReceiptsPdfSqlServerTenant = new("ai.knowledge_index_pdf_vector.receipts_sqlserver_tenant", PdfSqlServerReceipts + Tenant + " ORDER BY batch.BatchOrdinal", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
}
