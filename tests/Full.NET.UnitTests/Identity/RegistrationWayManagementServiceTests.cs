using Full.NET.Abstractions.Results;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.ManageRegistrationWays;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class RegistrationWayManagementServiceTests
{
    [TestMethod]
    public void ValidateMetadata_rejects_blank_name_and_invalid_code()
    {
        var blankName = RegistrationWayManagementService.ValidateMetadata(
            "   ",
            "valid-code",
            null);
        Assert.IsFalse(blankName.IsSuccess);
        Assert.AreEqual(ValidationErrorCodes.Failed, blankName.Error!.Code);

        var invalidCode = RegistrationWayManagementService.ValidateMetadata(
            "有效名称",
            "Invalid Code",
            null);
        Assert.IsFalse(invalidCode.IsSuccess);
        Assert.AreEqual(
            IdentityErrorCodes.RegistrationWayInvalidCode,
            invalidCode.Error!.Code);
    }

    [TestMethod]
    public void ValidateMetadata_normalizes_code_to_lowercase()
    {
        var result = RegistrationWayManagementService.ValidateMetadata(
            " 手机注册 ",
            " Mobile-Signup ",
            " 备注 ");
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("手机注册", result.Value!.Name);
        Assert.AreEqual("mobile-signup", result.Value.Code);
        Assert.AreEqual("备注", result.Value.Remark);
    }
}
