using Full.NET.Modules.Platform.Features.ManageBackupExecutor;

namespace Full.NET.UnitTests.Platform;

/// <summary>授权备份产物路径与文件名安全校验测试。</summary>
[TestClass]
public sealed class BackupRunArtifactServiceTests
{
    [TestMethod]
    public void IsSafeArtifactFileName_rejects_path_segments_and_traversal()
    {
        Assert.IsFalse(BackupRunArtifactService.IsSafeArtifactFileName(string.Empty));
        Assert.IsFalse(BackupRunArtifactService.IsSafeArtifactFileName("../dump.bak"));
        Assert.IsFalse(BackupRunArtifactService.IsSafeArtifactFileName("nested/dump.bak"));
        Assert.IsFalse(BackupRunArtifactService.IsSafeArtifactFileName("nested\\dump.bak"));
        Assert.IsTrue(BackupRunArtifactService.IsSafeArtifactFileName("fullnet-2026-09-06.bak"));
    }
}
