namespace Full.NET.Modules.Identity.Persistence;

/// <summary>Host user profile row mapped to fn_identity_user_profile.</summary>
internal sealed class HostUserProfileRecord
{
    public Guid UserId { get; set; }

    public string? Nickname { get; set; }

    public string? PhoneNumber { get; set; }

    public string? Email { get; set; }

    public string? EmployeeNumber { get; set; }

    public string? Gender { get; set; }

    public DateTime? JoinDateUtc { get; set; }

    public int SortOrder { get; set; }

    public string? IdCardType { get; set; }

    public string? IdCardNumber { get; set; }

    public DateTime? BirthDate { get; set; }

    public string? Ethnicity { get; set; }

    public string? Address { get; set; }

    public string? GraduatedSchool { get; set; }

    public string? EducationLevel { get; set; }

    public string? PoliticalStatus { get; set; }

    public string? OfficePhone { get; set; }

    public string? EmergencyContact { get; set; }

    public string? EmergencyContactRelation { get; set; }

    public string? EmergencyContactPhone { get; set; }

    public string? EmergencyContactAddress { get; set; }

    public string? Remark { get; set; }

    public int Version { get; set; }
}
