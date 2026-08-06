using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.ManageHostUsers;
using Full.NET.Modules.Identity.Persistence;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class HostUserProfileMapperTests
{
    [TestMethod]
    public void Merge_preserves_existing_hidden_fields_when_patch_field_keys_are_partial()
    {
        var existing = new HostUserProfileRecord
        {
            UserId = Guid.CreateVersion7(),
            Nickname = "旧昵称",
            PhoneNumber = "13800000000",
            Email = "before@example.com",
            EmployeeNumber = "E-001",
            Address = "旧地址",
            Remark = "保留备注",
            SortOrder = 9,
            Version = 3,
        };

        var merged = HostUserProfileMapper.Merge(
            existing,
            new HostUserProfileWriteRequest(
                FieldKeys: ["nickname", "email"],
                Nickname: "新昵称",
                PhoneNumber: null,
                Email: "after@example.com",
                EmployeeNumber: null,
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
                EmergencyContactPhone: null,
                EmergencyContactAddress: null,
                Remark: null,
                Version: null));

        Assert.AreEqual("新昵称", merged.Nickname);
        Assert.AreEqual("after@example.com", merged.Email);
        Assert.AreEqual("13800000000", merged.PhoneNumber);
        Assert.AreEqual("E-001", merged.EmployeeNumber);
        Assert.AreEqual("旧地址", merged.Address);
        Assert.AreEqual("保留备注", merged.Remark);
        Assert.AreEqual(9, merged.SortOrder);
        Assert.AreEqual(3, merged.Version);
    }
}
