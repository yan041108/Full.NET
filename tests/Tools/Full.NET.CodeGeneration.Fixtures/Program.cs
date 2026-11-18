using Full.NET.Data.CodeGeneration.Generation;
using Full.NET.Tests.CodeGeneration;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
var repository = CodeGenerationRemediationSchemas.RepositoryRoot();
if (args.Length is < 1 or > 2 || args[0] is not ("--write" or "--check" or "--preview")
    || args.Length == 2 && args[1] is not ("--catalog" or "--remediation"))
    throw new ArgumentException("使用 --check 检查、--write 更新或 --preview 只读预览；可加 --catalog 或 --remediation 限定范围。");
var check = args[0] == "--check";
var write = args[0] == "--write";
var preview = args[0] == "--preview";
// 单次页面或端点修复不能顺带重写共享数据库恢复夹具。
var catalogOnly = args.Length == 2 && args[1] == "--catalog";
var remediationOnly = args.Length == 2 && args[1] == "--remediation";
var owned = new SortedDictionary<string, string>(StringComparer.Ordinal);
var changes = new SortedDictionary<string, (string? BeforeHash, string AfterHash, string Content)>(StringComparer.Ordinal);
var root = Path.Combine(repository, "tests/Shared/CodeGeneration/Remediation");
if (!catalogOnly)
{
    if (write) Directory.CreateDirectory(root);
    foreach (var schema in new[] { CodeGenerationRemediationSchemas.Product(), CodeGenerationRemediationSchemas.ProductItem(), CodeGenerationRemediationSchemas.ProductTag(), CodeGenerationRemediationSchemas.Workbench(), CodeGenerationRemediationSchemas.TreeNode(), CodeGenerationRemediationSchemas.OrganizationTreeNode(), CodeGenerationRemediationSchemas.OrganizationParent(), CodeGenerationRemediationSchemas.OrganizationParent().Relationships[0].DependentSchema!, CodeGenerationRemediationSchemas.OrganizationLink(), CodeGenerationRemediationSchemas.OrganizationLink().Relationships[0].PrincipalSchema!, CodeGenerationRemediationSchemas.OrganizationLink().Relationships[1].PrincipalSchema! })
    {
        foreach (var artifact in CrudArtifactGenerator.Generate(schema).Where(item => item.Kind == GeneratedArtifactKind.Backend && item.RelativePath.EndsWith(".g.cs") && !item.RelativePath.EndsWith("Endpoint.g.cs")))
            Save(Path.Combine(root, Path.GetFileName(artifact.RelativePath)), artifact.Content);
    }
    if (!preview) Console.WriteLine("Generated actual remediation fixtures.");
}
if (!remediationOnly)
{
var legacy = Full.NET.Data.CodeGeneration.Schema.FullNetCrudSchema.CreateProject("acme", "catalog", "product", "acme_catalog_product", "Acme.Modules.Catalog", "Product", "products", "products", true, true,
new Full.NET.Data.CodeGeneration.Schema.FullNetColumn[] {
 new("Id","Id","id", Full.NET.Data.CodeGeneration.Schema.FullNetScalarType.Uuid),
 new("TenantId","TenantId","tenantId", Full.NET.Data.CodeGeneration.Schema.FullNetScalarType.Uuid),
 new("Name","Name","displayName", Full.NET.Data.CodeGeneration.Schema.FullNetScalarType.String, MaxLength:200),
 new("Description","Description","description", Full.NET.Data.CodeGeneration.Schema.FullNetScalarType.String, IsNullable:true,MaxLength:500),
 new("IsActive","IsActive","isActive", Full.NET.Data.CodeGeneration.Schema.FullNetScalarType.Boolean),
 new("Version","Version","version", Full.NET.Data.CodeGeneration.Schema.FullNetScalarType.Int64),
 new("CreatedAtUtc","CreatedAtUtc","createdAtUtc", Full.NET.Data.CodeGeneration.Schema.FullNetScalarType.DateTimeUtc)
});
foreach (var artifact in CrudArtifactGenerator.Generate(legacy, includeLayuiClientArtifacts:true)) {
 var output=Path.Combine(Path.Combine(repository, "tests/Full.NET.UnitTests/CodeGeneration/Fixtures/CatalogProduct"),artifact.RelativePath);
 if (write) Directory.CreateDirectory(Path.GetDirectoryName(output)!);
 Save(output, artifact.Content);
}
if (!preview) Console.WriteLine("Generated actual catalog fixtures.");
}
if (preview)
{
    using var buffer = new MemoryStream();
    using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
    {
        writer.WriteStartObject(); writer.WriteNumber("schemaVersion", 1);
        writer.WriteString("scope", catalogOnly ? "catalog" : remediationOnly ? "remediation" : "all");
        writer.WriteString("generatorVersion", CrudArtifactGenerator.GeneratorVersion);
        writer.WriteStartArray("ownedPaths"); foreach (var path in owned.Keys) writer.WriteStringValue(path); writer.WriteEndArray();
        writer.WriteStartArray("changes");
        foreach (var (path, change) in changes)
        {
            writer.WriteStartObject(); writer.WriteString("path", path); writer.WriteString("beforeHash", change.BeforeHash);
            writer.WriteString("afterHash", change.AfterHash); writer.WriteString("content", change.Content); writer.WriteEndObject();
        }
        writer.WriteEndArray(); writer.WriteEndObject();
    }
    Console.WriteLine(Encoding.UTF8.GetString(buffer.ToArray()));
}
void Save(string path, string content)
{
    var relative = Path.GetRelativePath(repository, path).Replace(Path.DirectorySeparatorChar, '/');
    // Git 的平台换行不能伪装成生成漂移；摘要与写入都采用生成器的规范 LF 内容。
    var canonical = content.ReplaceLineEndings("\n");
    var previous = File.Exists(path) ? File.ReadAllText(path).ReplaceLineEndings("\n") : null;
    var hash = Hash(canonical);
    if (owned.TryGetValue(relative, out var existing) && existing != hash) throw new InvalidOperationException("重复生成路径内容不一致：" + relative);
    owned[relative] = hash;
    if (preview)
    {
        if (previous != canonical) changes[relative] = (previous is null ? null : Hash(previous), hash, canonical);
    }
    else if (check)
    {
        if (previous != canonical) throw new InvalidOperationException("生成夹具已过期：" + relative);
    }
    else File.WriteAllText(path, canonical, new UTF8Encoding(false));
}
static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
