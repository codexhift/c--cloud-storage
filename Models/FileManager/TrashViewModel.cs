using CloudStorage.Models;

namespace CloudStorage.Models.FileManager;

public class TrashViewModel
{
    public List<Folder> TrashedFolders { get; set; } = new();
    public List<FileItem> TrashedFiles { get; set; } = new();
    public string? ErrorMessage { get; set; }
    public string? SuccessMessage { get; set; }
}
