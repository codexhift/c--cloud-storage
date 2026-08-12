namespace CloudStorage.Models;

public class FileItem
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string OwnerId { get; set; } = string.Empty;
    public ApplicationUser? Owner { get; set; }

    public Guid FolderId { get; set; }
    public Folder? Folder { get; set; }

    public long Size { get; set; }

    public string ContentType { get; set; } = string.Empty;

    public string? StorageKey { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public bool IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }
}
