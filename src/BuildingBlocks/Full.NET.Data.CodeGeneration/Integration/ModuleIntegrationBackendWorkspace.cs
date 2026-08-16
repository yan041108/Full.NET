using Full.NET.Data.CodeGeneration.Generation;
using Full.NET.Data.CodeGeneration.Schema;

namespace Full.NET.Data.CodeGeneration.Integration;

/// <summary>
/// 把实体后端产物映射到模块目录，并维护模块唯一的生成特性聚合注册桥。
/// </summary>
public static class ModuleIntegrationBackendWorkspace
{
    public const string RegistryRelativePath =
        "Generated/FullNetGeneratedModuleFeatures.g.cs";

    /// <summary>
    /// 创建只包含模块后端源码的实体级产物，避免把客户端和草案写入模块项目。
    /// </summary>
    public static IReadOnlyList<GeneratedArtifact> CreateArtifacts(
        FullNetCrudSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        var prefix = EntityPrefix(schema);
        return Array.AsReadOnly(
            CrudArtifactGenerator
                .Generate(schema)
                .Where(artifact =>
                    artifact.Kind == GeneratedArtifactKind.Backend
                    && artifact.RelativePath.EndsWith(
                        ".g.cs",
                        StringComparison.Ordinal))
                .Select(artifact => new GeneratedArtifact(
                    prefix + Path.GetFileName(artifact.RelativePath),
                    artifact.Kind,
                    artifact.Content))
                .OrderBy(
                    artifact => artifact.RelativePath,
                    StringComparer.Ordinal)
                .ToArray());
    }

    /// <summary>
    /// 为模块内全部生成实体创建唯一聚合注册桥，调用顺序不依赖导入先后。
    /// </summary>
    public static GeneratedArtifact CreateRegistryArtifact(
        FullNetCrudSchema schema,
        IEnumerable<string> entityClrTypeNames)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(entityClrTypeNames);

        var entityNames = entityClrTypeNames
            .Select(name =>
            {
                if (string.IsNullOrWhiteSpace(name)
                    || !IsIdentifier(name))
                {
                    throw new ArgumentException(
                        "聚合注册桥中的实体 CLR 名称必须是有效标识符。",
                        nameof(entityClrTypeNames));
                }

                return name;
            })
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (entityNames.Length == 0)
        {
            throw new ArgumentException(
                "聚合注册桥至少需要一个实体。",
                nameof(entityClrTypeNames));
        }

        return new GeneratedArtifact(
            RegistryRelativePath,
            GeneratedArtifactKind.Backend,
            GenerateRegistry(schema.RootNamespace, entityNames));
    }

    /// <summary>
    /// 规划当前实体和模块聚合桥，并拒绝静默接管被修改或越界的既有产物。
    /// </summary>
    public static async Task<GenerationWritePlan> PlanAsync(
        string moduleRoot,
        FullNetCrudSchema schema,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(schema);

        var currentArtifacts = CreateArtifacts(schema);
        var currentRegistry = CreateRegistryArtifact(
            schema,
            [schema.ClrTypeName]);
        var snapshot = await GenerationWorkspaceStore.CaptureAsync(
            moduleRoot,
            [.. currentArtifacts, currentRegistry],
            cancellationToken);
        if (snapshot.PreviousManifest is null)
        {
            return GenerationWritePlanner.Plan(
                [.. currentArtifacts, currentRegistry],
                snapshot.ExistingFiles);
        }

        var currentPrefix = EntityPrefix(schema);
        var allArtifacts = currentArtifacts.ToList();
        var entityNames = new HashSet<string>(
            [schema.ClrTypeName],
            StringComparer.Ordinal);
        foreach (var entry in snapshot.PreviousManifest.Artifacts)
        {
            if (StringComparer.Ordinal.Equals(
                    entry.RelativePath,
                    RegistryRelativePath))
            {
                EnsureUnchangedOwnedArtifact(
                    snapshot,
                    entry,
                    "模块聚合注册桥缺失或被修改。");
                continue;
            }

            if (!IsBackendArtifactPath(entry.RelativePath))
            {
                throw Conflict(
                    entry.RelativePath,
                    "模块生成清单包含非实体后端产物。");
            }

            entityNames.Add(EntityName(entry.RelativePath));
            if (entry.RelativePath.StartsWith(
                    currentPrefix,
                    StringComparison.Ordinal))
            {
                continue;
            }

            var content = EnsureUnchangedOwnedArtifact(
                snapshot,
                entry,
                "其他实体的已拥有产物缺失或被修改。");
            allArtifacts.Add(new GeneratedArtifact(
                entry.RelativePath,
                GeneratedArtifactKind.Backend,
                content));
        }

        allArtifacts.Add(CreateRegistryArtifact(
            schema,
            entityNames));
        return GenerationWritePlanner.Plan(
            allArtifacts,
            snapshot.ExistingFiles,
            snapshot.PreviousManifest);
    }

    private static string EntityPrefix(FullNetCrudSchema schema) =>
        $"Generated/{schema.ClrTypeName}/";

    private static string EntityName(string relativePath) =>
        relativePath.Split('/')[1];

    private static bool IsBackendArtifactPath(string relativePath)
    {
        var segments = relativePath.Split('/');
        return segments.Length == 3
            && StringComparer.Ordinal.Equals(segments[0], "Generated")
            && IsIdentifier(segments[1])
            && segments[2].Length > ".g.cs".Length
            && segments[2].EndsWith(
                ".g.cs",
                StringComparison.Ordinal);
    }

    private static bool IsIdentifier(string value) =>
        value.Length > 0
        && (char.IsLetter(value[0]) || value[0] == '_')
        && value.Skip(1).All(character =>
            char.IsLetterOrDigit(character) || character == '_');

    private static string EnsureUnchangedOwnedArtifact(
        GenerationWorkspaceSnapshot snapshot,
        GenerationManifestEntry entry,
        string reason)
    {
        if (!snapshot.ExistingFiles.TryGetValue(
                entry.RelativePath,
                out var content)
            || !StringComparer.Ordinal.Equals(
                GenerationContentHash.Compute(content),
                entry.Sha256))
        {
            throw Conflict(entry.RelativePath, reason);
        }

        return content;
    }

    private static string GenerateRegistry(
        string rootNamespace,
        IReadOnlyList<string> entityNames)
    {
        var serviceCalls = string.Join(
            '\n',
            entityNames.Select(name =>
                $"        services.AddGenerated{name}Feature();"));
        var endpointCalls = string.Join(
            '\n',
            entityNames.Select(name =>
                $"        endpoints.MapGenerated{name}Feature();"));
        return $$"""
            #nullable enable

            using System;
            using Microsoft.AspNetCore.Routing;
            using Microsoft.Extensions.DependencyInjection;

            namespace {{rootNamespace}}.Generated;

            public static class FullNetGeneratedModuleFeatureExtensions
            {
                public static IServiceCollection AddFullNetGeneratedModuleFeatures(
                    this IServiceCollection services)
                {
                    ArgumentNullException.ThrowIfNull(services);
            {{serviceCalls}}
                    return services;
                }

                public static IEndpointRouteBuilder MapFullNetGeneratedModuleFeatures(
                    this IEndpointRouteBuilder endpoints)
                {
                    ArgumentNullException.ThrowIfNull(endpoints);
            {{endpointCalls}}
                    return endpoints;
                }
            }
            """.Replace("\r\n", "\n", StringComparison.Ordinal)
            .TrimEnd('\r', '\n')
            + "\n";
    }

    private static GenerationWorkspaceConflictException Conflict(
        string relativePath,
        string reason) =>
        new(
            $"{reason} 路径：{relativePath}",
            relativePath);
}
