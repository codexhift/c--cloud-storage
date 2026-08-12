using Microsoft.AspNetCore.Identity;

namespace CloudStorage.Models;

public class ApplicationUser : IdentityUser
{
    public string DisplayName { get; set; } = string.Empty;
}