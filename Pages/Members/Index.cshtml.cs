using Microsoft.AspNetCore.Mvc;
using Pillar.Data;
using Pillar.Models;
namespace Pillar.Pages.Members;
public class IndexModel(Db db) : AppPage
{
    [BindProperty(SupportsGet=true)] public string? Search { get; set; }
    [BindProperty(SupportsGet=true)] public string? Status { get; set; }
    [BindProperty(SupportsGet=true)] public int PageNumber { get; set; } = 1;
    [BindProperty(SupportsGet=true)] public bool Archived { get; set; }
    public List<Row> Members { get; set; } = [];
    public long Total { get; set; }
    public async Task OnGetAsync()
    {
        PageNumber = Math.Clamp(PageNumber,1,100000);
        Search = Search?[..Math.Min(Search.Length,150)];
        if (!MemberInput.Statuses.Contains(Status ?? "")) Status = null;
        const string where = "WHERE (archived_at IS NOT NULL)=@Archived AND (@Status IS NULL OR congregation_status::text=@Status) AND (@search IS NULL OR first_name || ' ' || surname ILIKE @search OR member_code ILIKE @search)";
        var args = new { Archived, Status, search = string.IsNullOrWhiteSpace(Search) ? null : "%" + Search + "%", offset=(PageNumber-1)*30 };
        Total=(await db.One("SELECT count(*) total FROM pillar.people " + where,args))!.N("total");
        Members=await db.Read("SELECT id,member_code,first_name,surname,congregation_status::text,phone,email FROM pillar.people " + where + " ORDER BY surname,first_name,id LIMIT 30 OFFSET @offset",args);
    }
}
