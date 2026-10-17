using Microsoft.AspNetCore.Identity;

namespace TimeSlices.Web.Data;

public class ApplicationUser : IdentityUser
{
    /// <summary>Закреплённый экзаменатор (brief 9.3). На 0→1 не используется.</summary>
    public string? AssignedExaminerId { get; set; }
}
