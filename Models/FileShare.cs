namespace CloudStorage.Models;

public enum FileSharePermission
{
    View = 1,
    Download = 2
}

public class FileShare
{
    public Guid Id { get; set; }

    public Guid FileItemId { get; set; }
    public FileItem FileItem { get; set; } = null!;

    public string SharedWithUserId { get; set; } = string.Empty;
    public ApplicationUser SharedWithUser { get; set; } = null!;

    public FileSharePermission Permission { get; set; } = FileSharePermission.View;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
