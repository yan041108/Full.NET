// 架构测试共享：解析仓库根路径与常用源码目录。
namespace Full.NET.ArchitectureTests;

/// <summary>架构测试共享：解析仓库根路径与常用源码目录。</summary>
internal static class ArchitectureRepositoryRoot
{
    public static string Find()
    {
        // 聚焦Linux验收复用系统临时输出，源码根必须优先采用经过解决方案标记核对的只读原工作区。
        foreach (var origin in new[] { Environment.GetEnvironmentVariable("FULLNET_TEST_REPOSITORY_ROOT"), AppContext.BaseDirectory })
        {
            if (string.IsNullOrWhiteSpace(origin)) continue;
            var directory = new DirectoryInfo(origin);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Full.NET.slnx"))
                    && Directory.Exists(Path.Combine(directory.FullName, "src", "Modules")))
                    return directory.FullName;
                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
