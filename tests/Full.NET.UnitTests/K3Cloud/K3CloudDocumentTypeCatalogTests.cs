using Full.NET.Modules.K3Cloud.Domain;
using Full.NET.Modules.K3Cloud.Contracts;

namespace Full.NET.UnitTests.K3Cloud;

[TestClass]
public sealed class K3CloudDocumentTypeCatalogTests
{
    [TestMethod]
    public void ResolveFormId_maps_sale_order_slice()
    {
        Assert.AreEqual(
            "SAL_SaleOrder",
            K3CloudDocumentTypeCatalog.ResolveFormId(K3CloudDocumentTypeKeys.SalSaleOrder));
    }

    [TestMethod]
    public void IsSupported_rejects_unknown_document_type()
    {
        Assert.IsFalse(K3CloudDocumentTypeCatalog.IsSupported("k3cloud.unknown"));
    }
}
