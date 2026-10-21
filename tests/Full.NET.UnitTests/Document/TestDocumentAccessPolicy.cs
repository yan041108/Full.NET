using Full.NET.Abstractions.Results;
using Full.NET.Modules.Document.Security;

namespace Full.NET.UnitTests.Document;

/// <summary>显式测试调用者与授权结果，避免为动态代理扩大生产内部可见性。</summary>
internal sealed class TestDocumentAccessPolicy(Error? error = null) : IDocumentAccessPolicy
{
    public Guid? ActorUserId => Guid.Empty;
    public Task<Error?> CheckAsync(Guid documentId, DocumentOperation operation, CancellationToken cancellationToken) => Task.FromResult(error);
}

internal sealed class TestDocumentActorContext : IDocumentActorContext
{
    public Guid? UserId { get; set; }
}
