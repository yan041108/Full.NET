using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;

namespace Full.NET.Modules.Identity.Features.ManageHostUsers;

internal static class HostUserProfileMapper
{
    private static readonly string[] EditableFieldKeys =
    [
        "nickname",
        "phone_number",
        "email",
        "employee_number",
        "gender",
        "join_date_utc",
        "sort_order",
        "id_card_type",
        "id_card_number",
        "birth_date",
        "ethnicity",
        "address",
        "graduated_school",
        "education_level",
        "political_status",
        "office_phone",
        "emergency_contact",
        "emergency_contact_phone",
        "emergency_contact_address",
        "remark",
    ];

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

    public static HostUserProfileWriteRequest Merge(
        HostUserProfileRecord? existing,
        HostUserProfileWriteRequest patch)
    {
        var fieldKeys = NormalizeFieldKeys(patch.FieldKeys);
        if (fieldKeys.Count == 0)
        {
            fieldKeys = EditableFieldKeys;
        }

        return new HostUserProfileWriteRequest(
            FieldKeys: fieldKeys,
            Nickname: Resolve(fieldKeys, "nickname", patch.Nickname, existing?.Nickname),
            PhoneNumber: Resolve(fieldKeys, "phone_number", patch.PhoneNumber, existing?.PhoneNumber),
            Email: Resolve(fieldKeys, "email", patch.Email, existing?.Email),
            EmployeeNumber: Resolve(fieldKeys, "employee_number", patch.EmployeeNumber, existing?.EmployeeNumber),
            Gender: Resolve(fieldKeys, "gender", patch.Gender, existing?.Gender),
            JoinDateUtc: ResolveDate(fieldKeys, "join_date_utc", patch.JoinDateUtc, existing?.JoinDateUtc),
            SortOrder: ResolveNumber(fieldKeys, "sort_order", patch.SortOrder, existing?.SortOrder ?? 100),
            IdCardType: Resolve(fieldKeys, "id_card_type", patch.IdCardType, existing?.IdCardType),
            IdCardNumber: Resolve(fieldKeys, "id_card_number", patch.IdCardNumber, existing?.IdCardNumber),
            BirthDate: ResolveDate(fieldKeys, "birth_date", patch.BirthDate, existing?.BirthDate),
            Ethnicity: Resolve(fieldKeys, "ethnicity", patch.Ethnicity, existing?.Ethnicity),
            Address: Resolve(fieldKeys, "address", patch.Address, existing?.Address),
            GraduatedSchool: Resolve(fieldKeys, "graduated_school", patch.GraduatedSchool, existing?.GraduatedSchool),
            EducationLevel: Resolve(fieldKeys, "education_level", patch.EducationLevel, existing?.EducationLevel),
            PoliticalStatus: Resolve(fieldKeys, "political_status", patch.PoliticalStatus, existing?.PoliticalStatus),
            OfficePhone: Resolve(fieldKeys, "office_phone", patch.OfficePhone, existing?.OfficePhone),
            EmergencyContact: Resolve(fieldKeys, "emergency_contact", patch.EmergencyContact, existing?.EmergencyContact),
            EmergencyContactPhone: Resolve(
                fieldKeys,
                "emergency_contact_phone",
                patch.EmergencyContactPhone,
                existing?.EmergencyContactPhone),
            EmergencyContactAddress: Resolve(
                fieldKeys,
                "emergency_contact_address",
                patch.EmergencyContactAddress,
                existing?.EmergencyContactAddress),
            Remark: Resolve(fieldKeys, "remark", patch.Remark, existing?.Remark),
            Version: patch.Version ?? existing?.Version ?? 0);
    }

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

    public static IReadOnlyList<string> NormalizeFieldKeys(
        IReadOnlyList<string>? fieldKeys) =>
        fieldKeys?
            .Where(fieldKey => !string.IsNullOrWhiteSpace(fieldKey))
            .Select(fieldKey => fieldKey.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray()
        ?? [];

    private static string? Resolve(
        IReadOnlyCollection<string> fieldKeys,
        string fieldKey,
        string? incoming,
        string? existing) =>
        fieldKeys.Contains(fieldKey, StringComparer.Ordinal)
            ? incoming
            : existing;

    private static string? ResolveDate(
        IReadOnlyCollection<string> fieldKeys,
        string fieldKey,
        string? incoming,
        DateTime? existing) =>
        fieldKeys.Contains(fieldKey, StringComparer.Ordinal)
            ? incoming
            : FormatDate(existing);

    private static int? ResolveNumber(
        IReadOnlyCollection<string> fieldKeys,
        string fieldKey,
        int? incoming,
        int existing) =>
        fieldKeys.Contains(fieldKey, StringComparer.Ordinal)
            ? incoming
            : existing;

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
