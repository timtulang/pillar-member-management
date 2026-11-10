using System.ComponentModel.DataAnnotations;
using Pillar.Models;

namespace Pillar.Services;

public record ScheduledOccurrence(DateTime Start, DateTime End, DateTime Opens, DateTime Closes);

public static class EventSchedule
{
    public static IReadOnlyList<ScheduledOccurrence> Build(EventInput input)
    {
        var errors = new List<ValidationResult>();
        if (!Validator.TryValidateObject(input, new ValidationContext(input), errors, true))
            throw new InvalidOperationException(errors[0].ErrorMessage);
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(input.TimeZone); }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        { throw new InvalidOperationException("Enter a valid timezone, such as Asia/Manila."); }

        var firstStart = DateTime.SpecifyKind(input.StartsAt!.Value, DateTimeKind.Unspecified);
        var firstEnd = DateTime.SpecifyKind(input.EndsAt!.Value, DateTimeKind.Unspecified);
        var duration = firstEnd - firstStart;
        var count = input.Repeat == "none" ? 1 : input.Occurrences;
        var events = new List<ScheduledOccurrence>();
        for (var i = 0; i < count; i++)
        {
            try
            {
                // Calculate from the original date so January 31 -> February 28 -> March 31.
                var localStart = input.Repeat switch
                {
                    "daily" => firstStart.AddDays(i * input.Interval),
                    "weekly" => firstStart.AddDays(i * input.Interval * 7),
                    "monthly" => firstStart.AddMonths(i * input.Interval),
                    _ => firstStart
                };
                var localEnd = localStart + duration;
                if (zone.IsInvalidTime(localStart) || zone.IsInvalidTime(localEnd) || zone.IsAmbiguousTime(localStart) || zone.IsAmbiguousTime(localEnd))
                    throw new InvalidOperationException($"Occurrence {i + 1} ({localStart:dd MMM yyyy}) crosses an invalid or ambiguous daylight-saving time. Choose another time; no events have been created.");
                var start = TimeZoneInfo.ConvertTimeToUtc(localStart, zone);
                var end = TimeZoneInfo.ConvertTimeToUtc(localEnd, zone);
                if (end <= start) throw new InvalidOperationException("An occurrence ends before it starts after timezone conversion. Choose another time.");
                events.Add(new(start, end, start.AddMinutes(-input.MinutesBefore), end.AddMinutes(input.MinutesAfter)));
            }
            catch (ArgumentOutOfRangeException)
            { throw new InvalidOperationException("This schedule extends beyond the supported date range. Choose fewer occurrences or an earlier date."); }
        }
        return events;
    }
}
