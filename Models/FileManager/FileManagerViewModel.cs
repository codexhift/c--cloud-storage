using CloudStorage.Models;

namespace CloudStorage.Models.FileManager;

public class FileManagerViewModel
{
    public Folder CurrentFolder { get; set; } = null!;
    public List<Folder> Breadcrumbs { get; set; } = new();
    public List<Folder> SubFolders { get; set; } = new();
    public List<FileItem> FileItems { get; set; } = new();
    public List<Folder> AllUserFolders { get; set; } = new();
    public string? ErrorMessage { get; set; }
    public string? SuccessMessage { get; set; }
}
