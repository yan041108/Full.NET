using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document.Configuration;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Document.Features.ManageHostDocumentItems;
using Full.NET.Modules.Document.Features.ManageHostDocumentPreviewTasks;
using Full.NET.Modules.Document.Features.QueryHostRecycleBin;
using Full.NET.Modules.Document.Persistence;
using Full.NET.Modules.Files.Contracts;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Document;

/// <summary>用可控事务交错验证主行/子行次序、版本不变量和预览入库前复核。</summary>
[TestClass]
public sealed class DocumentLifecycleConcurrencyTests
{
    [TestMethod]
    public async Task Purge_locks_parent_before_deleting_dependents()
    {
        var f = new Fixture();
        Assert.IsTrue((await f.Recycle().PurgeAsync(f.Id)).IsSuccess);
        Assert.IsTrue(f.ChildWritesGuarded);
    }

    [TestMethod]
    public async Task Concurrent_version_additions_allocate_distinct_numbers()
    {
        var f = new Fixture { Race = "add" };
        var values = await Task.WhenAll(f.Items().AddVersionAsync(f.Id, Guid.CreateVersion7(), new(f.File)),
            f.Items().AddVersionAsync(f.Id, Guid.CreateVersion7(), new(f.File)));
        Assert.IsTrue(values.All(v => v.IsSuccess));
        CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, f.Versions.Values.Order().ToArray());
    }

    [TestMethod]
    public async Task Concurrent_retention_deletes_keep_minimum_versions()
    {
        var f = new Fixture { Race = "retention" };
        var ids = f.Versions.Keys.Take(2).ToArray();
        var values = await Task.WhenAll(f.Deletion().TryDeleteVersionForRetentionAsync(f.Id, ids[0]),
            f.Deletion().TryDeleteVersionForRetentionAsync(f.Id, ids[1]));
        Assert.AreEqual(1, values.Count(v => v));
        Assert.AreEqual(2, f.Versions.Count);
    }

    [TestMethod]
    public async Task Version_rollback_reads_target_inside_parent_lock()
    {
        var f = new Fixture();
        Assert.IsTrue((await f.Items().RollbackVersionAsync(f.Id, f.Versions.Keys.First(), Guid.CreateVersion7(), new(1))).IsSuccess);
        Assert.AreEqual(0, f.UnguardedVersionReads);
    }

    [TestMethod]
    [DataRow("removed")]
    [DataRow("source-changed")]
    public async Task Preview_rechecks_document_after_external_probe(string change)
    {
        var f = new Fixture { ProbeChange = change };
        var result = await f.Preview().CreateAsync(new(f.Id, null), Guid.CreateVersion7());
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(0, f.Writes);
        Assert.IsFalse(f.ProbeInsideTransaction);
    }

    [TestMethod]
    public async Task Missing_preview_reload_rolls_back_insert()
    {
        var f = new Fixture { MissingPreviewReload = true };
        Assert.IsFalse((await f.Preview().CreateAsync(new(f.Id, null), Guid.CreateVersion7())).IsSuccess);
        Assert.AreEqual(0, f.Writes);
        Assert.IsFalse(f.ProbeInsideTransaction);
    }

    private sealed class Fixture : IQueryExecutor, ICommandExecutor, ICommandTransaction
    {
        public Guid Id { get; } = Guid.CreateVersion7();
        public Guid File { get; private set; } = Guid.CreateVersion7();
        public Dictionary<Guid, int> Versions { get; } = new();
        public string? Race { get; init; }
        public string? ProbeChange { get; init; }
        public bool MissingPreviewReload { get; init; }
        public bool ChildWritesGuarded { get; private set; } = true;
        public bool ProbeInsideTransaction { get; private set; }
        public int UnguardedVersionReads { get; private set; }
        public int Writes { get; private set; }
        private readonly object sync = new();
        private readonly AsyncLocal<Frame?> frame = new();
        private readonly SemaphoreSlim parent = new(1, 1);
        private readonly TaskCompletionSource barrier = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int readers;
        private bool removed;
        private long version = 1;
        private Guid current;
        private readonly IClock clock = Substitute.For<IClock>();
        private readonly IIdGenerator ids = Substitute.For<IIdGenerator>();
        private readonly IHostFileReferenceClaimService claims = Substitute.For<IHostFileReferenceClaimService>();

        public Fixture()
        {
            for(var i=1;i<=3;i++) Versions.Add(Guid.CreateVersion7(),i);
            current = Versions.Keys.Last();
            ids.NewId().Returns(_=>Guid.CreateVersion7());
            clock.UtcNow.Returns(DateTimeOffset.UtcNow);
            claims.ClaimAsync(Arg.Any<HostFileReferenceClaimRequest>(),Arg.Any<CancellationToken>()).Returns(_=>Task.FromResult(Result<HostFileReferenceClaimResult>.Success(new(Guid.CreateVersion7(),HostFileReferenceClaimStates.Pending,new(File,8,"hash")))));
            claims.ConfirmAsync(Arg.Any<string>(),Arg.Any<CancellationToken>()).Returns(_=>Task.FromResult(Result<HostFileReferenceClaimResult>.Success(new(Guid.CreateVersion7(),HostFileReferenceClaimStates.Active,new(File,8,"hash")))));
            claims.ReleaseAsync(Arg.Any<string>(),Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result<bool>.Success(true)));
        }

        private DocumentItemTagAssignmentService Tags() => new(this,this,new(this,Options.Create(new DatabaseOptions())));
        private DocumentItemWriteGuard ItemWrites() => new(this,Options.Create(new DatabaseOptions { Provider=DatabaseProvider.SqlServer }));
        public HostDocumentItemManagementService Items() => new(this,this,this,claims,null!,null!,Tags(),clock,ids,null!,null!,null!,ItemWrites());
        public HostRecycleBinManagementService Recycle() => new(this,this,this,null!,null!,ItemWrites());
        public DocumentVersionDeletionService Deletion()
        {
            var options=Substitute.For<IOptionsMonitor<DocumentVersionRetentionOptions>>();
            options.CurrentValue.Returns(new DocumentVersionRetentionOptions { MinimumRetainedVersionsPerItem=2 });
            return new(this,this,this,claims,options,clock,ids,ItemWrites());
        }
        public HostDocumentPreviewTaskManagementService Preview()
        {
            var reader=Substitute.For<IHostFileContentReader>();
            reader.OpenReadyContentAsync(Arg.Any<Guid>(),Arg.Any<CancellationToken>()).Returns(_=>
            {
                ProbeInsideTransaction=frame.Value is not null;
                if(ProbeChange=="removed")removed=true;
                if(ProbeChange=="source-changed")File=Guid.CreateVersion7();
                return Task.FromResult(Result<HostFileContent>.Success(new(new MemoryStream([1]),"application/vnd.openxmlformats-officedocument.wordprocessingml.document","sample.docx")));
            });
            var options=Substitute.For<IOptionsMonitor<DocumentOfficePreviewConversionOptions>>();
            options.CurrentValue.Returns(new DocumentOfficePreviewConversionOptions());
            return new(this,this,reader,clock,ids,options,this,ItemWrites());
        }

        public async Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement,object? parameters=null,CancellationToken cancellationToken=default)
        {
            object? result;
            if(statement.Name.StartsWith("document.host_item.lock_host.",StringComparison.Ordinal))
            {
                // 软删除仍保留主行：锁取得成功后必须再次检查活动状态。
                await parent.WaitAsync(cancellationToken);frame.Value!.Locked=true;result=1;
            }
            else if(typeof(T)==typeof(int))
            {
                lock(sync)result=statement==DocumentItemSql.NextVersionNumber?Versions.Values.Max()+1:Versions.Count;
                if((Race=="add"&&statement==DocumentItemSql.NextVersionNumber)||(Race=="retention"&&statement==DocumentVersionRetentionSql.CountVersionsByItemId))
                    if(frame.Value?.Locked!=true){if(Interlocked.Increment(ref readers)==2)barrier.TrySetResult();await barrier.Task.WaitAsync(TimeSpan.FromSeconds(5),cancellationToken);}
            }
            else if(typeof(T)==typeof(DocumentItemDetailRecord))result=removed?null:new DocumentItemDetailRecord{Id=Id,Title="item",CurrentVersionId=current,Version=version,FileId=File,VersionId=current,VersionNumber=Versions[current],ContentHash="hash",SizeBytes=8,VersionCreatedAtUtc=clock.UtcNow,UploadedByUserId=Id};
            else if(typeof(T)==typeof(DocumentVersionRecord))
            {
                if(frame.Value?.Locked!=true)UnguardedVersionReads++;
                var p=(Dictionary<string,object?>)parameters!;var key=(Guid)p["VersionId"]!;
                result=Versions.TryGetValue(key,out var n)?new DocumentVersionRecord{Id=key,DocumentItemId=Id,FileId=File,VersionNumber=n,ContentHash="hash",SizeBytes=8}:null;
            }
            else result=typeof(T)==typeof(DocumentPreviewTaskRecord)&&!MissingPreviewReload?new DocumentPreviewTaskRecord{Id=Id,DocumentItemId=Id,SourceFileId=File,Version=1}:null;
            return result is null?default:(T)(object)result;
        }

        public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement,object? parameters=null,CancellationToken cancellationToken=default)=>Task.FromResult<IReadOnlyList<T>>([]);
        public Task<int> ExecuteAsync(SqlStatement statement,object? parameters=null,CancellationToken cancellationToken=default)
        {
            lock(sync)
            {
                Writes++;
                if(statement==DocumentItemSql.PurgeDependents)ChildWritesGuarded=frame.Value?.Locked==true;
                if(statement==DocumentItemSql.InsertVersion){var p=(Dictionary<string,object?>)parameters!;var number=(int)p["VersionNumber"]!;if(Versions.ContainsValue(number))throw new InvalidOperationException("Duplicate version number.");Versions.Add((Guid)p["Id"]!,number);}
                if(statement==DocumentItemSql.SetCurrentVersion){var p=(Dictionary<string,object?>)parameters!;current=(Guid)p["CurrentVersionId"]!;version++;}
                if(statement==DocumentVersionRetentionSql.DeleteVersionById){var p=(Dictionary<string,object?>)parameters!;Versions.Remove((Guid)p["VersionId"]!);}
                return Task.FromResult(1);
            }
        }
        public async Task<T> ExecuteAsync<T>(Func<CancellationToken,Task<T>> action,CancellationToken cancellationToken=default)
        {
            var value=new Frame();frame.Value=value;try{return await action(cancellationToken);}finally{if(value.Locked)parent.Release();frame.Value=null;}
        }
        public async Task<Result<T>> ExecuteResultAsync<T>(Func<CancellationToken,Task<Result<T>>> action,CancellationToken cancellationToken=default)
        {
            var before=Writes;var result=await ExecuteAsync(action,cancellationToken);if(!result.IsSuccess)Writes=before;return result;
        }
        private sealed class Frame { public bool Locked { get; set; } }
    }
}
