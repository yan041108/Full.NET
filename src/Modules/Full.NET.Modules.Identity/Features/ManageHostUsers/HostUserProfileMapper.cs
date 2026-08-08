using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;

namespace Full.NET.Modules.Identity.Features.ManageHostUsers;

internal static class HostUserProfileMapper
{
    private static readonly string[] ProfileFieldKeys =
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
        "emergency_contact_relation",
        "emergency_contact_phone",
        "emergency_contact_address",
        "remark",
    ];

    private static readonly string[] EditableFieldKeys =
    [
        .. ProfileFieldKeys,
    ];

    public static HostUserProfileResponse? ToResponse(
        HostUserProfileRecord? record,
        IReadOnlyCollection<string>? effectiveFieldKeys = null)
    {
        if (record is null)
        {
            return null;
        }

        var readableFieldKeys = GetReadableFieldKeys(effectiveFieldKeys);
        if (readableFieldKeys.Count == 0)
        {
            return null;
        }

        return new HostUserProfileResponse(
            HasField(readableFieldKeys, "nickname") ? record.Nickname : null,
            HasField(readableFieldKeys, "phone_number") ? record.PhoneNumber : null,
            HasField(readableFieldKeys, "email") ? record.Email : null,
            HasField(readableFieldKeys, "employee_number") ? record.EmployeeNumber : null,
            HasField(readableFieldKeys, "gender") ? record.Gender : null,
            HasField(readableFieldKeys, "join_date_utc") ? FormatDate(record.JoinDateUtc) : null,
            HasField(readableFieldKeys, "sort_order") ? record.SortOrder : null,
            HasField(readableFieldKeys, "id_card_type") ? record.IdCardType : null,
            HasField(readableFieldKeys, "id_card_number") ? record.IdCardNumber : null,
            HasField(readableFieldKeys, "birth_date") ? FormatDate(record.BirthDate) : null,
            HasField(readableFieldKeys, "ethnicity") ? record.Ethnicity : null,
            HasField(readableFieldKeys, "address") ? record.Address : null,
            HasField(readableFieldKeys, "graduated_school") ? record.GraduatedSchool : null,
            HasField(readableFieldKeys, "education_level") ? record.EducationLevel : null,
            HasField(readableFieldKeys, "political_status") ? record.PoliticalStatus : null,
            HasField(readableFieldKeys, "office_phone") ? record.OfficePhone : null,
            HasField(readableFieldKeys, "emergency_contact") ? record.EmergencyContact : null,
            HasField(readableFieldKeys, "emergency_contact_relation")
                ? record.EmergencyContactRelation
                : null,
            HasField(readableFieldKeys, "emergency_contact_phone")
                ? record.EmergencyContactPhone
                : null,
            HasField(readableFieldKeys, "emergency_contact_address")
                ? record.EmergencyContactAddress
                : null,
            HasField(readableFieldKeys, "remark") ? record.Remark : null,
            record.Version);
    }

    public static HostUserProfileWriteRequest Merge(
        HostUserProfileRecord? existing,
        HostUserProfileWriteRequest patch,
        IReadOnlyCollection<string>? allowedFieldKeys = null)
    {
        var fieldKeys = NormalizeFieldKeys(patch.FieldKeys, allowedFieldKeys);
        if (fieldKeys.Count == 0)
        {
            fieldKeys = [];
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
            EmergencyContactRelation: Resolve(
                fieldKeys,
                "emergency_contact_relation",
                patch.EmergencyContactRelation,
                existing?.EmergencyContactRelation),
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
            EmergencyContactRelation = Normalize(profile.EmergencyContactRelation),
            EmergencyContactPhone = Normalize(profile.EmergencyContactPhone),
            EmergencyContactAddress = Normalize(profile.EmergencyContactAddress),
            Remark = Normalize(profile.Remark),
            Version = profile.Version ?? 0
        };

    public static IReadOnlyList<string> NormalizeFieldKeys(
        IReadOnlyList<string>? fieldKeys,
        IReadOnlyCollection<string>? allowedFieldKeys = null)
    {
        var normalized = fieldKeys?
            .Where(fieldKey => !string.IsNullOrWhiteSpace(fieldKey))
            .Select(fieldKey => fieldKey.Trim())
            .Where(fieldKey => EditableFieldKeys.Contains(fieldKey, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray()
            ?? [];
        if (allowedFieldKeys is null)
        {
            return normalized;
        }

        var allowed = allowedFieldKeys.ToHashSet(StringComparer.Ordinal);
        return normalized
            .Where(fieldKey => allowed.Contains(fieldKey))
            .ToArray();
    }

    public static IReadOnlyList<string> GetReadableFieldKeys(
        IReadOnlyCollection<string>? effectiveFieldKeys)
    {
        if (effectiveFieldKeys is null || effectiveFieldKeys.Count == 0)
        {
            return [];
        }

        var effective = effectiveFieldKeys.ToHashSet(StringComparer.Ordinal);
        return ProfileFieldKeys
            .Where(fieldKey => effective.Contains(fieldKey))
            .ToArray();
    }

    public static IReadOnlyList<string> GetWritableFieldKeys(
        IReadOnlyCollection<string>? effectiveFieldKeys) =>
        GetReadableFieldKeys(effectiveFieldKeys);

    public static bool HasReadableFields(
        IReadOnlyCollection<string>? effectiveFieldKeys) =>
        GetReadableFieldKeys(effectiveFieldKeys).Count > 0;

    public static IReadOnlyDictionary<string, string> GetReadableColumnMap(
        IReadOnlyCollection<string>? effectiveFieldKeys)
    {
        var readable = GetReadableFieldKeys(effectiveFieldKeys)
            .ToHashSet(StringComparer.Ordinal);
        return ProfileColumnMap
            .Where(pair => readable.Contains(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }

    private static readonly IReadOnlyDictionary<string, string> ProfileColumnMap =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["nickname"] = "Nickname",
            ["phone_number"] = "PhoneNumber",
            ["email"] = "Email",
            ["employee_number"] = "EmployeeNumber",
            ["gender"] = "Gender",
            ["join_date_utc"] = "JoinDateUtc",
            ["sort_order"] = "SortOrder",
            ["id_card_type"] = "IdCardType",
            ["id_card_number"] = "IdCardNumber",
            ["birth_date"] = "BirthDate",
            ["ethnicity"] = "Ethnicity",
            ["address"] = "Address",
            ["graduated_school"] = "GraduatedSchool",
            ["education_level"] = "EducationLevel",
            ["political_status"] = "PoliticalStatus",
            ["office_phone"] = "OfficePhone",
            ["emergency_contact"] = "EmergencyContact",
            ["emergency_contact_relation"] = "EmergencyContactRelation",
            ["emergency_contact_phone"] = "EmergencyContactPhone",
            ["emergency_contact_address"] = "EmergencyContactAddress",
            ["remark"] = "Remark",
        };

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

    private static bool HasField(
        IReadOnlyCollection<string> fieldKeys,
        string fieldKey) =>
        fieldKeys.Contains(fieldKey, StringComparer.Ordinal);
}
