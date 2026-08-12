using CloudStorage.Models;

namespace CloudStorage.Models.FileManager;

public class SearchResultItem
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ItemType { get; set; } = string.Empty; // "Folder" or "File"
    public Guid? ParentFolderId { get; set; } // ParentFolderId for Folder, FolderId for File
    public long Size { get; set; } // 0 for Folder
    public string ContentType { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class SearchViewModel
{
    public string? Q { get; set; }
    public string Type { get; set; } = "All"; // "All", "Files", "Folders"
    public string FileType { get; set; } = "All"; // "All", "PDF", "Images", "Documents", "Archives"
    public string Sort { get; set; } = "name"; // "name", "created", "updated", "size"
    public string Direction { get; set; } = "asc"; // "asc", "desc"
    public string Date { get; set; } = "All"; // "All", "Today", "7d", "30d"

    public List<SearchResultItem> Results { get; set; } = new();
    public int TotalCount { get; set; }
    public string? ErrorMessage { get; set; }
}
