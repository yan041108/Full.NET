using System.Text;
using Full.NET.Data.CodeGeneration.Integration;
using Full.NET.Data.CodeGeneration.Schema;

namespace Full.NET.CodeGeneration.Cli;

/// <summary>
/// 只读取显式接入目标并把文件系统状态交给纯规划器。
/// </summary>
internal static class ModuleIntegrationPlanCommand
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static async Task<ModuleIntegrationPlan> PlanAsync(
        string repositoryRoot,
        FullNetCrudSchema schema,
        ModuleIntegrationTarget target,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(target);

        var root = Path.GetFullPath(repositoryRoot);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException();
        }

        var paths = new List<string>
        {
            target.ModuleProjectPath,
            target.ModuleEntryPointPath,
            target.CompositionProjectPath,
            target.CompositionCatalogPath,
            target.VueRouterPath,
            target.LayuiRouterPath,
        };
        if (target.ClientRoute is not null)
        {
            paths.Add(target.ClientRoute.VueComponentPath);
            paths.Add(target.ClientRoute.LayuiControllerPath);
        }
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var relativePath in paths)
        {
            var fullPath = Path.Combine(
                root,
                relativePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
            {
                continue;
            }

            var bytes = await File.ReadAllBytesAsync(
                fullPath,
                cancellationToken);
            if (bytes.Length >= 3
                && bytes[0] == 0xEF
                && bytes[1] == 0xBB
                && bytes[2] == 0xBF)
            {
                throw new DecoderFallbackException(
                    "模块接入目标文件不得包含 UTF-8 BOM。");
            }

            files.Add(relativePath, StrictUtf8.GetString(bytes));
        }

        return ModuleIntegrationPlanner.Plan(
            schema,
            target,
            new ModuleIntegrationSnapshot(files));
    }
}

/// <summary>
/// 保存模块接入只读命令的三个显式输入。
/// </summary>
internal sealed record ModuleIntegrationCliOptions(
    string SchemaPath,
    string RepositoryPath,
    string TargetPath,
    ModuleIntegrationCliMode Mode);

/// <summary>
/// 区分只读规划、临时编译验证、显式后端写盘与手写入口接线。
/// </summary>
internal enum ModuleIntegrationCliMode
{
    Plan = 1,
    ValidateCompilation = 2,
    ApplyBackend = 3,
    ApplyModuleEntry = 4,
    ApplyComposition = 5,
    ApplyClientRoutes = 6,
}
