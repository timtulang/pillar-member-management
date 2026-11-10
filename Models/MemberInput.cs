using System.ComponentModel.DataAnnotations;

namespace Pillar.Models;

public class MemberInput : IValidatableObject
{
    [Required, StringLength(150), Display(Name = "First name")] public string FirstName { get; set; } = "";
    [StringLength(150), Display(Name = "Middle name")] public string? MiddleName { get; set; }
    [Required, StringLength(150), Display(Name = "Surname")] public string Surname { get; set; } = "";
    [StringLength(30)] public string? Suffix { get; set; }
    [StringLength(150), Display(Name = "Preferred name")] public string? PreferredName { get; set; }
    [StringLength(50)] public string? Gender { get; set; }
    [DataType(DataType.Date), Display(Name = "Birthday")] public DateTime? BirthDate { get; set; }
    [StringLength(2000), Display(Name = "Residential address")] public string? Address { get; set; }
    [StringLength(40)] public string? Phone { get; set; }
    [EmailAddress, StringLength(254)] public string? Email { get; set; }
    [Required, Display(Name = "Church status")] public string Status { get; set; } = "visitor";
    [DataType(DataType.Date), Display(Name = "Membership date")] public DateTime? MembershipDate { get; set; }
    [Required, Display(Name = "Reported civil status")] public string CivilStatus { get; set; } = "unknown";
    [Range(0, 100), Display(Name = "Reported number of children")] public int? ChildCount { get; set; }
    [StringLength(300), Display(Name = "Invited by (reported name)")] public string? Inviter { get; set; }
    [DataType(DataType.Date), Display(Name = "Date of death, if applicable")] public DateTime? DeceasedOn { get; set; }
    public long Version { get; set; }
    public static string[] Statuses => ["visitor", "regular_attendee", "member", "inactive", "transferred"];
    public static string[] CivilStatuses => ["unknown", "single", "married", "separated", "divorced", "widowed", "other"];
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (string.IsNullOrWhiteSpace(FirstName) || string.IsNullOrWhiteSpace(Surname)) yield return new("Names cannot be blank.");
        if (!Statuses.Contains(Status)) yield return new("Choose a valid church status.", [nameof(Status)]);
        if (!CivilStatuses.Contains(CivilStatus)) yield return new("Choose a valid civil status.", [nameof(CivilStatus)]);
        if (BirthDate > DateTime.UtcNow.Date) yield return new("Birthday cannot be in the future.", [nameof(BirthDate)]);
        if (DeceasedOn < BirthDate || DeceasedOn > DateTime.UtcNow.Date) yield return new("Check the date of death.", [nameof(DeceasedOn)]);
    }
}
