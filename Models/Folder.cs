namespace CloudStorage.Models;

public class Folder
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string OwnerId { get; set; } = string.Empty;
    public ApplicationUser? Owner { get; set; }

    public Guid? ParentFolderId { get; set; }
    public Folder? ParentFolder { get; set; }

    public ICollection<Folder> SubFolders { get; set; } = new List<Folder>();
    public ICollection<FileItem> FileItems { get; set; } = new List<FileItem>();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
