using Full.NET.Data.CodeGeneration.Generation;

namespace Full.NET.UnitTests.CodeGeneration;

/// <summary>恢复协调不能更换仍被旧执行者持有的锁文件，也不能自动重置损坏的工作区身份。</summary>
[TestClass]
public sealed class GenerationWorkspaceLeaseTests
{
    [TestMethod]
    public async Task Missing_identity_with_bound_plans_is_not_replaced_by_a_new_workspace()
    {
        var root = Directory.CreateTempSubdirectory("fullnet-codegen-missing-identity-").FullName;
        try
        {
            var lease = await GenerationWorkspaceLease.TryAcquireAsync(root) ?? throw new InvalidOperationException();
            var id = lease.WorkspaceId;
            await GenerationWorkspaceMetadata.WriteAsync(root, ".fullnet/codegeneration-plans/evidence.json", "bound plan evidence");
            await lease.DisposeAsync();
            GenerationWorkspaceMetadata.Delete(root, ".fullnet/codegeneration-workspace.id");
            await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(() => GenerationWorkspaceLease.TryAcquireAsync(root));
            Assert.IsNull(await GenerationWorkspaceMetadata.ReadAsync(root, ".fullnet/codegeneration-workspace.id"));
            Assert.AreEqual("bound plan evidence", await GenerationWorkspaceMetadata.ReadAsync(root, ".fullnet/codegeneration-plans/evidence.json"));
            await GenerationWorkspaceMetadata.WriteAsync(root, ".fullnet/codegeneration-workspace.id", id.ToString("D") + "\n");
            await using var restored = await GenerationWorkspaceLease.TryAcquireAsync(root);
            Assert.AreEqual(id, restored!.WorkspaceId);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public async Task Metadata_cannot_replace_operation_lock_or_product_manifest()
    {
        var root = Directory.CreateTempSubdirectory("fullnet-codegen-lease-").FullName;
        try
        {
            await using var lease = await GenerationWorkspaceLease.TryAcquireAsync(root);
            Assert.IsNotNull(lease);
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => GenerationWorkspaceMetadata.WriteAsync(root,
                ".fullnet/codegeneration-operation.lock", "replace"));
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => GenerationWorkspaceMetadata.WriteAsync(root,
                GenerationWorkspaceStore.ManifestRelativePath, "replace"));
            Assert.IsNull(await GenerationWorkspaceLease.TryAcquireAsync(root));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public async Task Damaged_identity_is_preserved_and_does_not_strand_the_operation_lock()
    {
        var root = Directory.CreateTempSubdirectory("fullnet-codegen-identity-").FullName;
        try
        {
            var lease = await GenerationWorkspaceLease.TryAcquireAsync(root);
            Assert.IsNotNull(lease);
            await lease.DisposeAsync();
            await GenerationWorkspaceMetadata.WriteAsync(root, ".fullnet/codegeneration-workspace.id", "damaged");
            await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(() => GenerationWorkspaceLease.TryAcquireAsync(root));
            Assert.AreEqual("damaged", await GenerationWorkspaceMetadata.ReadAsync(root, ".fullnet/codegeneration-workspace.id"));
            await GenerationWorkspaceMetadata.WriteAsync(root, ".fullnet/codegeneration-workspace.id", lease.WorkspaceId.ToString("D") + "\n");
            await using var restored = await GenerationWorkspaceLease.TryAcquireAsync(root);
            Assert.IsNotNull(restored);
            Assert.AreEqual(lease.WorkspaceId, restored.WorkspaceId);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
