using Full.NET.Modules.ImportExport.Contracts;
using Full.NET.Modules.ImportExport.Domain;

namespace Full.NET.UnitTests.ImportExport;

[TestClass]
public sealed class StaticImportSchemaRegistryTests
{
    [TestMethod]
    public void Registry_resolves_registered_schema_handlers()
    {
        var handler = new StubStaticImportSchemaHandler(
            StaticImportSchemaKeys.OrganizationTenantPositions,
            "租户职位");
        var registry = new StaticImportSchemaRegistry([handler]);

        var definitions = registry.ListDefinitions();
        Assert.HasCount(1, definitions);
        Assert.AreEqual(StaticImportSchemaKeys.OrganizationTenantPositions, definitions[0].SchemaKey);

        var resolved = registry.TryResolve(StaticImportSchemaKeys.OrganizationTenantPositions);
        Assert.IsNotNull(resolved);
        Assert.AreSame(handler, resolved);
        Assert.IsNull(registry.TryResolve("missing.schema"));
    }

    private sealed class StubStaticImportSchemaHandler(string schemaKey, string displayName)
        : IStaticImportSchemaHandler
    {
        public string SchemaKey => schemaKey;

        public StaticImportSchemaDefinition GetDefinition() =>
            new(
                schemaKey,
                displayName,
                StaticImportSchemaScopeKeys.Tenant,
                ImportExportPermissions.StaticSchemasRead,
                [new StaticImportWorksheetDefinition("positions", "职位", ["code", "name"])]);

        public byte[] CreateTemplate(string worksheetKey) => [1, 2, 3];

        public Task<Abstractions.Results.Result<StaticImportPreviewResult>> PreviewAsync(
            Stream content,
            long contentLength,
            StaticImportPreviewContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Abstractions.Results.Result<StaticImportPreviewResult>.Success(
                new StaticImportPreviewResult(0, 0, 0, [])));
    }
}
