using Full.NET.Abstractions.Results;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.ManageOpenAccessClients;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class OpenAccessClientManagementServiceTests
{
    [TestMethod]
    public void ValidateMetadata_rejects_blank_name_and_oversized_fields()
    {
        var blankName = OpenAccessClientManagementService.ValidateMetadata(
            "   ",
            null,
            null);
        Assert.IsFalse(blankName.IsSuccess);
        Assert.AreEqual(
            ValidationErrorCodes.Failed,
            blankName.Error!.Code);

        var oversizedName = OpenAccessClientManagementService.ValidateMetadata(
            new string('a', OpenAccessClientManagementService.MaxNameLength + 1),
            null,
            null);
        Assert.IsFalse(oversizedName.IsSuccess);

        var oversizedDescription = OpenAccessClientManagementService.ValidateMetadata(
            "合法名称",
            new string('b', OpenAccessClientManagementService.MaxDescriptionLength + 1),
            null);
        Assert.IsFalse(oversizedDescription.IsSuccess);
    }

    [TestMethod]
    public void ValidateMetadata_normalizes_optional_fields()
    {
        var result = OpenAccessClientManagementService.ValidateMetadata(
            " 测试应用 ",
            " 描述 ",
            " 备注 ");
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("测试应用", result.Value!.Name);
        Assert.AreEqual("描述", result.Value.Description);
        Assert.AreEqual("备注", result.Value.Remark);
    }
}
