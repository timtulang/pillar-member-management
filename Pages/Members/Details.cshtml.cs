using Dapper;
using Microsoft.AspNetCore.Mvc;
using Pillar.Data;
using Pillar.Services;

namespace Pillar.Pages.Members;
public class DetailsModel(Db db, MemberService members) : AppPage
{
    [BindProperty(SupportsGet=true)] public Guid Id { get; set; }
    public Row Person { get; set; } = null!;
    public List<Row> Households { get; set; }=[];
    public List<Row> History { get; set; }=[];
    public List<Row> Relatives { get; set; }=[];
    public List<Row> Marriages { get; set; }=[];
    public List<Row> Ministries { get; set; }=[];
    public List<Row> MinistryOptions { get; set; }=[];
    public List<Row> Roles { get; set; }=[];
    public List<Row> Programs { get; set; }=[];
    public List<Row> Enrollments { get; set; }=[];
    public List<Row> Baptisms { get; set; }=[];
    public List<Row> Cards { get; set; }=[];
    public List<Row> Preferences { get; set; }=[];
    public List<Row> Attendance { get; set; }=[];
    public async Task<IActionResult> OnGetAsync()
    {
        var p=await db.One("SELECT *,congregation_status::text status,reported_civil_status::text civil FROM pillar.people WHERE id=@Id",new{Id});
        if(p==null)return NotFound();Person=p;
        Households=await db.Read("SELECT id,display_name FROM pillar.households WHERE archived_at IS NULL ORDER BY display_name");
        History=await db.Read("SELECT h.display_name,m.* FROM pillar.household_memberships m JOIN pillar.households h ON h.id=m.household_id WHERE m.person_id=@Id ORDER BY m.valid_from DESC NULLS LAST",new{Id});
        Relatives=await db.Read("""
            SELECT r.id,r.kind::text,r.valid_from,r.valid_to,p.id person_id,p.first_name,p.surname,
            CASE WHEN r.parent_id=@Id THEN 'Child' ELSE 'Parent' END direction FROM pillar.parent_child_relationships r
            JOIN pillar.people p ON p.id=CASE WHEN r.parent_id=@Id THEN r.child_id ELSE r.parent_id END
            WHERE r.parent_id=@Id OR r.child_id=@Id ORDER BY direction,p.surname
            """,new{Id});
        Marriages=await db.Read("""
            SELECT m.*,p.id person_id,p.first_name,p.surname FROM pillar.marriages m JOIN pillar.people p
            ON p.id=CASE WHEN m.spouse_a_id=@Id THEN m.spouse_b_id ELSE m.spouse_a_id END
            WHERE m.spouse_a_id=@Id OR m.spouse_b_id=@Id ORDER BY m.married_on DESC NULLS LAST
            """,new{Id});
        Ministries=await db.Read("SELECT a.*,m.name,r.name role_name FROM pillar.ministry_assignments a JOIN pillar.ministries m ON m.id=a.ministry_id LEFT JOIN pillar.ministry_roles r ON r.id=a.ministry_role_id WHERE a.person_id=@Id ORDER BY a.valid_to NULLS FIRST,m.name",new{Id});
        MinistryOptions=await db.Read("SELECT id,name FROM pillar.ministries WHERE is_active ORDER BY name");
        Roles=await db.Read("SELECT id,name FROM pillar.ministry_roles ORDER BY name");
        Programs=await db.Read("SELECT id,name FROM pillar.discipleship_programs WHERE is_active ORDER BY name");
        Enrollments=await db.Read("SELECT e.*,p.name,e.status::text progress FROM pillar.discipleship_enrollments e JOIN pillar.discipleship_programs p ON p.id=e.program_id WHERE e.person_id=@Id",new{Id});
        Baptisms=await db.Read("SELECT *,location::text place FROM pillar.baptisms WHERE person_id=@Id ORDER BY baptized_on NULLS LAST",new{Id});
        Cards=await db.Read("SELECT id,issued_at,expires_at,revoked_at FROM pillar.member_cards WHERE person_id=@Id ORDER BY issued_at DESC LIMIT 5",new{Id});
        Preferences=await db.Read("SELECT m.id,m.name FROM pillar.ministry_preferences p JOIN pillar.ministries m ON m.id=p.ministry_id WHERE p.person_id=@Id",new{Id});
        Attendance=await db.Read("SELECT e.title,e.starts_at,a.voided_at FROM pillar.attendance_records a JOIN pillar.event_occurrences e ON e.id=a.event_occurrence_id WHERE a.person_id=@Id ORDER BY e.starts_at DESC LIMIT 10",new{Id});
        return Page();
    }
    public Task<IActionResult> OnPostArchiveAsync(bool archive) => Change(async()=>
    {
        await db.Write(Actor,archive?"member.archived":"member.restored","people",Id,async(c,t)=>
        {
            var count=await c.ExecuteAsync("UPDATE pillar.people SET archived_at=CASE WHEN @archive THEN now() ELSE NULL END WHERE id=@Id",new{archive,Id},t);
            if(archive) await c.ExecuteAsync("UPDATE pillar.member_cards SET revoked_at=now(),revoked_by_user_id=@actor WHERE person_id=@Id AND revoked_at IS NULL",new{actor=Actor,Id},t);
            return count;
        });
    },"Details",new{Id});

    public Task<IActionResult> OnPostMoveAsync(Guid householdId,string role,DateTime effective) => Change(async()=>
    { RequireDate(effective); await members.MoveHousehold(Actor,Id,householdId,role,effective); },"Details",new{Id},"Household history updated.");

    public Task<IActionResult> OnPostParentAsync(string relativeCode,string direction,string kind,DateTime? since) => Change(async()=>
    {
        if(!new[]{"biological","adoptive","step","foster"}.Contains(kind)||!new[]{"parent","child"}.Contains(direction))throw new InvalidOperationException("Choose a relationship type and direction.");
        var other=await FindPerson(relativeCode);
        await db.Execute(Actor,"parentage.added","people",Id,"INSERT INTO pillar.parent_child_relationships(parent_id,child_id,kind,valid_from) VALUES(@parent,@child,CAST(@kind AS pillar.parent_relationship_kind),@since)",new{parent=direction=="parent"?other:Id,child=direction=="child"?other:Id,kind,since});
    },"Details",new{Id});

    public Task<IActionResult> OnPostEndParentAsync(Guid relationshipId,DateTime endedOn) => Change(async()=>
    {RequireDate(endedOn);await db.Execute(Actor,"parentage.ended","people",Id,"UPDATE pillar.parent_child_relationships SET valid_to=@endedOn WHERE id=@relationshipId AND (parent_id=@Id OR child_id=@Id) AND valid_to IS NULL",new{Id,relationshipId,endedOn});},"Details",new{Id});

    public Task<IActionResult> OnPostMarriageAsync(string spouseCode,DateTime? marriedOn) => Change(async()=>
    {
        var other=await FindPerson(spouseCode);
        await db.Execute(Actor,"marriage.added","people",Id,"INSERT INTO pillar.marriages(spouse_a_id,spouse_b_id,married_on) VALUES(LEAST(@Id,@other),GREATEST(@Id,@other),@marriedOn)",new{Id,other,marriedOn});
    },"Details",new{Id},"Marriage recorded. Parent links and household memberships are preserved.");

    public Task<IActionResult> OnPostEndMarriageAsync(Guid marriageId,DateTime endedOn,string reason) => Change(async()=>
    {
        RequireDate(endedOn);if(!new[]{"divorce","annulment","spouse_death","other"}.Contains(reason))throw new InvalidOperationException("Choose an end reason.");
        await db.Execute(Actor,"marriage.ended","people",Id,"UPDATE pillar.marriages SET ended_on=@endedOn,end_reason=CAST(@reason AS pillar.marriage_end_reason) WHERE id=@marriageId AND (spouse_a_id=@Id OR spouse_b_id=@Id) AND ended_on IS NULL",new{Id,marriageId,endedOn,reason});
    },"Details",new{Id});

    public Task<IActionResult> OnPostMinistryAsync(Guid ministryId,Guid? roleId,bool main,DateTime? since) => Change(async()=>
    {await db.Execute(Actor,"ministry.assigned","people",Id,"INSERT INTO pillar.ministry_assignments(person_id,ministry_id,ministry_role_id,is_main,valid_from) VALUES(@Id,@ministryId,@roleId,@main,@since)",new{Id,ministryId,roleId,main,since});},"Details",new{Id});

    public Task<IActionResult> OnPostEndMinistryAsync(Guid assignmentId,DateTime endedOn) => Change(async()=>
    {RequireDate(endedOn);await db.Execute(Actor,"ministry.ended","people",Id,"UPDATE pillar.ministry_assignments SET valid_to=@endedOn WHERE id=@assignmentId AND person_id=@Id AND valid_to IS NULL",new{Id,assignmentId,endedOn});},"Details",new{Id});

    public Task<IActionResult> OnPostPreferenceAsync(Guid ministryId,bool remove) => Change(async()=>
    {await db.Execute(Actor,"ministry.preference_changed","people",Id,remove?"DELETE FROM pillar.ministry_preferences WHERE person_id=@Id AND ministry_id=@ministryId":"INSERT INTO pillar.ministry_preferences(person_id,ministry_id) VALUES(@Id,@ministryId) ON CONFLICT DO NOTHING",new{Id,ministryId});},"Details",new{Id});

    public Task<IActionResult> OnPostProgressAsync(Guid programId,string status,DateTime? startedOn,DateTime? completedOn) => Change(async()=>
    {
        if(!new[]{"reported_participation","in_progress","completed","paused"}.Contains(status))throw new InvalidOperationException("Choose a valid progress status.");
        if(completedOn<startedOn)throw new InvalidOperationException("Completion date cannot be before the start date.");
        if(completedOn>DateTime.UtcNow.Date)throw new InvalidOperationException("Completion date cannot be in the future.");
        if(completedOn!=null && status!="completed")throw new InvalidOperationException("Choose Completed when recording a completion date.");
        await db.Execute(Actor,"discipleship.updated","people",Id,"""
            INSERT INTO pillar.discipleship_enrollments(person_id,program_id,status,started_on,completed_on)
            VALUES(@Id,@programId,CAST(@status AS pillar.discipleship_status),@startedOn,@completedOn)
            ON CONFLICT(person_id,program_id) DO UPDATE SET status=EXCLUDED.status,started_on=EXCLUDED.started_on,completed_on=EXCLUDED.completed_on
            """,new{Id,programId,status,startedOn,completedOn});
    },"Details",new{Id});

    public Task<IActionResult> OnPostBaptismAsync(DateTime? baptizedOn,string location,string? churchName) => Change(async()=>
    {
        if(!new[]{"pillar","elsewhere","unknown"}.Contains(location)||churchName?.Length>200)throw new InvalidOperationException("Check the baptism details.");
        if(baptizedOn>DateTime.UtcNow.Date)throw new InvalidOperationException("Baptism date cannot be in the future.");
        await db.Execute(Actor,"baptism.recorded","people",Id,"INSERT INTO pillar.baptisms(person_id,baptized_on,location,church_name) VALUES(@Id,@baptizedOn,CAST(@location AS pillar.baptism_location),@churchName)",new{Id,baptizedOn,location,churchName});
    },"Details",new{Id});

    public Task<IActionResult> OnPostRevokeAsync(Guid cardId) => Change(async()=>
    {await db.Execute(Actor,"card.revoked","people",Id,"UPDATE pillar.member_cards SET revoked_at=now(),revoked_by_user_id=@actor WHERE id=@cardId AND person_id=@Id AND revoked_at IS NULL",new{actor=Actor,Id,cardId});},"Details",new{Id});

    private async Task<Guid> FindPerson(string? code)
    {
        if(string.IsNullOrWhiteSpace(code)||code.Length>24)throw new InvalidOperationException("Enter the person's member ID from the directory.");
        var row=await db.One("SELECT id FROM pillar.people WHERE member_code=@code AND archived_at IS NULL",new{code=code.Trim().ToUpperInvariant()});
        if(row==null)throw new InvalidOperationException("No active person was found with that member ID.");
        return row.G("id");
    }
    private static void RequireDate(DateTime value) { if(value.Year<1900)throw new InvalidOperationException("Enter a valid effective date."); }
}
