using CloudStorage.Models;

namespace CloudStorage.Models.FileManager;

public class ManageSharesViewModel
{
    public FileItem FileItem { get; set; } = null!;
    public List<FileShare> Shares { get; set; } = new();
    public string? ErrorMessage { get; set; }
    public string? SuccessMessage { get; set; }
}

public class SharedWithMeViewModel
{
    public List<FileShare> SharedFiles { get; set; } = new();
    public string? ErrorMessage { get; set; }
    public string? SuccessMessage { get; set; }
}
