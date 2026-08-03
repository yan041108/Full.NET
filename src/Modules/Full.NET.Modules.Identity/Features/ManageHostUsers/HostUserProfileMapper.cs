using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;

namespace Full.NET.Modules.Identity.Features.ManageHostUsers;

internal static class HostUserProfileMapper
{
    public static HostUserProfileResponse? ToResponse(HostUserProfileRecord? record) =>
        record is null
            ? null
            : new HostUserProfileResponse(
                record.Nickname,
                record.PhoneNumber,
                record.Email,
                record.EmployeeNumber,
                record.Gender,
                FormatDate(record.JoinDateUtc),
                record.SortOrder,
                record.IdCardType,
                record.IdCardNumber,
                FormatDate(record.BirthDate),
                record.Ethnicity,
                record.Address,
                record.GraduatedSchool,
                record.EducationLevel,
                record.PoliticalStatus,
                record.OfficePhone,
                record.EmergencyContact,
                record.EmergencyContactPhone,
                record.EmergencyContactAddress,
                record.Remark,
                record.Version);

    public static object ToParameters(
        Guid userId,
        HostUserProfileWriteRequest profile) =>
        new
        {
            UserId = userId,
            Nickname = Normalize(profile.Nickname),
            PhoneNumber = Normalize(profile.PhoneNumber),
            Email = Normalize(profile.Email),
            EmployeeNumber = Normalize(profile.EmployeeNumber),
            Gender = Normalize(profile.Gender),
            JoinDateUtc = ParseDate(profile.JoinDateUtc),
            SortOrder = profile.SortOrder ?? 100,
            IdCardType = Normalize(profile.IdCardType),
            IdCardNumber = Normalize(profile.IdCardNumber),
            BirthDate = ParseDate(profile.BirthDate),
            Ethnicity = Normalize(profile.Ethnicity),
            Address = Normalize(profile.Address),
            GraduatedSchool = Normalize(profile.GraduatedSchool),
            EducationLevel = Normalize(profile.EducationLevel),
            PoliticalStatus = Normalize(profile.PoliticalStatus),
            OfficePhone = Normalize(profile.OfficePhone),
            EmergencyContact = Normalize(profile.EmergencyContact),
            EmergencyContactPhone = Normalize(profile.EmergencyContactPhone),
            EmergencyContactAddress = Normalize(profile.EmergencyContactAddress),
            Remark = Normalize(profile.Remark),
            Version = profile.Version ?? 0
        };

    private static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static DateTime? ParseDate(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : DateTime.TryParse(value, out var parsed)
                ? parsed.Date
                : null;

    private static string? FormatDate(DateTime? value) =>
        value?.ToString("yyyy-MM-dd");
}
