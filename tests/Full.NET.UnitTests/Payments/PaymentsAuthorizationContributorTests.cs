using System.Security.Cryptography;
using System.Text;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Payments;
using Full.NET.Modules.Payments.Contracts;

namespace Full.NET.UnitTests.Payments;

[TestClass]
public sealed class PaymentsAuthorizationContributorTests
{
    [TestMethod]
    public void Contributor_registers_merchant_and_order_permissions()
    {
        var catalog = AuthorizationCatalog.Create([new PaymentsAuthorizationContributor()]);

        CollectionAssert.AreEquivalent(
            new[]
            {
                PaymentMerchantPermissions.Read,
                PaymentMerchantPermissions.Create,
                PaymentMerchantPermissions.Update,
                PaymentOrderPermissions.Read,
                PaymentOrderPermissions.Create,
            },
            catalog.Permissions.Select(permission => permission.Code).ToArray());

        var merchantNavigation = catalog.Navigation.Single(item => item.Id == "payments-merchant-configs");
        Assert.AreEqual(PaymentMerchantPermissions.Read, merchantNavigation.RequiredPermission);
        Assert.AreEqual("/payments/merchant-configs", merchantNavigation.Path);

        var orderNavigation = catalog.Navigation.Single(item => item.Id == "payments-orders");
        Assert.AreEqual(PaymentOrderPermissions.Read, orderNavigation.RequiredPermission);
        Assert.AreEqual("/payments/orders", orderNavigation.Path);
    }
}
