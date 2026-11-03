using System.Data;
using System.Security.Cryptography;
using System.Text;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Data.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Full.NET.Modules.Ai.Features;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;

namespace Full.NET.UnitTests.Ai;

/// <summary>数据库修订来源的真实字节、静态读取及批准模型绑定，不借文件协议容忍缺失文件。</summary>
[TestClass]
public sealed class AiKnowledgeFaqLifecycleTests
{
    [TestMethod]
    public async Task Faq_index_uses_exact_utf8_revision_bytes_and_readonly_stream()
    {
        var content=new AiKnowledgeFaqContent("问题", ["问法一","问法二"], "答案\n第二行", "制度", null, null);
        var value=AiKnowledgeFaqIndexContent.Create(content); const string text="Question\n问题\nAliases\n问法一\n问法二\nAnswer\n答案\n第二行\n";
        Assert.AreEqual(text,value.Text); Assert.AreEqual(Encoding.UTF8.GetByteCount(text),value.SizeBytes);
        Assert.AreEqual(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))),value.Hash);
        await using var stream=value.Open(); Assert.IsFalse(stream.CanWrite); using var reader=new StreamReader(stream,Encoding.UTF8);
        Assert.AreEqual(text,await reader.ReadToEndAsync());
        await using var parsedStream=value.Open();var parsed=await AiKnowledgeTextParser.ParseAsync(parsedStream,"faq.txt","text/plain",value.Hash,default);
        Assert.IsTrue(parsed.IsSuccess);Assert.AreEqual(value.Hash,parsed.Document!.ContentHash,"完整清单的摘要必须与生产解析器完全相同。");
    }
    [TestMethod]
    public void Faq_index_maximum_legal_unicode_is_bounded_without_truncation()
    {
        var c=AiKnowledgeFaqDraftPolicy.Freeze(new(new string('问',512),Enumerable.Range(0,16).Select(i=>new string('别',511)+(char)('A'+i)).ToArray(),new string('答',8000),new string('类',64),null,null));
        var indexed=AiKnowledgeFaqIndexContent.Create(c); Assert.IsTrue(indexed.SizeBytes<131072); Assert.IsTrue(indexed.Text.Contains(c.Answer,StringComparison.Ordinal));
        Assert.ThrowsExactly<InvalidDataException>(()=>AiKnowledgeFaqIndexContent.Create(c with{Answer=new string('答',131072)}));
        Assert.ThrowsExactly<EncoderFallbackException>(()=>AiKnowledgeFaqIndexContent.Create(c with{Answer="\ud800"}));
    }
    [TestMethod] [DataRow("id")] [DataRow("version")] [DataRow("tenant")] [DataRow("provider")] [DataRow("model")] [DataRow("endpoint")] [DataRow("organization")] [DataRow("key")]
    public void Faq_embedding_model_fingerprint_binds_every_dispatch_identity(string changed)
    {
        var id=Guid.CreateVersion7();
        AiModelConfigRecord Model(bool alternate)=>new(){Id=alternate&&changed=="id"?Guid.CreateVersion7():id,Version=alternate&&changed=="version"?2:1,
            TenantId=alternate&&changed=="tenant"?Guid.CreateVersion7():null,ProviderKey=alternate&&changed=="provider"?"other":"openai",
            ModelId=alternate&&changed=="model"?"other":"text-embedding-3-small",EndpointBaseUrl=alternate&&changed=="endpoint"?"https://other.example":"https://model.example",
            OrganizationId=alternate&&changed=="organization"?"other":"org",ApiKeyProtected=alternate&&changed=="key"?"protected-other":"protected",Name=alternate?"改名":"原名"};
        var before=AiKnowledgeFaqModelFingerprint.Create(Model(false)); Assert.AreEqual(64,before.Length);
        Assert.AreNotEqual(before,AiKnowledgeFaqModelFingerprint.Create(Model(true)));
    }
    [TestMethod] [DataRow(false)] [DataRow(true)]
    public void File_protocol_rejects_missing_files_and_faq_even_if_a_file_is_forged(bool dispatch)
    {
        var file=Guid.CreateVersion7();
        if(dispatch){Assert.AreEqual(file,AiKnowledgeFileSource.RequireFileId(new AiKnowledgeIndexDispatchRecord{FileId=file}));
            Assert.ThrowsExactly<InvalidDataException>(()=>AiKnowledgeFileSource.RequireFileId(new AiKnowledgeIndexDispatchRecord()));
            Assert.ThrowsExactly<InvalidDataException>(()=>AiKnowledgeFileSource.RequireFileId(new AiKnowledgeIndexDispatchRecord{FileId=file,FaqRevisionId=Guid.CreateVersion7()}));}
        else{Assert.AreEqual(file,AiKnowledgeFileSource.RequireFileId(new AiKnowledgeIndexStatusRecord{FileId=file}));
            Assert.ThrowsExactly<InvalidDataException>(()=>AiKnowledgeFileSource.RequireFileId(new AiKnowledgeIndexStatusRecord()));
            Assert.ThrowsExactly<InvalidDataException>(()=>AiKnowledgeFileSource.RequireFileId(new AiKnowledgeIndexStatusRecord{FileId=file,FaqRevisionId=Guid.CreateVersion7()}));}
    }
    [TestMethod] [DataRow(false)] [DataRow(true)]
    public void Faq_index_source_static_reader_keeps_complete_frozen_identity(bool mysql)
    {
        using var table=new DataTable();var id=Guid.CreateVersion7();var now=new DateTime(2026,10,29,8,0,0,DateTimeKind.Utc);
        foreach(var name in new[]{"Id","FaqId","RevisionId","ModelConfigId","RequestKey","ActorUserId"})table.Columns.Add(name,typeof(Guid));
        foreach(var name in new[]{"RevisionHash","SourceHash","DataClassification","ModelFingerprint","BodyText","BodyHash","RequestHash"})table.Columns.Add(name,typeof(string));
        foreach(var name in new[]{"KnowledgeBaseVersion","ModelConfigVersion"})table.Columns.Add(name,mysql?typeof(long):typeof(int));
        table.Columns.Add("SizeBytes",typeof(long));table.Columns.Add("CreatedAtUtc",mysql?typeof(DateTime):typeof(DateTimeOffset));var row=table.NewRow();
        foreach(DataColumn column in table.Columns)row[column]=column.DataType==typeof(Guid)?id:column.DataType==typeof(string)?"完整原值":column.DataType==typeof(DateTime)?now:column.DataType==typeof(DateTimeOffset)?new DateTimeOffset(now):7;
        table.Rows.Add(row);using var reader=table.CreateDataReader();Assert.IsTrue(reader.Read());var source=AiBudgetRowReaders.ReadKnowledgeFaqIndexSource(reader);
        Assert.AreEqual(id,source.RevisionId);Assert.AreEqual(id,source.ModelConfigId);Assert.AreEqual(id,source.RequestKey);Assert.AreEqual("完整原值",source.ModelFingerprint);Assert.AreEqual("完整原值",source.BodyText);
        Assert.AreEqual(7,source.KnowledgeBaseVersion);Assert.AreEqual(7,source.ModelConfigVersion);Assert.AreEqual(7L,source.SizeBytes);Assert.AreEqual(new DateTimeOffset(now),source.CreatedAtUtc);
    }
}

public sealed partial class AiKnowledgeIndexPdfActivationTests
{
    [TestMethod] [DataRow(false)] [DataRow(true)]
    public async Task Faq_binding_metadata_rechecks_full_permissions_after_role_lookup_without_payload_or_budget(bool revoked)
    {
        using var f=await PaidAnswerFixture.CreateAsync(false);var root=FaqRoot(f);var query=FaqQuery(f,root);var proof=new FaqDraftProof(()=>false);
        var session=new SessionBindingSnapshot(f.R.Actor,f.R.F.Tenant.Id,Guid.CreateVersion7(),"stamp","host","host",SessionBindingKinds.Refresh);
        var authorization=Substitute.For<IBackgroundSessionAuthorization>();var afterRoles=false;var authorizations=0;
        authorization.AuthorizeAllAsync(Arg.Any<SessionBindingSnapshot>(),Arg.Any<IReadOnlyCollection<string>>(),Arg.Any<CancellationToken>())
            .Returns(call=>
            {
                authorizations++;var required=call.ArgAt<IReadOnlyCollection<string>>(1);
                foreach(var permission in new[]{AiKnowledgePermissions.Read,AiKnowledgeFaqPermissions.Read,AiKnowledgeFaqPermissions.Index,
                    AiKnowledgeDocumentPermissions.Read,AiKnowledgeDocumentPermissions.SourceRead,AiKnowledgeDocumentPermissions.IndexRead})Assert.IsTrue(required.Contains(permission));
                return revoked&&afterRoles?null:new AuthorizedSessionActor(session.UserId,session.TenantId,session.SessionId);
            });
        var roles=Substitute.For<IActiveRoleDirectory>();var organizations=Substitute.For<ICurrentTenantOrganizationUnitDirectory>();
        roles.ListActiveRoleIdsForUserAsync(Arg.Any<Guid>(),Arg.Any<CancellationToken>()).Returns(_=>{afterRoles=true;return Array.Empty<Guid>();});
        organizations.ListActiveUnitIdsForUserAsync(Arg.Any<Guid>(),Arg.Any<CancellationToken>()).Returns(Array.Empty<Guid>());
        var guard=new AiKnowledgeFaqDraftSourceGuard(query,f.R.F.Transactions,f.R.F.Tenant,Options.Create(new DatabaseOptions()),
            new AiKnowledgeReadAccess(roles,organizations,f.R.F.Tenant),proof,authorization);
        Assert.AreEqual(!revoked,(await guard.AuthorizeIndexBindingsAsync(root,session,default)).Contains(root.Id));
        Assert.IsTrue(afterRoles);Assert.IsTrue(authorizations<=2,"纯绑定不应重复执行没有载荷IO的证明轮次。");
        Assert.AreEqual(0,proof.Calls);Assert.AreEqual(0,f.Budget.Requests.Count);
    }

    [TestMethod] [DataRow(false)] [DataRow(true)]
    public async Task Faq_lifecycle_stage_binding_checks_revocation_but_cannot_release_unproven_payload(bool sourceChanged)
    {
        using var f=await PaidAnswerFixture.CreateAsync(false);var root=FaqRoot(f);var query=FaqQuery(f,root);var proof=new FaqDraftProof(()=>false);
        var session=new SessionBindingSnapshot(f.R.Actor,f.R.F.Tenant.Id,Guid.CreateVersion7(),"stamp","host","host",SessionBindingKinds.Refresh);
        var authorization=Substitute.For<IBackgroundSessionAuthorization>();var denied=false;
        authorization.AuthorizeAllAsync(Arg.Any<SessionBindingSnapshot>(),Arg.Any<IReadOnlyCollection<string>>(),Arg.Any<CancellationToken>())
            .Returns(_=>denied?null:new AuthorizedSessionActor(session.UserId,session.TenantId,session.SessionId));
        var guard=new AiKnowledgeFaqDraftSourceGuard(query,f.R.F.Transactions,f.R.F.Tenant,Options.Create(new DatabaseOptions()),FaqAccess(f),proof,authorization);
        Assert.IsTrue((await guard.AuthorizeIndexBindingsAsync(root,session,default)).Contains(root.Id));Assert.AreEqual(0,proof.Calls);
        Assert.AreEqual(0,(await guard.AuthorizeAsync([root],session.UserId,AiKnowledgeFaqPermissions.Index,default,original:session)).Count);
        Assert.AreEqual(1,proof.Calls,"阶段绑定不能冒充正文/收费边界的完整载荷证明。");
        if(sourceChanged)f.R.Query.Rows=[f.R.Query.Rows[0] with{PublicationId=Guid.CreateVersion7()}];else denied=true;
        Assert.AreEqual(0,(await guard.AuthorizeIndexBindingsAsync(root,session,default)).Count);Assert.AreEqual(0,f.Budget.Requests.Count);
    }
    [TestMethod] [DataRow("public")] [DataRow("restricted")] [DataRow("unknown")]
    public async Task Faq_lifecycle_classification_serializes_readonly_full_dependencies_and_rejects_unknown(string classification)
    {
        using var f=await PaidAnswerFixture.CreateAsync(false);var source=f.R.Query.Rows[0] with{DataClassification=classification};
        var json=AiKnowledgeFaqProvenanceCodec.Encode(new(1,"document_derived",null,[AiKnowledgeFaqProvenanceCodec.Dependency(source)]));
        var root=new AiKnowledgeFaqDraftRecord{Id=Guid.CreateVersion7(),KnowledgeBaseId=source.KnowledgeBaseId,OriginKey="document_derived",SourceJson=json,SourceHash=AiKnowledgeFaqDraftPolicy.Hash(json)};
        var query=new FaqClassificationQuery(source);var roles=Substitute.For<IActiveRoleDirectory>();var organizations=Substitute.For<ICurrentTenantOrganizationUnitDirectory>();
        roles.ListActiveRoleIdsForUserAsync(Arg.Any<Guid>(),Arg.Any<CancellationToken>()).Returns(Array.Empty<Guid>());
        var input=new AiKnowledgeFaqIndexInput(query,f.R.F.Transactions,f.R.F.Tenant,Options.Create(new DatabaseOptions()),f.R.F.Clock,null!,new(roles,organizations,f.R.F.Tenant));
        Assert.AreEqual(classification=="unknown"?null:classification=="public"?"internal":"restricted",await input.ClassificationAsync(root,f.R.Actor,default));
        Assert.AreEqual(1,query.DependencyCount);Assert.AreEqual(0,f.Budget.Requests.Count);Assert.AreEqual(0,f.Factory.EmbeddingCalls);
    }
    private sealed class FaqClassificationQuery(AiKnowledgeIndexStatusRecord source):IQueryExecutor
    {
        public int DependencyCount{get;private set;}
        public Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement,object? parameters=null,CancellationToken cancellationToken=default)=>
            Task.FromResult((T?)(object)new AiKnowledgeBaseRecord{Id=source.KnowledgeBaseId,IsEnabled=true,DataClassification="internal"});
        public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement,object? parameters=null,CancellationToken cancellationToken=default)
        {
            var p=(Dictionary<string,object?>)parameters!;using var json=System.Text.Json.JsonDocument.Parse((string)p["DependenciesJson"]!);DependencyCount=json.RootElement.GetArrayLength();
            Assert.AreEqual(source.DocumentId,json.RootElement[0].GetProperty("documentId").GetGuid());return Task.FromResult<IReadOnlyList<T>>([(T)(object)source]);
        }
    }
}
