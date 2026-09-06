using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Directory;
using Full.NET.Modules.Identity.Features.ManageLdapConnections;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Security;
using Microsoft.AspNetCore.DataProtection;
using NSubstitute;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class LdapConnectionManagementServiceTests
{
    [TestMethod]
    public void ValidateMetadata_rejects_invalid_port_and_requires_filter_placeholder()
    {
        var invalidPort = LdapConnectionManagementService.ValidateMetadata(
            "Corp LDAP",
            "ldap.example.com",
            70000,
            "DC=example,DC=com",
            "CN=svc,DC=example,DC=com",
            "(sAMAccountName={0})",
            "sAMAccountName",
            null,
            null,
            "OU=Users,DC=example,DC=com");
        Assert.IsFalse(invalidPort.IsSuccess);
        Assert.AreEqual(
            IdentityErrorCodes.LdapConnectionInvalidMetadata,
            invalidPort.Error!.Code);

        var missingPlaceholder = LdapConnectionManagementService.ValidateMetadata(
            "Corp LDAP",
            "ldap.example.com",
            389,
            "DC=example,DC=com",
            "CN=svc,DC=example,DC=com",
            "(sAMAccountName=alice)",
            "sAMAccountName",
            null,
            null,
            "OU=Users,DC=example,DC=com");
        Assert.IsFalse(missingPlaceholder.IsSuccess);
    }

    [TestMethod]
    public void ValidateMetadata_applies_defaults_for_filter_and_account_attribute()
    {
        var result = LdapConnectionManagementService.ValidateMetadata(
            "  Corp LDAP ",
            " ldap.example.com ",
            636,
            " DC=example,DC=com ",
            " CN=svc,DC=example,DC=com ",
            "   ",
            "   ",
            null,
            null,
            " OU=Users,DC=example,DC=com ");
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("Corp LDAP", result.Value!.Name);
        Assert.AreEqual(
            LdapConnectionManagementService.DefaultUserSearchFilter,
            result.Value.UserSearchFilter);
        Assert.AreEqual(
            LdapConnectionManagementService.DefaultUserAccountAttribute,
            result.Value.UserAccountAttribute);
    }
}

[TestClass]
public sealed class LdapDnScopeValidatorTests
{
    [TestMethod]
    public void IsSameOrSubordinate_accepts_equal_and_child_dns()
    {
        const string root = "DC=example,DC=com";
        Assert.IsTrue(LdapDnScopeValidator.IsSameOrSubordinate(root, root));
        Assert.IsTrue(
            LdapDnScopeValidator.IsSameOrSubordinate(
                "OU=Users,DC=example,DC=com",
                root));
    }

    [TestMethod]
    public void IsSameOrSubordinate_rejects_out_of_scope_dns()
    {
        Assert.IsFalse(
            LdapDnScopeValidator.IsSameOrSubordinate(
                "DC=other,DC=com",
                "DC=example,DC=com"));
        Assert.IsFalse(
            LdapDnScopeValidator.IsSameOrSubordinate(
                "OU=Users,DC=other,DC=com",
                "DC=example,DC=com"));
    }
}

[TestClass]
public sealed class LdapConnectionOperationsServiceTests
{
    [TestMethod]
    public async Task PreviewSyncAsync_rejects_search_base_outside_whitelist()
    {
        var queryExecutor = Substitute.For<IQueryExecutor>();
        var bindPasswordProtector = new LdapBindPasswordProtector(new EphemeralDataProtectionProvider());
        var protectedPassword = bindPasswordProtector.Protect("secret");
        var ldapClient = Substitute.For<ILdapDirectoryClient>();
        var service = new LdapConnectionOperationsService(
            queryExecutor,
            bindPasswordProtector,
            ldapClient);
        var connectionId = Guid.NewGuid();
        queryExecutor.QuerySingleOrDefaultAsync<LdapConnectionRecord>(
                Arg.Any<SqlStatement>(),
                Arg.Any<object>(),
                Arg.Any<CancellationToken>())
            .Returns(new LdapConnectionRecord
            {
                Id = connectionId,
                Host = "ldap.example.com",
                Port = 389,
                BaseDn = "DC=example,DC=com",
                BindDn = "CN=svc,DC=example,DC=com",
                BindPasswordProtected = protectedPassword,
                UserSearchFilter = "(sAMAccountName={0})",
                UserAccountAttribute = "sAMAccountName",
                SyncSearchBaseDn = "OU=Users,DC=example,DC=com",
            });

        var result = await service.PreviewSyncAsync(
            connectionId,
            new PreviewLdapSyncRequest("DC=other,DC=com", 10));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(
            IdentityErrorCodes.LdapConnectionPreviewSearchBaseOutOfScope,
            result.Error!.Code);
        await ldapClient.DidNotReceiveWithAnyArgs().PreviewEntriesAsync(
            default!,
            default!,
            default,
            default);
    }
}
