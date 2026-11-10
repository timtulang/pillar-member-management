using Microsoft.AspNetCore.Mvc;
using Pillar.Data;
using Pillar.Services;
namespace Pillar.Pages.Events;
public class DetailsModel(Db db,AttendanceService attendance):AppPage
{
    [BindProperty(SupportsGet=true)]public Guid Id{get;set;}
    public Row Event{get;set;}=null!;public List<Row> Attendees{get;set;}=[];
    public async Task<IActionResult> OnGetAsync()
    {
        var e=await db.One("SELECT * FROM pillar.event_occurrences WHERE id=@Id",new{Id});if(e==null)return NotFound();Event=e;
        Attendees=await db.Read("SELECT a.id,a.checked_in_at,a.method::text,a.voided_at,p.first_name,p.surname,p.member_code FROM pillar.attendance_records a JOIN pillar.people p ON p.id=a.person_id WHERE a.event_occurrence_id=@Id ORDER BY a.checked_in_at DESC",new{Id});return Page();
    }
    public async Task<IActionResult> OnPostScanAsync(string? token)
    {
        if(!ModelState.IsValid||string.IsNullOrWhiteSpace(token))return BadRequest(new{message="Scan a valid Pillar ID."});
        try{return new JsonResult(await attendance.CheckIn(Actor,Id,token,null,null));}
        catch(InvalidOperationException e){return BadRequest(new{message=e.Message});}
    }
    public Task<IActionResult> OnPostManualAsync(string? memberCode,string? reason)=>Change(async()=>
    {
        if(string.IsNullOrWhiteSpace(memberCode)||memberCode.Length>24)throw new InvalidOperationException("Enter a member ID.");
        var p=await db.One("SELECT id FROM pillar.people WHERE member_code=@code",new{code=memberCode.Trim().ToUpperInvariant()});if(p==null)throw new InvalidOperationException("No person was found with that member ID.");
        var result=await attendance.CheckIn(Actor,Id,null,p.G("id"),reason);Notice=$"{result.Name}: {result.Message}";
    },"Details",new{Id},"Attendance recorded. Duplicate check-ins are counted once.");
    public async Task<IActionResult> OnPostCancelAsync()
    {
        if(!(await CanManage()).Succeeded)return Forbid();
        return await Change(async()=>{await db.Execute(Actor,"event.cancelled","event_occurrences",Id,"UPDATE pillar.event_occurrences SET cancelled_at=now() WHERE id=@Id AND cancelled_at IS NULL",new{Id});},"Details",new{Id},"Event cancelled. Check-in is closed.");
    }
    public async Task<IActionResult> OnPostCorrectAsync(Guid attendanceId,bool restore,string? reason)
    {
        if(!(await CanManage()).Succeeded)return Forbid();
        return await Change(async()=>
        {
            if(string.IsNullOrWhiteSpace(reason)||reason.Length>300)throw new InvalidOperationException("Enter a correction reason of up to 300 characters.");
            await db.Execute(Actor,restore?"attendance.reinstated":"attendance.voided","attendance_records",attendanceId,"""
                UPDATE pillar.attendance_records SET voided_at=CASE WHEN @restore THEN NULL ELSE now() END,
                voided_by_user_id=CASE WHEN @restore THEN NULL ELSE @actor END,void_reason=CASE WHEN @restore THEN NULL ELSE @reason END,
                last_correction_reason=@reason WHERE id=@attendanceId AND event_occurrence_id=@Id
                """,new{restore,actor=Actor,reason,attendanceId,Id},["voided_at","last_correction_reason"]);
        },"Details",new{Id});
    }
}
