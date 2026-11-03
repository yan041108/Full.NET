using Full.NET.Abstractions.Results;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.ImportExport.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Organization.Features.ImportExport;

namespace Full.NET.UnitTests.Organization;

/// <summary>单元测试（组织）：Tenant Position Import Preview Service。</summary>
[TestClass]
public sealed class TenantPositionImportPreviewServiceTests
{
    private readonly TenantPositionImportPreviewService _service = new();

    [TestMethod]
    public void Preview_rejects_duplicate_codes_and_invalid_names()
    {
        var context = new StaticImportPreviewContext(
            Guid.NewGuid(),
            new Dictionary<string, bool>(StringComparer.Ordinal)
            {
                [OrganizationPositionManagementPermissions.AssignUnit] = true,
                [OrganizationPositionManagementPermissions.AssignPositionLevel] = true,
            });
        var result = _service.Preview(
            [
                new ImportOrganizationPositionRow("engineer", "工程师", 10, null, null),
                new ImportOrganizationPositionRow("engineer", "重复", 10, null, null),
                new ImportOrganizationPositionRow("bad", "", 10, null, null),
            ],
            context);

        Assert.AreEqual(3, result.TotalRows);
        Assert.AreEqual(1, result.ValidRowCount);
        Assert.AreEqual(2, result.InvalidRowCount);
        Assert.IsFalse(result.Rows[1].IsValid);
        Assert.AreEqual(
            OrganizationErrorCodes.PositionImportDuplicateCode,
            result.Rows[1].ErrorCode);
    }

    [TestMethod]
    public void Preview_rejects_unit_assignment_without_capability()
    {
        var context = new StaticImportPreviewContext(
            Guid.NewGuid(),
            new Dictionary<string, bool>(StringComparer.Ordinal));
        var result = _service.Preview(
            [new ImportOrganizationPositionRow("engineer", "工程师", 10, "sales", null)],
            context);

        Assert.AreEqual(1, result.InvalidRowCount);
        Assert.AreEqual(CommonErrorCodes.PermissionDenied, result.Rows[0].ErrorCode);
    }

    [TestMethod]
    public void Assignment_requires_both_field_write_and_reference_read_capabilities()
    {
        foreach (var unit in new[] { true, false })
        {
            var flags = new Dictionary<string, bool> { [unit ? OrganizationPositionManagementPermissions.AssignUnit : OrganizationPositionManagementPermissions.AssignPositionLevel] = true };
            var context = new StaticImportPreviewContext(Guid.CreateVersion7(), flags);
            var row = new ImportOrganizationPositionRow("engineer", "工程师", 10, unit ? "sales" : null, unit ? null : "senior");
            Assert.AreEqual(0, _service.Preview([row], context).ValidRowCount);
            flags[unit ? OrganizationUnitManagementPermissions.Read : OrganizationPositionLevelManagementPermissions.Read] = true;
            Assert.AreEqual(1, _service.Preview([row], context).ValidRowCount);
        }
    }
}
