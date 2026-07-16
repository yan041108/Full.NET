using Full.NET.Modules.Tenancy.Features.ProvisionTenant;

namespace Full.NET.UnitTests.Tenancy;

[TestClass]
public sealed class ProvisionTenantCommandValidatorTests
{
    private const string IdentifierMessage =
        "Identifier must be 3-64 lowercase letters, numbers, or hyphens.";
    private const string NameMessage =
        "Name is required and must not exceed 128 characters.";
    private const string DomainMessage =
        "Domain is required and must not exceed 253 characters.";

    [TestMethod]
    public async Task Valid_trimmed_command_passes()
    {
        var validator = new ProvisionTenantCommandValidator();

        var result = await validator.ValidateAsync(new ProvisionTenantCommand(
            " ACME ",
            " Acme Corporation ",
            " ACME.LOCALHOST "));

        Assert.IsTrue(result.IsValid);
    }

    [TestMethod]
    public async Task Invalid_identifier_is_rejected()
    {
        await AssertInvalidAsync(
            new ProvisionTenantCommand("not_valid", "Acme", "acme.localhost"),
            nameof(ProvisionTenantCommand.Identifier),
            IdentifierMessage);
    }

    [TestMethod]
    public async Task Blank_or_long_name_is_rejected()
    {
        await AssertInvalidAsync(
            new ProvisionTenantCommand("acme", " ", "acme.localhost"),
            nameof(ProvisionTenantCommand.Name),
            NameMessage);
        await AssertInvalidAsync(
            new ProvisionTenantCommand(
                "acme",
                new string('x', 129),
                "acme.localhost"),
            nameof(ProvisionTenantCommand.Name),
            NameMessage);
    }

    [TestMethod]
    public async Task Blank_or_long_domain_is_rejected()
    {
        await AssertInvalidAsync(
            new ProvisionTenantCommand("acme", "Acme", " "),
            nameof(ProvisionTenantCommand.Domain),
            DomainMessage);
        await AssertInvalidAsync(
            new ProvisionTenantCommand(
                "acme",
                "Acme",
                new string('x', 254)),
            nameof(ProvisionTenantCommand.Domain),
            DomainMessage);
    }

    private static async Task AssertInvalidAsync(
        ProvisionTenantCommand command,
        string propertyName,
        string expectedMessage)
    {
        var validator = new ProvisionTenantCommandValidator();

        var result = await validator.ValidateAsync(command);

        var messages = result.Errors
            .Where(error => error.PropertyName == propertyName)
            .Select(error => error.ErrorMessage)
            .ToArray();
        CollectionAssert.AreEqual(new[] { expectedMessage }, messages);
    }
}
