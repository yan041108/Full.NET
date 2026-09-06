using Full.NET.Modules.Printing.Contracts;
using Full.NET.Modules.Printing.Domain;

namespace Full.NET.UnitTests.Printing;

[TestClass]
public sealed class PrintingFormSchemaCatalogTests
{
    [TestMethod]
    public void Catalog_exposes_tenant_profile_card_schema()
    {
        var schema = PrintingFormSchemaCatalog.TryGet(PrintingFormSchemaKeys.TenantProfileCard);
        Assert.IsNotNull(schema);
        CollectionAssert.AreEquivalent(
            new[] { "tenantName", "tenantCode", "tenantDomain", "printedByDisplayName", "printedAtUtc" },
            schema!.Fields.Select(field => field.FieldKey).ToArray());
    }
}
