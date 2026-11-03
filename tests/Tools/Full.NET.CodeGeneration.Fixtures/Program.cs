using Full.NET.Data.CodeGeneration.Generation;
using Full.NET.Tests.CodeGeneration;
var repository = CodeGenerationRemediationSchemas.RepositoryRoot();
if (args.Length != 1 || args[0] is not ("--write" or "--check")) throw new ArgumentException("使用 --check 检查或 --write 更新本工具拥有的夹具。");
var check = args[0] == "--check";
var root = Path.Combine(repository, "tests/Shared/CodeGeneration/Remediation");
if (!check) Directory.CreateDirectory(root);
foreach (var schema in new[] { CodeGenerationRemediationSchemas.Product(), CodeGenerationRemediationSchemas.ProductItem(), CodeGenerationRemediationSchemas.ProductTag(), CodeGenerationRemediationSchemas.Workbench(), CodeGenerationRemediationSchemas.TreeNode(), CodeGenerationRemediationSchemas.OrganizationTreeNode(), CodeGenerationRemediationSchemas.OrganizationParent(), CodeGenerationRemediationSchemas.OrganizationParent().Relationships[0].DependentSchema!, CodeGenerationRemediationSchemas.OrganizationLink(), CodeGenerationRemediationSchemas.OrganizationLink().Relationships[0].PrincipalSchema!, CodeGenerationRemediationSchemas.OrganizationLink().Relationships[1].PrincipalSchema! })
{
    foreach (var artifact in CrudArtifactGenerator.Generate(schema).Where(item => item.Kind == GeneratedArtifactKind.Backend && item.RelativePath.EndsWith(".g.cs") && !item.RelativePath.EndsWith("Endpoint.g.cs")))
        Save(Path.Combine(root, Path.GetFileName(artifact.RelativePath)), artifact.Content);
}
Console.WriteLine("Generated actual remediation fixtures.");
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
 if (!check) Directory.CreateDirectory(Path.GetDirectoryName(output)!);
 Save(output, artifact.Content);
}
void Save(string path, string content) { if (check) { if (!File.Exists(path) || File.ReadAllText(path) != content) throw new InvalidOperationException("生成夹具已过期：" + Path.GetRelativePath(repository, path)); } else File.WriteAllText(path, content, new System.Text.UTF8Encoding(false)); }
