using Pillar.Data;

namespace Pillar.Models;

public record DemographicBucket(string Label, long Count);
public record DemographicGroup(string Title, IReadOnlyList<DemographicBucket> Buckets)
{
    public long Total => Buckets.Sum(b => b.Count);
    public string Percentage(long count) => Total == 0 ? "0%" : ((decimal)count * 100 / Total).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "%";
}

public static class DemographicSummary
{
    public const string Query = """
        WITH population AS (
            SELECT *, (now() AT TIME ZONE @zone)::date today
            FROM pillar.people WHERE archived_at IS NULL AND deceased_on IS NULL
        ), classified AS (
            SELECT congregation_status::text church_status, reported_civil_status::text civil_status,
                CASE
                    WHEN birth_date IS NULL OR birth_date>today THEN 'not_provided'
                    WHEN age(today,birth_date)<interval '13 years' THEN '0_12'
                    WHEN age(today,birth_date)<interval '18 years' THEN '13_17'
                    WHEN age(today,birth_date)<interval '25 years' THEN '18_24'
                    WHEN age(today,birth_date)<interval '35 years' THEN '25_34'
                    WHEN age(today,birth_date)<interval '45 years' THEN '35_44'
                    WHEN age(today,birth_date)<interval '55 years' THEN '45_54'
                    WHEN age(today,birth_date)<interval '65 years' THEN '55_64'
                    ELSE '65_plus' END age_group,
                CASE
                    WHEN nullif(trim(gender),'') IS NULL THEN 'not_provided'
                    WHEN replace(replace(lower(trim(gender)),'-','_'),' ','_') IN ('male','female','non_binary','other','prefer_not_to_say')
                        THEN replace(replace(lower(trim(gender)),'-','_'),' ','_')
                    ELSE 'other_recorded' END gender_group
            FROM population
        )
        SELECT bucket.dimension,bucket.category,count(*) people
        FROM classified CROSS JOIN LATERAL (VALUES
            ('age',age_group),('gender',gender_group),('status',church_status),('civil',civil_status)
        ) AS bucket(dimension,category)
        GROUP BY bucket.dimension,bucket.category
        """;

    public static IReadOnlyList<DemographicGroup> Build(IEnumerable<Row> rows)
    {
        var counts = rows.ToDictionary(r => (r.S("dimension"), r.S("category")), r => r.N("people"));
        DemographicGroup Group(string title, string dimension, IEnumerable<(string Key, string Label)> options) =>
            new(title, options.Select(o => new DemographicBucket(o.Label, counts.GetValueOrDefault((dimension, o.Key)))).ToList());
        var gender = new List<(string, string)> { ("female", "Female"), ("male", "Male"), ("non_binary", "Non-binary"), ("other", "Other"), ("prefer_not_to_say", "Prefer not to say"), ("not_provided", "Not provided") };
        if (counts.GetValueOrDefault(("gender", "other_recorded")) > 0) gender.Add(("other_recorded", "Other recorded values"));
        return [
            Group("Age groups", "age", [("0_12", "Children · 0–12"), ("13_17", "Teens · 13–17"), ("18_24", "18–24"), ("25_34", "25–34"), ("35_44", "35–44"), ("45_54", "45–54"), ("55_64", "55–64"), ("65_plus", "65+"), ("not_provided", "Not provided")]),
            Group("Gender", "gender", gender),
            Group("Church status", "status", MemberInput.Statuses.Select(s => (s, DisplayLabels.Text(s)))),
            Group("Reported civil status", "civil", MemberInput.CivilStatuses.Select(s => (s, DisplayLabels.Text(s))))
        ];
    }
}
