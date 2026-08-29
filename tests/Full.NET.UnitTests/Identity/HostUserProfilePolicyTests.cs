using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.ManageHostUsers;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class HostUserProfilePolicyTests
{
    [TestMethod]
    public void Normalize_and_validate_canonicalizes_authoritative_fields()
    {
        var result = HostUserProfilePolicy.NormalizeAndValidate(CreateProfile(
            phoneNumber: " 13800000000 ",
            email: " User.Name@Example.COM ",
            employeeNumber: " emp-001 ",
            idCardType: " PASSPORT ",
            idCardNumber: " e1234567 "));

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("13800000000", result.Value!.PhoneNumber);
        Assert.AreEqual("user.name@example.com", result.Value.Email);
        Assert.AreEqual("EMP-001", result.Value.EmployeeNumber);
        Assert.AreEqual("passport", result.Value.IdCardType);
        Assert.AreEqual("E1234567", result.Value.IdCardNumber);
    }

    [TestMethod]
    [DataRow("0013800000000")]
    [DataRow("+138-0000-0000")]
    [DataRow("1234567")]
    public void Normalize_and_validate_rejects_non_canonical_phone_shape(string phoneNumber)
    {
        var result = HostUserProfilePolicy.NormalizeAndValidate(
            CreateProfile(phoneNumber: phoneNumber));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(IdentityErrorCodes.UserProfileInvalid, result.Error!.Code);
        CollectionAssert.Contains(
            result.Error.ValidationErrors!.Keys.ToArray(),
            "phoneNumber");
    }

    [TestMethod]
    public void Normalize_and_validate_rejects_invalid_email()
    {
        var result = HostUserProfilePolicy.NormalizeAndValidate(
            CreateProfile(email: "invalid@@example.com"));

        Assert.IsFalse(result.IsSuccess);
        CollectionAssert.Contains(result.Error!.ValidationErrors!.Keys.ToArray(), "email");
    }

    [TestMethod]
    public void Normalize_and_validate_requires_card_type_and_number_together()
    {
        var result = HostUserProfilePolicy.NormalizeAndValidate(
            CreateProfile(idCardType: "passport"));

        Assert.IsFalse(result.IsSuccess);
        CollectionAssert.Contains(result.Error!.ValidationErrors!.Keys.ToArray(), "idCardNumber");
    }

    [TestMethod]
    [DataRow("11010519491231002X", true)]
    [DataRow("110105194912310021", false)]
    [DataRow("110105202302300029", false)]
    public void Normalize_and_validate_checks_mainland_identity_card(
        string idCardNumber,
        bool expectedSuccess)
    {
        var result = HostUserProfilePolicy.NormalizeAndValidate(
            CreateProfile(idCardType: "id_card", idCardNumber: idCardNumber));

        Assert.AreEqual(expectedSuccess, result.IsSuccess);
    }

    [TestMethod]
    public void Normalize_and_validate_rejects_unknown_card_type()
    {
        var result = HostUserProfilePolicy.NormalizeAndValidate(
            CreateProfile(idCardType: "driver_license", idCardNumber: "A123456"));

        Assert.IsFalse(result.IsSuccess);
        CollectionAssert.Contains(result.Error!.ValidationErrors!.Keys.ToArray(), "idCardType");
    }

    private static HostUserProfileWriteRequest CreateProfile(
        string? phoneNumber = null,
        string? email = null,
        string? employeeNumber = null,
        string? idCardType = null,
        string? idCardNumber = null) =>
        new(
            FieldKeys: ["phone_number", "email", "employee_number", "id_card_type", "id_card_number"],
            Nickname: null,
            PhoneNumber: phoneNumber,
            Email: email,
            EmployeeNumber: employeeNumber,
            Gender: null,
            JoinDateUtc: null,
            SortOrder: null,
            IdCardType: idCardType,
            IdCardNumber: idCardNumber,
            BirthDate: null,
            Ethnicity: null,
            Address: null,
            GraduatedSchool: null,
            EducationLevel: null,
            PoliticalStatus: null,
            OfficePhone: null,
            EmergencyContact: null,
            EmergencyContactRelation: null,
            EmergencyContactPhone: null,
            EmergencyContactAddress: null,
            Remark: null,
            Version: null);
}
