namespace Full.NET.Data.CodeGeneration.Generation;

/// <summary>
/// 将可移植生成路径约束到真实工作区，并拒绝链接和大小写别名。
/// </summary>
internal static class GenerationWorkspacePath
{
    public static string NormalizeRoot(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        var fullRoot = Path.GetFullPath(workspaceRoot);
        if (!Directory.Exists(fullRoot))
        {
            throw new DirectoryNotFoundException(
                $"生成工作区根目录不存在：{fullRoot}");
        }

        RejectReparsePoint(fullRoot, relativePath: null);
        return new DirectoryInfo(fullRoot).FullName;
    }

    public static string Resolve(
        string fullRoot,
        string relativePath)
    {
        GenerationArtifactPath.Validate(relativePath, nameof(relativePath));

        var segments = relativePath.Split('/');
        var currentPath = fullRoot;
        for (var index = 0; index < segments.Length; index++)
        {
            if (!Directory.Exists(currentPath))
            {
                currentPath = Path.Combine(
                    currentPath,
                    Path.Combine(segments[index..]));
                break;
            }

            RejectReparsePoint(currentPath, relativePath);
            var aliases = Directory
                .EnumerateFileSystemEntries(
                    currentPath,
                    "*",
                    SearchOption.TopDirectoryOnly)
                .Where(path => StringComparer.OrdinalIgnoreCase.Equals(
                    Path.GetFileName(path),
                    segments[index]))
                .ToArray();
            if (aliases.Length > 1)
            {
                throw Conflict(
                    relativePath,
                    "工作区包含大小写不唯一的路径别名。");
            }

            if (aliases.Length == 0)
            {
                currentPath = Path.Combine(
                    currentPath,
                    Path.Combine(segments[index..]));
                break;
            }

            var actualName = Path.GetFileName(aliases[0]);
            if (!StringComparer.Ordinal.Equals(actualName, segments[index]))
            {
                throw Conflict(
                    relativePath,
                    $"磁盘路径大小写与计划不一致：{actualName}");
            }

            currentPath = aliases[0];
            RejectReparsePoint(currentPath, relativePath);
            if (index < segments.Length - 1
                && !Directory.Exists(currentPath))
            {
                throw Conflict(
                    relativePath,
                    "产物父路径已被普通文件占用。");
            }
        }

        var fullPath = Path.GetFullPath(currentPath);
        EnsureContained(fullRoot, fullPath, relativePath);
        return fullPath;
    }

    public static void EnsureParentDirectory(
        string fullRoot,
        string relativePath)
    {
        GenerationArtifactPath.Validate(relativePath, nameof(relativePath));

        var parentSegments = relativePath.Split('/')[..^1];
        var currentPath = fullRoot;
        var currentRelativePath = string.Empty;
        foreach (var segment in parentSegments)
        {
            currentRelativePath = currentRelativePath.Length == 0
                ? segment
                : $"{currentRelativePath}/{segment}";
            var candidate = Resolve(fullRoot, currentRelativePath);
            if (File.Exists(candidate))
            {
                throw Conflict(
                    relativePath,
                    "产物父路径已被普通文件占用。");
            }

            if (!Directory.Exists(candidate))
            {
                Directory.CreateDirectory(candidate);
            }

            RejectReparsePoint(candidate, currentRelativePath);
            currentPath = candidate;
        }

        EnsureContained(fullRoot, currentPath, relativePath);
    }

    private static void EnsureContained(
        string fullRoot,
        string fullPath,
        string relativePath)
    {
        var rootPrefix = Path.EndsInDirectorySeparator(fullRoot)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!fullPath.StartsWith(rootPrefix, comparison))
        {
            throw Conflict(relativePath, "生成路径逃逸工作区根目录。");
        }
    }

    private static void RejectReparsePoint(
        string path,
        string? relativePath)
    {
        FileAttributes attributes;
        try
        {
            // GetAttributes 读取链接条目本身，不能先用 Exists，
            // 否则指向不存在目标的悬空链接会被误判为普通缺失路径。
            attributes = File.GetAttributes(path);
        }
        catch (FileNotFoundException)
        {
            return;
        }
        catch (DirectoryNotFoundException)
        {
            return;
        }

        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new GenerationWorkspaceConflictException(
                "生成工作区路径不得经过符号链接或 reparse point。",
                relativePath);
        }
    }

    private static GenerationWorkspaceConflictException Conflict(
        string relativePath,
        string reason)
    {
        return new GenerationWorkspaceConflictException(
            $"{reason} 路径：{relativePath}",
            relativePath);
    }
}
