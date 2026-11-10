using Dapper;
using Pillar.Data;
using Pillar.Models;

namespace Pillar.Services;

public sealed class MemberService(Db db)
{
    public Task<Guid> Save(Guid actor, Guid? existing, MemberInput m)
    {
        var id = existing ?? Guid.NewGuid();
        return db.Write(actor, existing == null ? "member.created" : "member.updated", "people", id, async (c, t) =>
        {
            var args = new { id, m.FirstName, m.MiddleName, m.Surname, m.Suffix, m.PreferredName, m.Gender, m.BirthDate, m.Address, m.Phone, m.Email, m.Status, m.MembershipDate, m.CivilStatus, m.ChildCount, m.Inviter, m.DeceasedOn, m.Version };
            var count = existing == null
                ? await c.ExecuteAsync("""
                    INSERT INTO pillar.people(id,first_name,middle_name,surname,suffix,preferred_name,gender,birth_date,
                    residential_address,phone,email,congregation_status,membership_date,reported_civil_status,
                    civil_status_reported_on,reported_child_count,inviter_name_reported,registered_at,deceased_on)
                    VALUES (@id,trim(@FirstName),@MiddleName,trim(@Surname),@Suffix,@PreferredName,@Gender,@BirthDate,
                    @Address,@Phone,@Email,CAST(@Status AS pillar.congregation_status),@MembershipDate,
                    CAST(@CivilStatus AS pillar.civil_status),CURRENT_DATE,@ChildCount,@Inviter,now(),@DeceasedOn)
                    """, args, t)
                : await c.ExecuteAsync("""
                    UPDATE pillar.people SET first_name=trim(@FirstName),middle_name=@MiddleName,surname=trim(@Surname),
                    suffix=@Suffix,preferred_name=@PreferredName,gender=@Gender,birth_date=@BirthDate,
                    residential_address=@Address,phone=@Phone,email=@Email,congregation_status=CAST(@Status AS pillar.congregation_status),
                    membership_date=@MembershipDate,civil_status_reported_on=CASE WHEN reported_civil_status::text<>@CivilStatus THEN CURRENT_DATE ELSE civil_status_reported_on END,
                    reported_civil_status=CAST(@CivilStatus AS pillar.civil_status),reported_child_count=@ChildCount,
                    inviter_name_reported=@Inviter,deceased_on=@DeceasedOn WHERE id=@id AND version=@Version
                    """, args, t);
            if (count != 1) throw new InvalidOperationException("This profile changed while you were editing. Reload it before saving again.");
            return id;
        }, ["profile"]);
    }

    public Task<int> MoveHousehold(Guid actor, Guid person, Guid household, string role, DateTime effective)
    {
        if (!new[] { "adult", "child", "dependent", "other" }.Contains(role)) throw new InvalidOperationException("Choose a valid household role.");
        return db.Write(actor, "household.moved", "people", person, async (c, t) =>
        {
            var active = await c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM pillar.households WHERE id=@household AND archived_at IS NULL)", new { household }, t);
            if (!active) throw new InvalidOperationException("Choose an active household.");
            // A same-day change is a correction, not a zero-length historical period.
            await c.ExecuteAsync("DELETE FROM pillar.household_memberships WHERE person_id=@person AND is_primary AND valid_from=@effective AND valid_to IS NULL", new { person, effective }, t);
            await c.ExecuteAsync("UPDATE pillar.household_memberships SET valid_to=@effective WHERE person_id=@person AND is_primary AND valid_to IS NULL", new { person, effective }, t);
            return await c.ExecuteAsync("""
                INSERT INTO pillar.household_memberships(household_id,person_id,role,is_primary,valid_from)
                VALUES (@household,@person,CAST(@role AS pillar.household_role),true,@effective)
                """, new { household, person, role, effective }, t);
        });
    }
}
