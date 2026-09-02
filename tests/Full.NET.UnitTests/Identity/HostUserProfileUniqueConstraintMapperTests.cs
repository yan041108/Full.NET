using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.ManageHostUsers;

namespace Full.NET.UnitTests.Identity;

/// <summary>验证 Host 用户资料唯一约束竞态可稳定映射为 409 冲突语义。</summary>
[TestClass]
public sealed class HostUserProfileUniqueConstraintMapperTests
{
    [TestMethod]
    public void TryMapConflict_maps_email_index_from_sql_server_duplicate_key()
    {
        var exception = new DataCommandException(
            DataCommandFailureKind.UniqueConstraint,
            new InvalidOperationException(
                "Cannot insert duplicate key row in object 'dbo.fn_identity_user_profile' "
                + "with unique index 'UX_fn_identity_user_profile_Email'."));
        var profile = CreateProfile(email: "race@example.com");

        var conflict = HostUserProfileUniqueConstraintMapper.TryMapConflict(exception, profile);

        AssertConflict(
            conflict,
            IdentityErrorCodes.UserEmailExists,
            "Email is already assigned to another host user.");
    }

    [TestMethod]
    public void TryMapConflict_maps_email_when_only_email_field_is_written()
    {
        var exception = new DataCommandException(
            DataCommandFailureKind.UniqueConstraint,
            new InvalidOperationException("duplicate key value violates unique constraint"));
        var profile = CreateProfile(email: "race@example.com");

        var conflict = HostUserProfileUniqueConstraintMapper.TryMapConflict(exception, profile);

        AssertConflict(
            conflict,
            IdentityErrorCodes.UserEmailExists,
            "Email is already assigned to another host user.");
    }

    [TestMethod]
    public void TryMapConflict_returns_null_when_multiple_unique_fields_are_written_without_index_hint()
    {
        var exception = new DataCommandException(
            DataCommandFailureKind.UniqueConstraint,
            new InvalidOperationException("duplicate key value violates unique constraint"));
        var profile = CreateProfile(
            email: "race@example.com",
            phoneNumber: "13800000000");

        var conflict = HostUserProfileUniqueConstraintMapper.TryMapConflict(exception, profile);

        Assert.IsNull(conflict);
    }

    private static HostUserProfileWriteRequest CreateProfile(
        string? phoneNumber = null,
        string? email = null,
        string? employeeNumber = null) =>
        new(
            FieldKeys: new[]
            {
                phoneNumber is null ? null : "phone_number",
                email is null ? null : "email",
                employeeNumber is null ? null : "employee_number",
            }.Where(key => key is not null).Cast<string>().ToArray(),
            Nickname: null,
            PhoneNumber: phoneNumber,
            Email: email,
            EmployeeNumber: employeeNumber,
            Gender: null,
            JoinDateUtc: null,
            SortOrder: null,
            IdCardType: null,
            IdCardNumber: null,
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

    private static void AssertConflict(Error? conflict, string expectedCode, string expectedMessage)
    {
        Assert.IsNotNull(conflict);
        Assert.AreEqual(expectedCode, conflict.Code);
        Assert.AreEqual(expectedMessage, conflict.Message);
        Assert.AreEqual(ErrorType.Conflict, conflict.Type);
    }
}
