using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.AspNetCore.WebUtilities;
using Pillar.Data;

namespace Pillar.Services;

public record CheckInResult(string Message, string Name, bool Duplicate);
public sealed partial class AttendanceService(Db db, IConfiguration config)
{
    [GeneratedRegex("^PF1:[A-Za-z0-9_-]{43}$", RegexOptions.CultureInvariant)] private static partial Regex TokenPattern();

    public Task<string> Issue(Guid actor, Guid person) => db.Write(actor, "card.issued", "people", person, async (c, t) =>
    {
        var eligible = await c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM pillar.people WHERE id=@person AND archived_at IS NULL AND deceased_on IS NULL)", new { person }, t);
        if (!eligible) throw new InvalidOperationException("This profile cannot receive a card.");
        await c.ExecuteAsync("UPDATE pillar.member_cards SET revoked_at=now(),revoked_by_user_id=@actor WHERE person_id=@person AND revoked_at IS NULL", new { actor, person }, t);
        var token = RandomNumberGenerator.GetBytes(32);
        var days = Math.Clamp(config.GetValue("Pillar:CardValidityDays", 365), 1, 3650);
        await c.ExecuteAsync("""
            INSERT INTO pillar.member_cards(person_id,token_digest,expires_at,issued_by_user_id)
            VALUES (@person,@digest,now()+make_interval(days=>@days),@actor)
            """, new { person, digest = SHA256.HashData(token), days, actor }, t);
        return "PF1:" + WebEncoders.Base64UrlEncode(token);
    });

    public Task<CheckInResult> CheckIn(Guid actor, Guid eventId, string? token, Guid? manualPerson, string? reason)
    {
        byte[]? digest = null;
        if (token != null)
        {
            if (!TokenPattern().IsMatch(token)) throw new InvalidOperationException("This is not a valid Pillar ID.");
            var bytes = WebEncoders.Base64UrlDecode(token[4..]);
            if (bytes.Length != 32 || WebEncoders.Base64UrlEncode(bytes) != token[4..]) throw new InvalidOperationException("This is not a valid Pillar ID.");
            digest = SHA256.HashData(bytes);
        }
        else if (manualPerson == null || string.IsNullOrWhiteSpace(reason) || reason.Length > 300) throw new InvalidOperationException("Choose a person and enter a reason for manual check-in.");

        return db.Write(actor, "attendance.checked_in", "event_occurrences", eventId, async (c, t) =>
        {
            var allowed = await c.ExecuteScalarAsync<bool>("""
                SELECT EXISTS(SELECT 1 FROM pillar.event_occurrences WHERE id=@eventId AND cancelled_at IS NULL
                AND now()>=check_in_opens_at AND now()<check_in_closes_at)
                """, new { eventId }, t);
            if (!allowed) throw new InvalidOperationException("Check-in is closed or this event has been cancelled.");
            Guid person; Guid? card = null;
            if (digest != null)
            {
                var found = await c.QuerySingleOrDefaultAsync("SELECT id,person_id FROM pillar.member_cards WHERE token_digest=@digest AND revoked_at IS NULL AND expires_at>now()", new { digest }, t);
                if (found == null) throw new InvalidOperationException("This card is invalid, expired, or revoked.");
                person = found.person_id; card = found.id;
            }
            else person = manualPerson!.Value;
            var name = await c.ExecuteScalarAsync<string>("SELECT first_name || ' ' || surname FROM pillar.people WHERE id=@person AND archived_at IS NULL AND deceased_on IS NULL", new { person }, t);
            if (name == null) throw new InvalidOperationException("This member is not eligible for check-in.");
            var inserted = await c.ExecuteAsync("""
                INSERT INTO pillar.attendance_records(event_occurrence_id,person_id,method,member_card_id,recorded_by_user_id,manual_reason)
                VALUES (@eventId,@person,CAST(@method AS pillar.attendance_method),@card,@actor,@reason)
                ON CONFLICT(event_occurrence_id,person_id) DO NOTHING
                """, new { eventId, person, method = digest != null ? "qr_scan" : "manual", card, actor, reason = digest != null ? null : reason!.Trim() }, t);
            var voided = await c.ExecuteScalarAsync<bool>("SELECT voided_at IS NOT NULL FROM pillar.attendance_records WHERE event_occurrence_id=@eventId AND person_id=@person", new { eventId, person }, t);
            if (voided) throw new InvalidOperationException("This attendance was voided. Ask a manager to reinstate it.");
            return new CheckInResult(inserted == 1 ? "Checked in successfully" : "Already checked in", name, inserted == 0);
        });
    }
}
