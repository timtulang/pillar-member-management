using System.ComponentModel.DataAnnotations;

namespace Pillar.Models;

public class EventInput : IValidatableObject
{
    [Required, StringLength(200), Display(Name = "Event name")] public string Title { get; set; } = "";
    [StringLength(250)] public string? Location { get; set; }
    [Required, Display(Name = "Starts at")] public DateTime? StartsAt { get; set; }
    [Required, Display(Name = "Ends at")] public DateTime? EndsAt { get; set; }
    [Required, StringLength(100), Display(Name = "Timezone")] public string TimeZone { get; set; } = "Asia/Manila";
    [Range(0, 1440), Display(Name = "Open check-in (minutes before start)")] public int MinutesBefore { get; set; } = 60;
    [Range(0, 1440), Display(Name = "Close check-in (minutes after end)")] public int MinutesAfter { get; set; } = 30;
    [Required, Display(Name = "Repeat")] public string Repeat { get; set; } = "none";
    [Display(Name = "Repeat every")] public int Interval { get; set; } = 1;
    [Display(Name = "Number of occurrences")] public int Occurrences { get; set; } = 12;
    public static string[] RepeatOptions => ["none", "daily", "weekly", "monthly"];

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (StartsAt?.Year < 2000) yield return new("Choose a start date in 2000 or later.", [nameof(StartsAt)]);
        if (EndsAt <= StartsAt) yield return new("The end must be after the start.", [nameof(EndsAt)]);
        if (!RepeatOptions.Contains(Repeat)) yield return new("Choose a valid repeat option.", [nameof(Repeat)]);
        if (Repeat != "none")
        {
            if (Interval is < 1 or > 12) yield return new("Repeat intervals must be between 1 and 12.", [nameof(Interval)]);
            if (Occurrences is < 2 or > 52) yield return new("Choose between 2 and 52 occurrences, including the first event.", [nameof(Occurrences)]);
        }
    }
}
