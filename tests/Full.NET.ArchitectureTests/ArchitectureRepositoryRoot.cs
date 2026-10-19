// 架构测试共享：解析仓库根路径与常用源码目录。
namespace Full.NET.ArchitectureTests;

/// <summary>架构测试共享：解析仓库根路径与常用源码目录。</summary>
internal static class ArchitectureRepositoryRoot
{
    public static string Find()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Full.NET.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
