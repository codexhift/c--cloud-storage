using Microsoft.AspNetCore.Identity;

namespace CloudStorage.Models;

public class ApplicationUser : IdentityUser
{
    public string DisplayName { get; set; } = string.Empty;

    public ICollection<Folder> Folders { get; set; } = new List<Folder>();
    public ICollection<FileItem> FileItems { get; set; } = new List<FileItem>();
}