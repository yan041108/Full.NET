using System.Data;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Features.ManageKnowledgeOperations;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>维护元数据不提升正文权限、处理或发布资格；分页和最后授权独立核证。</summary>
[TestClass]
public sealed class AiKnowledgeOperationsTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-30T04:00:00Z");
    private static AiKnowledgeOperationRecord Row() => new() { KnowledgeBaseId=Guid.CreateVersion7(),DocumentId=Guid.CreateVersion7(),VersionId=Guid.CreateVersion7(),VersionNumber=1,
        SourceKind="document-v1",PreparationState="completed",TaskId=Guid.CreateVersion7(),TaskState="completed",AttemptCount=1,CreatedAtUtc=Now.AddDays(-1),HasActiveGeneration=1 };

    [TestMethod]
    public void Completed_activation_does_not_imply_publication_or_source_eligibility()
    {
        var value=AiKnowledgeOperationsService.Project(Row(),Now)!;
        Assert.AreEqual("completed",value.TaskState);Assert.IsTrue(value.HasActiveGeneration);Assert.AreEqual("none",value.PublicationState);Assert.IsNull(value.PublicationId);
    }
    [TestMethod] [DataRow(-1,"expired_record")] [DataRow(0,"expired_record")] [DataRow(1,"published_record")]
    public void Publication_period_is_distinct_from_processing_and_review(int days,string expected)
    {
        var row=Row() with { PublicationId=Guid.CreateVersion7(),PublicationVersionId=Guid.CreateVersion7(),PublicationState="published",EffectiveFromUtc=Now.AddDays(-2),EffectiveToUtc=Now.AddDays(days),ReviewState="pending" };
        var value=AiKnowledgeOperationsService.Project(row,Now)!;Assert.AreEqual(expected,value.PublicationState);Assert.AreEqual("pending",value.ReviewState);
        Assert.AreNotEqual(value.VersionId,value.PublicationVersionId);
    }
    [TestMethod] [DataRow(-1,"lease_expired")] [DataRow(1,"processing")]
    public void Running_lease_is_observed_without_automatically_retrying(int minutes,string expected)
    {Assert.AreEqual(expected,AiKnowledgeOperationsService.Project(Row() with {TaskState="running",TaskLeaseExpiresAtUtc=Now.AddMinutes(minutes)},Now)!.TaskState);}
    [TestMethod]
    public void Unknown_state_is_not_reported_successful_and_private_payload_has_no_projection()
    {
        var value=AiKnowledgeOperationsService.Project(Row() with {TaskState="unexpected",PreparationState="unexpected"},Now)!;
        Assert.AreEqual("unknown",value.TaskState);Assert.AreEqual("unknown",value.PreparationState);Assert.IsNull(value.FailureCode);
        var names=value.GetType().GetProperties().Select(p=>p.Name).ToArray();Assert.IsFalse(names.Any(n=>n is "Answer" or "Question" or "OriginalFileName" or "SourceJson" or "SnapshotJson" or "ProgressPercent" or "ModelLatencyMs"));
    }
    [TestMethod] [DataRow("awaiting_upload")] [DataRow("file_uploaded")] [DataRow("pending_confirmation")]
    public void Preparation_records_preserve_their_actual_stage(string stage)
    {Assert.AreEqual(stage,AiKnowledgeOperationsService.Project(Row() with {PreparationState=stage},Now)!.PreparationState);}
    [TestMethod]
    public void Missing_preparation_is_not_fabricated_from_completed_task()
    {Assert.AreEqual("not_recorded",AiKnowledgeOperationsService.Project(Row() with {PreparationState=null},Now)!.PreparationState);}
    [TestMethod]
    public void Faq_identity_is_closed_and_unknown_or_mixed_identity_is_rejected()
    {
        var row=Row();Assert.IsNull(AiKnowledgeOperationsService.Project(row with {FaqId=Guid.CreateVersion7()},Now));
        Assert.IsNull(AiKnowledgeOperationsService.Project(row with {SourceKind="faq-revision-v1"},Now));
        Assert.IsNull(AiKnowledgeOperationsService.Project(row with {SourceKind="other"},Now));
        Assert.IsNotNull(AiKnowledgeOperationsService.Project(row with {SourceKind="faq-revision-v1",FaqId=Guid.CreateVersion7()},Now));
    }
    [TestMethod] [DataRow(false)] [DataRow(true)]
    public async Task Revoked_permission_or_changed_session_drops_previously_read_metadata(bool changeSession)
    {
        var f=new Fixture(false);var calls=0;f.Authorization.AuthorizeAllAsync(Arg.Any<IReadOnlyCollection<string>>(),Arg.Any<CancellationToken>()).Returns(_=>++calls==1?f.Actor:changeSession?f.Actor with {SessionId=Guid.CreateVersion7()}:null);
        var result=await f.Read();Assert.IsFalse(result.IsSuccess);Assert.AreEqual(1,f.Reads);
    }
    [TestMethod]
    public async Task Ownership_changes_while_authorizing_are_observed_by_final_query()
    {
        var f=new Fixture(false);f.AfterRead=()=>{if(f.Reads==1)f.Rows=[];};var result=await f.Read();Assert.IsTrue(result.IsSuccess);Assert.AreEqual(0,result.Value!.Items.Count);Assert.AreEqual(2,f.Reads);
    }
    [TestMethod] [DataRow(false,DatabaseProvider.SqlServer)] [DataRow(true,DatabaseProvider.SqlServer)] [DataRow(false,DatabaseProvider.MySql)] [DataRow(true,DatabaseProvider.MySql)]
    public async Task Page_retains_scope_and_uses_last_delivered_identity_for_continuation(bool tenant,DatabaseProvider provider)
    {
        var f=new Fixture(tenant,provider);f.Rows=Enumerable.Range(0,26).Select(_=>Row()).ToArray();var result=await f.Read();Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(25,result.Value!.Items.Count);Assert.AreEqual(f.Rows[24].VersionId,result.Value.NextAfterId);Assert.AreEqual(2,f.Reads);
    }
    [TestMethod] [DataRow("unknown")] [DataRow("ALL")] [DataRow("")]
    public async Task Invalid_filters_do_not_read_database(string view)
    {var f=new Fixture(false);Assert.IsFalse((await f.Service.ReadAsync(f.Actor.UserId,null,null,view,null,null,default)).IsSuccess);Assert.AreEqual(0,f.Reads);}
    [TestMethod]
    public async Task Invalid_or_overflowing_time_window_is_rejected_without_reading()
    {
        var f=new Fixture(false);Assert.IsFalse((await f.Service.ReadAsync(f.Actor.UserId,null,null,null,null,DateTimeOffset.MinValue,default)).IsSuccess);
        Assert.IsFalse((await f.Service.ReadAsync(f.Actor.UserId,null,null,null,Now.AddDays(-32),Now,default)).IsSuccess);Assert.AreEqual(0,f.Reads);
    }
    [TestMethod] [DataRow(false)] [DataRow(true)]
    public void Static_row_reader_preserves_nulls_and_both_provider_timestamp_types(bool mysql)
    {
        var row=Row();var values=new Dictionary<string,object?>{["KnowledgeBaseId"]=row.KnowledgeBaseId,["DocumentId"]=row.DocumentId,["VersionId"]=row.VersionId,["VersionNumber"]=mysql?(object)1L:1,
            ["FaqId"]=null,["SourceKind"]="document-v1",["PreparationState"]="completed",["TaskId"]=null,["TaskState"]=null,["AttemptCount"]=mysql?(object)0L:0,
            ["CreatedAtUtc"]=mysql?(object)Now.UtcDateTime:Now,["TaskUpdatedAtUtc"]=null,["TaskLeaseExpiresAtUtc"]=null,["HasActiveGeneration"]=mysql?(object)0L:0,
            ["ReviewState"]=null,["PublicationId"]=null,["PublicationVersionId"]=null,["PublicationState"]=null,["EffectiveFromUtc"]=null,["EffectiveToUtc"]=null};
        using var table=new DataTable();foreach(var (key,value) in values)table.Columns.Add(key,value?.GetType()??typeof(object));table.Rows.Add(values.Values.Select(v=>v??DBNull.Value).ToArray());
        using var reader=table.CreateDataReader();Assert.IsTrue(reader.Read());var actual=AiBudgetRowReaders.ReadKnowledgeOperation(reader);
        Assert.AreEqual(Now,actual.CreatedAtUtc);Assert.IsNull(actual.TaskUpdatedAtUtc);Assert.IsNull(actual.TaskLeaseExpiresAtUtc);Assert.IsNull(actual.FaqId);
    }
    private sealed class Fixture : IQueryExecutor
    {
        internal readonly ICurrentSessionAuthorization Authorization=Substitute.For<ICurrentSessionAuthorization>();
        internal readonly AuthorizedSessionActor Actor;
        internal readonly AiKnowledgeOperationsService Service;
        internal IReadOnlyList<AiKnowledgeOperationRecord> Rows=[Row()];internal int Reads;internal Action? AfterRead;
        internal Fixture(bool scoped,DatabaseProvider provider=DatabaseProvider.SqlServer)
        {
            var tenant=Substitute.For<ICurrentTenant>();Guid? scope=scoped?Guid.CreateVersion7():null;tenant.Id.Returns(scope);tenant.IsHost.Returns(!scoped);
            Actor=new(Guid.CreateVersion7(),scope,Guid.CreateVersion7());Authorization.AuthorizeAllAsync(Arg.Any<IReadOnlyCollection<string>>(),Arg.Any<CancellationToken>()).Returns(Actor);
            var clock=Substitute.For<IClock>();clock.UtcNow.Returns(Now);Service=new(this,Substitute.For<IDataTransactionState>(),tenant,Authorization,clock,Options.Create(new DatabaseOptions{Provider=provider}));
        }
        // 泛型内部行类型使用显式执行器夹具，避免动态代理的默认返回值扩大生产可见性。
        public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement sql,object? parameters=null,CancellationToken cancellationToken=default)
        {Reads++;Assert.AreEqual(Actor.TenantId is null?SqlDataScope.HostOnly:SqlDataScope.TenantRequired,sql.Scope);Assert.AreEqual(Actor.UserId,((Dictionary<string,object?>)parameters!)["ActorUserId"]);Assert.AreEqual(typeof(AiKnowledgeOperationRecord),typeof(T));var rows=Rows;AfterRead?.Invoke();return Task.FromResult((IReadOnlyList<T>)(object)rows);}
        public Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement,object? parameters=null,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
        internal Task<Full.NET.Abstractions.Results.Result<Full.NET.Modules.Ai.Contracts.AiKnowledgeOperationsPage>> Read()=>Service.ReadAsync(Actor.UserId,null,null,null,null,null,default);
    }
}
