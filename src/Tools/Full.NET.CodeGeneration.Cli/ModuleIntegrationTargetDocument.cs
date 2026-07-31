using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.Data.CodeGeneration.Integration;

namespace Full.NET.CodeGeneration.Cli;

/// <summary>
/// 从严格 JSON 读取模块接入目标，避免根据命名空间猜测仓库拓扑。
/// </summary>
internal sealed class ModuleIntegrationTargetDocument
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowTrailingCommas = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
    };

    public required string ModuleName { get; init; }

    public required string ModuleProjectPath { get; init; }

    public required string ModuleEntryPointPath { get; init; }

    public required string CompositionProjectPath { get; init; }

    public required string CompositionCatalogPath { get; init; }

    public required string VueRouterPath { get; init; }

    public required string LayuiRouterPath { get; init; }

    public ModuleClientRouteTargetDocument? ClientRoute { get; init; }

    public static async Task<ModuleIntegrationTarget> LoadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        if (bytes.Length >= 3
            && bytes[0] == 0xEF
            && bytes[1] == 0xBB
            && bytes[2] == 0xBF)
        {
            throw new DecoderFallbackException(
                "模块接入目标 JSON 不得包含 UTF-8 BOM。");
        }

        var document = JsonSerializer.Deserialize<
            ModuleIntegrationTargetDocument>(
                StrictUtf8.GetString(bytes),
                JsonOptions)
            ?? throw new JsonException(
                "模块接入目标 JSON 不能为空。");
        return ModuleIntegrationTarget.Create(
            document.ModuleName,
            document.ModuleProjectPath,
            document.ModuleEntryPointPath,
            document.CompositionProjectPath,
            document.CompositionCatalogPath,
            document.VueRouterPath,
            document.LayuiRouterPath,
            document.ClientRoute?.ToTarget());
    }
}

/// <summary>
/// 保存严格 JSON 中显式声明的双管理端本地路由映射。
/// </summary>
internal sealed class ModuleClientRouteTargetDocument
{
    public required string RoutePath { get; init; }

    public required string VueRouteName { get; init; }

    public required string VueComponentPath { get; init; }

    public required string LayuiControllerPath { get; init; }

    public required string LayuiControllerExport { get; init; }

    public ModuleClientRouteTarget ToTarget() =>
        ModuleClientRouteTarget.Create(
            RoutePath,
            VueRouteName,
            VueComponentPath,
            LayuiControllerPath,
            LayuiControllerExport);
}
