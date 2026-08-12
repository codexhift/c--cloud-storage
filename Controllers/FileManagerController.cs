using CloudStorage.Data;
using CloudStorage.Models;
using CloudStorage.Models.FileManager;
using CloudStorage.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CloudStorage.Controllers;

[Authorize]
public class FileManagerController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IStorageService _storage;
    private readonly IConfiguration _configuration;
    private readonly ILogger<FileManagerController> _logger;

    public FileManagerController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        IStorageService storage,
        IConfiguration configuration,
        ILogger<FileManagerController> logger)
    {
        _db = db;
        _userManager = userManager;
        _storage = storage;
        _configuration = configuration;
        _logger = logger;
    }

    // ──────────────────────────────────────────────
    // INDEX — list current folder (Active files/folders only)
    // ──────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> Index(Guid? folderId, string? error, string? success)
    {
        var userId = _userManager.GetUserId(User)!;

        Folder? folder;
        if (folderId == null)
        {
            folder = await _db.Folders
                .FirstOrDefaultAsync(f => f.OwnerId == userId && f.ParentFolderId == null && !f.IsDeleted);
        }
        else
        {
            folder = await _db.Folders
                .FirstOrDefaultAsync(f => f.Id == folderId && f.OwnerId == userId && !f.IsDeleted);
        }

        if (folder == null)
            return NotFound();

        var subFolders = await _db.Folders
            .Where(f => f.ParentFolderId == folder.Id && f.OwnerId == userId && !f.IsDeleted)
            .OrderBy(f => f.Name)
            .ToListAsync();

        var fileItems = await _db.FileItems
            .Where(f => f.FolderId == folder.Id && f.OwnerId == userId && !f.IsDeleted)
            .OrderBy(f => f.Name)
            .ToListAsync();

        var allUserFolders = await _db.Folders
            .Where(f => f.OwnerId == userId && !f.IsDeleted)
            .OrderBy(f => f.Name)
            .ToListAsync();

        var breadcrumbs = BuildBreadcrumbs(allUserFolders, folder);

        var vm = new FileManagerViewModel
        {
            CurrentFolder = folder,
            Breadcrumbs = breadcrumbs,
            SubFolders = subFolders,
            FileItems = fileItems,
            AllUserFolders = allUserFolders,
            ErrorMessage = error,
            SuccessMessage = success
        };

        return View(vm);
    }

    // ──────────────────────────────────────────────
    // SEARCH — Global Search, Filtering, and Sorting
    // ──────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> Search(
        string? q,
        string? type,
        string? fileType,
        string? sort,
        string? direction,
        string? date)
    {
        var userId = _userManager.GetUserId(User)!;

        // Validation & Normalization
        q = q?.Trim();
        if (q != null && q.Length > 100)
        {
            q = q.Substring(0, 100);
        }

        type = (type ?? "All").Trim();
        if (type != "Files" && type != "Folders") type = "All";

        fileType = (fileType ?? "All").Trim();

        sort = (sort ?? "name").ToLowerInvariant().Trim();
        var allowedSorts = new[] { "name", "created", "updated", "size" };
        if (!allowedSorts.Contains(sort)) sort = "name";

        direction = (direction ?? "asc").ToLowerInvariant().Trim();
        if (direction != "desc") direction = "asc";

        date = (date ?? "All").Trim();
        var allowedDates = new[] { "All", "Today", "7d", "30d" };
        if (!allowedDates.Contains(date)) date = "All";

        // Date Boundary Calculation
        DateTime? dateCutoff = null;
        var now = DateTime.UtcNow;
        if (date == "Today")
        {
            dateCutoff = now.Date;
        }
        else if (date == "7d")
        {
            dateCutoff = now.AddDays(-7);
        }
        else if (date == "30d")
        {
            dateCutoff = now.AddDays(-30);
        }

        var results = new List<SearchResultItem>();

        // 1. QUERY FOLDERS
        if (type == "All" || type == "Folders")
        {
            var folderQuery = _db.Folders
                .Where(f => f.OwnerId == userId && !f.IsDeleted && f.ParentFolderId != null); // exclude root from search

            if (!string.IsNullOrWhiteSpace(q))
            {
                folderQuery = folderQuery.Where(f => EF.Functions.ILike(f.Name, $"%{q}%"));
            }

            if (dateCutoff.HasValue)
            {
                folderQuery = folderQuery.Where(f => f.CreatedAt >= dateCutoff.Value);
            }

            // Apply Folder Sorting
            if (sort == "created")
            {
                folderQuery = direction == "desc"
                    ? folderQuery.OrderByDescending(f => f.CreatedAt)
                    : folderQuery.OrderBy(f => f.CreatedAt);
            }
            else
            {
                // Default & Name sort for folders (Size sort falls back to Name)
                folderQuery = direction == "desc"
                    ? folderQuery.OrderByDescending(f => f.Name)
                    : folderQuery.OrderBy(f => f.Name);
            }

            var folderResults = await folderQuery
                .Take(100)
                .Select(f => new SearchResultItem
                {
                    Id = f.Id,
                    Name = f.Name,
                    ItemType = "Folder",
                    ParentFolderId = f.ParentFolderId,
                    Size = 0,
                    ContentType = "Folder",
                    CreatedAt = f.CreatedAt,
                    UpdatedAt = f.CreatedAt
                })
                .ToListAsync();

            results.AddRange(folderResults);
        }

        // 2. QUERY FILES
        if (type == "All" || type == "Files")
        {
            var fileQuery = _db.FileItems
                .Where(f => f.OwnerId == userId && !f.IsDeleted);

            if (!string.IsNullOrWhiteSpace(q))
            {
                fileQuery = fileQuery.Where(f => EF.Functions.ILike(f.Name, $"%{q}%"));
            }

            if (dateCutoff.HasValue)
            {
                fileQuery = fileQuery.Where(f => f.UpdatedAt >= dateCutoff.Value);
            }

            // File Type Filter
            if (fileType == "PDF")
            {
                fileQuery = fileQuery.Where(f => f.ContentType == "application/pdf" || f.Name.ToLower().EndsWith(".pdf"));
            }
            else if (fileType == "Images")
            {
                fileQuery = fileQuery.Where(f =>
                    f.ContentType.StartsWith("image/") ||
                    f.Name.ToLower().EndsWith(".png") ||
                    f.Name.ToLower().EndsWith(".jpg") ||
                    f.Name.ToLower().EndsWith(".jpeg") ||
                    f.Name.ToLower().EndsWith(".gif") ||
                    f.Name.ToLower().EndsWith(".webp"));
            }
            else if (fileType == "Documents")
            {
                fileQuery = fileQuery.Where(f =>
                    f.ContentType.Contains("word") ||
                    f.ContentType.Contains("excel") ||
                    f.ContentType.Contains("presentation") ||
                    f.ContentType.Contains("text") ||
                    f.Name.ToLower().EndsWith(".doc") ||
                    f.Name.ToLower().EndsWith(".docx") ||
                    f.Name.ToLower().EndsWith(".xls") ||
                    f.Name.ToLower().EndsWith(".xlsx") ||
                    f.Name.ToLower().EndsWith(".ppt") ||
                    f.Name.ToLower().EndsWith(".pptx") ||
                    f.Name.ToLower().EndsWith(".txt"));
            }
            else if (fileType == "Archives")
            {
                fileQuery = fileQuery.Where(f =>
                    f.ContentType.Contains("zip") ||
                    f.ContentType.Contains("rar") ||
                    f.ContentType.Contains("compressed") ||
                    f.ContentType.Contains("tar") ||
                    f.Name.ToLower().EndsWith(".zip") ||
                    f.Name.ToLower().EndsWith(".rar") ||
                    f.Name.ToLower().EndsWith(".7z") ||
                    f.Name.ToLower().EndsWith(".tar") ||
                    f.Name.ToLower().EndsWith(".gz"));
            }

            // Apply File Sorting
            if (sort == "created")
            {
                fileQuery = direction == "desc"
                    ? fileQuery.OrderByDescending(f => f.CreatedAt)
                    : fileQuery.OrderBy(f => f.CreatedAt);
            }
            else if (sort == "updated")
            {
                fileQuery = direction == "desc"
                    ? fileQuery.OrderByDescending(f => f.UpdatedAt)
                    : fileQuery.OrderBy(f => f.UpdatedAt);
            }
            else if (sort == "size")
            {
                fileQuery = direction == "desc"
                    ? fileQuery.OrderByDescending(f => f.Size)
                    : fileQuery.OrderBy(f => f.Size);
            }
            else
            {
                fileQuery = direction == "desc"
                    ? fileQuery.OrderByDescending(f => f.Name)
                    : fileQuery.OrderBy(f => f.Name);
            }

            var fileResults = await fileQuery
                .Take(100)
                .Select(f => new SearchResultItem
                {
                    Id = f.Id,
                    Name = f.Name,
                    ItemType = "File",
                    ParentFolderId = f.FolderId,
                    Size = f.Size,
                    ContentType = f.ContentType,
                    CreatedAt = f.CreatedAt,
                    UpdatedAt = f.UpdatedAt
                })
                .ToListAsync();

            results.AddRange(fileResults);
        }

        // Final Memory Sort (Keep Folders first, then Files when type is All)
        if (type == "All")
        {
            var foldersPart = results.Where(r => r.ItemType == "Folder");
            var filesPart = results.Where(r => r.ItemType == "File");

            if (sort == "name")
            {
                foldersPart = direction == "desc" ? foldersPart.OrderByDescending(f => f.Name) : foldersPart.OrderBy(f => f.Name);
                filesPart = direction == "desc" ? filesPart.OrderByDescending(f => f.Name) : filesPart.OrderBy(f => f.Name);
            }
            else if (sort == "created")
            {
                foldersPart = direction == "desc" ? foldersPart.OrderByDescending(f => f.CreatedAt) : foldersPart.OrderBy(f => f.CreatedAt);
                filesPart = direction == "desc" ? filesPart.OrderByDescending(f => f.CreatedAt) : filesPart.OrderBy(f => f.CreatedAt);
            }
            else if (sort == "updated")
            {
                filesPart = direction == "desc" ? filesPart.OrderByDescending(f => f.UpdatedAt) : filesPart.OrderBy(f => f.UpdatedAt);
            }
            else if (sort == "size")
            {
                filesPart = direction == "desc" ? filesPart.OrderByDescending(f => f.Size) : filesPart.OrderBy(f => f.Size);
            }

            results = foldersPart.Concat(filesPart).Take(100).ToList();
        }
        else
        {
            results = results.Take(100).ToList();
        }

        var vm = new SearchViewModel
        {
            Q = q,
            Type = type,
            FileType = fileType,
            Sort = sort,
            Direction = direction,
            Date = date,
            Results = results,
            TotalCount = results.Count
        };

        return View(vm);
    }

    // ──────────────────────────────────────────────
    // CREATE FOLDER
    // ──────────────────────────────────────────────

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateFolder(Guid parentFolderId, string name)
    {
        var userId = _userManager.GetUserId(User)!;

        var parent = await _db.Folders
            .FirstOrDefaultAsync(f => f.Id == parentFolderId && f.OwnerId == userId && !f.IsDeleted);

        if (parent == null)
            return NotFound();

        if (string.IsNullOrWhiteSpace(name))
            return RedirectToAction(nameof(Index), new { folderId = parentFolderId, error = "Nama folder tidak boleh kosong." });

        name = name.Trim();

        var exists = await _db.Folders.AnyAsync(f =>
            f.OwnerId == userId &&
            f.ParentFolderId == parentFolderId &&
            f.Name == name &&
            !f.IsDeleted);

        if (exists)
            return RedirectToAction(nameof(Index), new { folderId = parentFolderId, error = $"Folder '{name}' sudah ada di sini." });

        var folder = new Folder
        {
            Id = Guid.NewGuid(),
            Name = name,
            OwnerId = userId,
            ParentFolderId = parentFolderId,
            CreatedAt = DateTime.UtcNow,
            IsDeleted = false
        };

        _db.Folders.Add(folder);
        await _db.SaveChangesAsync();

        return RedirectToAction(nameof(Index), new { folderId = parentFolderId, success = $"Folder '{name}' berhasil dibuat." });
    }

    // ──────────────────────────────────────────────
    // UPLOAD FILE
    // ──────────────────────────────────────────────

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadFile(Guid folderId, IFormFile? file)
    {
        var userId = _userManager.GetUserId(User)!;

        var folder = await _db.Folders
            .FirstOrDefaultAsync(f => f.Id == folderId && f.OwnerId == userId && !f.IsDeleted);

        if (folder == null)
            return NotFound();

        if (file == null || file.Length == 0)
            return RedirectToAction(nameof(Index), new { folderId, error = "Tidak ada file yang dipilih atau file kosong." });

        var maxFileSizeMB = _configuration.GetValue<int>("Storage:MaxFileSizeMB", 50);
        var maxBytes = maxFileSizeMB * 1024L * 1024L;
        if (file.Length > maxBytes)
            return RedirectToAction(nameof(Index), new { folderId, error = $"Ukuran file melebihi batas {maxFileSizeMB} MB." });

        string storageKey;
        try
        {
            using var stream = file.OpenReadStream();
            storageKey = await _storage.SaveAsync(stream);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gagal menyimpan file fisik saat upload.");
            return RedirectToAction(nameof(Index), new { folderId, error = "Gagal menyimpan file. Silakan coba lagi." });
        }

        var fileItem = new FileItem
        {
            Id = Guid.NewGuid(),
            Name = Path.GetFileName(file.FileName),
            OwnerId = userId,
            FolderId = folderId,
            Size = file.Length,
            ContentType = file.ContentType,
            StorageKey = storageKey,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsDeleted = false
        };

        try
        {
            _db.FileItems.Add(fileItem);
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gagal menyimpan metadata file ke database. Menghapus file fisik (storageKey: {Key}).", storageKey);
            await _storage.DeleteAsync(storageKey);
            return RedirectToAction(nameof(Index), new { folderId, error = "Gagal menyimpan informasi file." });
        }

        return RedirectToAction(nameof(Index), new { folderId, success = $"File '{fileItem.Name}' berhasil diunggah." });
    }

    // ──────────────────────────────────────────────
    // DOWNLOAD FILE (Active files only)
    // ──────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> DownloadFile(Guid id)
    {
        var userId = _userManager.GetUserId(User)!;

        var fileItem = await _db.FileItems
            .FirstOrDefaultAsync(f => f.Id == id && f.OwnerId == userId && !f.IsDeleted);

        if (fileItem == null)
            return NotFound();

        if (string.IsNullOrEmpty(fileItem.StorageKey))
            return NotFound();

        var stream = await _storage.OpenReadAsync(fileItem.StorageKey);
        if (stream == null)
            return NotFound();

        var contentType = string.IsNullOrEmpty(fileItem.ContentType)
            ? "application/octet-stream"
            : fileItem.ContentType;

        return File(stream, contentType, fileItem.Name);
    }

    // ──────────────────────────────────────────────
    // RENAME FILE
    // ──────────────────────────────────────────────

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RenameFile(Guid id, string newName)
    {
        var userId = _userManager.GetUserId(User)!;

        var fileItem = await _db.FileItems
            .FirstOrDefaultAsync(f => f.Id == id && f.OwnerId == userId && !f.IsDeleted);

        if (fileItem == null)
            return NotFound();

        if (string.IsNullOrWhiteSpace(newName))
            return RedirectToAction(nameof(Index), new { folderId = fileItem.FolderId, error = "Nama file tidak boleh kosong." });

        fileItem.Name = newName.Trim();
        fileItem.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return RedirectToAction(nameof(Index), new { folderId = fileItem.FolderId, success = $"File berhasil diubah menjadi '{fileItem.Name}'." });
    }

    // ──────────────────────────────────────────────
    // RENAME FOLDER
    // ──────────────────────────────────────────────

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RenameFolder(Guid id, string newName)
    {
        var userId = _userManager.GetUserId(User)!;

        var folder = await _db.Folders
            .FirstOrDefaultAsync(f => f.Id == id && f.OwnerId == userId && !f.IsDeleted);

        if (folder == null)
            return NotFound();

        if (folder.ParentFolderId == null)
            return RedirectToAction(nameof(Index), new { error = "Root folder tidak dapat diubah namanya." });

        if (string.IsNullOrWhiteSpace(newName))
            return RedirectToAction(nameof(Index), new { folderId = folder.ParentFolderId, error = "Nama folder tidak boleh kosong." });

        newName = newName.Trim();

        var exists = await _db.Folders.AnyAsync(f =>
            f.OwnerId == userId &&
            f.ParentFolderId == folder.ParentFolderId &&
            f.Name == newName &&
            f.Id != id &&
            !f.IsDeleted);

        if (exists)
            return RedirectToAction(nameof(Index), new { folderId = folder.ParentFolderId, error = $"Folder '{newName}' sudah ada di sini." });

        folder.Name = newName;
        await _db.SaveChangesAsync();

        return RedirectToAction(nameof(Index), new { folderId = folder.ParentFolderId, success = $"Folder berhasil diubah menjadi '{newName}'." });
    }

    // ──────────────────────────────────────────────
    // MOVE FILE
    // ──────────────────────────────────────────────

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MoveFile(Guid id, Guid targetFolderId)
    {
        var userId = _userManager.GetUserId(User)!;

        var fileItem = await _db.FileItems
            .FirstOrDefaultAsync(f => f.Id == id && f.OwnerId == userId && !f.IsDeleted);

        if (fileItem == null)
            return NotFound();

        var targetFolder = await _db.Folders
            .FirstOrDefaultAsync(f => f.Id == targetFolderId && f.OwnerId == userId && !f.IsDeleted);

        if (targetFolder == null)
            return NotFound();

        var sourceFolderId = fileItem.FolderId;
        fileItem.FolderId = targetFolderId;
        fileItem.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return RedirectToAction(nameof(Index), new { folderId = sourceFolderId, success = $"File '{fileItem.Name}' berhasil dipindahkan." });
    }

    // ──────────────────────────────────────────────
    // MOVE FOLDER
    // ──────────────────────────────────────────────

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MoveFolder(Guid id, Guid targetFolderId)
    {
        var userId = _userManager.GetUserId(User)!;

        var folder = await _db.Folders
            .FirstOrDefaultAsync(f => f.Id == id && f.OwnerId == userId && !f.IsDeleted);

        if (folder == null)
            return NotFound();

        if (folder.ParentFolderId == null)
            return RedirectToAction(nameof(Index), new { error = "Root folder tidak dapat dipindahkan." });

        if (folder.Id == targetFolderId)
            return RedirectToAction(nameof(Index), new { folderId = folder.ParentFolderId, error = "Folder tidak dapat dipindahkan ke dalam dirinya sendiri." });

        var targetFolder = await _db.Folders
            .FirstOrDefaultAsync(f => f.Id == targetFolderId && f.OwnerId == userId && !f.IsDeleted);

        if (targetFolder == null)
            return NotFound();

        // Cek apakah targetFolder adalah descendant dari folder yang akan dipindahkan
        var allFolders = await _db.Folders.Where(f => f.OwnerId == userId && !f.IsDeleted).ToListAsync();
        if (IsDescendant(allFolders, descendantId: targetFolderId, ancestorId: id))
            return RedirectToAction(nameof(Index), new { folderId = folder.ParentFolderId, error = "Tidak dapat memindahkan folder ke dalam sub-folder miliknya sendiri." });

        var existsName = await _db.Folders.AnyAsync(f =>
            f.OwnerId == userId &&
            f.ParentFolderId == targetFolderId &&
            f.Name == folder.Name &&
            f.Id != id &&
            !f.IsDeleted);

        if (existsName)
            return RedirectToAction(nameof(Index), new { folderId = folder.ParentFolderId, error = $"Folder '{folder.Name}' sudah ada di tujuan." });

        var oldParentId = folder.ParentFolderId;
        folder.ParentFolderId = targetFolderId;
        await _db.SaveChangesAsync();

        return RedirectToAction(nameof(Index), new { folderId = oldParentId, success = $"Folder '{folder.Name}' berhasil dipindahkan." });
    }

    // ──────────────────────────────────────────────
    // DELETE FILE → SOFT DELETE (MOVE TO TRASH)
    // ──────────────────────────────────────────────

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteFile(Guid id)
    {
        var userId = _userManager.GetUserId(User)!;

        var fileItem = await _db.FileItems
            .FirstOrDefaultAsync(f => f.Id == id && f.OwnerId == userId && !f.IsDeleted);

        if (fileItem == null)
            return NotFound();

        var folderId = fileItem.FolderId;

        fileItem.IsDeleted = true;
        fileItem.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return RedirectToAction(nameof(Index), new { folderId, success = $"File '{fileItem.Name}' dipindahkan ke Trash." });
    }

    // ──────────────────────────────────────────────
    // DELETE FOLDER → SOFT DELETE SUBTREE (MOVE TO TRASH)
    // ──────────────────────────────────────────────

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteFolder(Guid id)
    {
        var userId = _userManager.GetUserId(User)!;

        var folder = await _db.Folders
            .FirstOrDefaultAsync(f => f.Id == id && f.OwnerId == userId && !f.IsDeleted);

        if (folder == null)
            return NotFound();

        if (folder.ParentFolderId == null)
            return RedirectToAction(nameof(Index), new { error = "Root folder tidak dapat dipindahkan ke Trash." });

        var parentId = folder.ParentFolderId;

        var allFolders = await _db.Folders.Where(f => f.OwnerId == userId).ToListAsync();
        var descendantFolderIds = GetDescendantFolderIds(allFolders, id);
        descendantFolderIds.Add(id);

        var now = DateTime.UtcNow;

        var foldersToSoftDelete = await _db.Folders
            .Where(f => descendantFolderIds.Contains(f.Id) && !f.IsDeleted)
            .ToListAsync();

        foreach (var f in foldersToSoftDelete)
        {
            f.IsDeleted = true;
            f.DeletedAt = now;
        }

        var filesToSoftDelete = await _db.FileItems
            .Where(f => descendantFolderIds.Contains(f.FolderId) && !f.IsDeleted)
            .ToListAsync();

        foreach (var file in filesToSoftDelete)
        {
            file.IsDeleted = true;
            file.DeletedAt = now;
        }

        await _db.SaveChangesAsync();

        return RedirectToAction(nameof(Index), new { folderId = parentId, success = $"Folder '{folder.Name}' dan seluruh isinya dipindahkan ke Trash." });
    }

    // ──────────────────────────────────────────────
    // TRASH PAGE — List trashed items
    // ──────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> Trash(string? error, string? success)
    {
        var userId = _userManager.GetUserId(User)!;

        var allUserFolders = await _db.Folders.Where(f => f.OwnerId == userId).ToListAsync();
        var deletedFolderIds = allUserFolders.Where(f => f.IsDeleted).Select(f => f.Id).ToHashSet();

        // Top-level trashed folders (IsDeleted == true, but parent is NOT deleted)
        var trashedFolders = allUserFolders
            .Where(f => f.IsDeleted && (f.ParentFolderId == null || !deletedFolderIds.Contains(f.ParentFolderId.Value)))
            .OrderByDescending(f => f.DeletedAt)
            .ToList();

        // Top-level trashed files (IsDeleted == true, and parent folder is NOT deleted)
        var trashedFiles = await _db.FileItems
            .Where(f => f.OwnerId == userId && f.IsDeleted && !deletedFolderIds.Contains(f.FolderId))
            .OrderByDescending(f => f.DeletedAt)
            .ToListAsync();

        var vm = new TrashViewModel
        {
            TrashedFolders = trashedFolders,
            TrashedFiles = trashedFiles,
            ErrorMessage = error,
            SuccessMessage = success
        };

        return View(vm);
    }

    // ──────────────────────────────────────────────
    // RESTORE FILE
    // ──────────────────────────────────────────────

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RestoreFile(Guid id)
    {
        var userId = _userManager.GetUserId(User)!;

        var fileItem = await _db.FileItems
            .FirstOrDefaultAsync(f => f.Id == id && f.OwnerId == userId && f.IsDeleted);

        if (fileItem == null)
            return NotFound();

        // Check if parent folder is deleted
        var parentFolder = await _db.Folders
            .FirstOrDefaultAsync(f => f.Id == fileItem.FolderId && f.OwnerId == userId);

        if (parentFolder == null || parentFolder.IsDeleted)
            return RedirectToAction(nameof(Trash), new { error = $"Parent folder dari file '{fileItem.Name}' masih di Trash. Restore parent folder terlebih dahulu." });

        // Conflict check: active file with same name in same parent folder
        var conflict = await _db.FileItems.AnyAsync(f =>
            f.OwnerId == userId &&
            f.FolderId == fileItem.FolderId &&
            f.Name == fileItem.Name &&
            !f.IsDeleted);

        if (conflict)
            return RedirectToAction(nameof(Trash), new { error = $"Gagal restore: File dengan nama '{fileItem.Name}' sudah ada di folder tujuan." });

        fileItem.IsDeleted = false;
        fileItem.DeletedAt = null;
        await _db.SaveChangesAsync();

        return RedirectToAction(nameof(Trash), new { success = $"File '{fileItem.Name}' berhasil dipulihkan." });
    }

    // ──────────────────────────────────────────────
    // RESTORE FOLDER (Restores folder + subtree)
    // ──────────────────────────────────────────────

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RestoreFolder(Guid id)
    {
        var userId = _userManager.GetUserId(User)!;

        var folder = await _db.Folders
            .FirstOrDefaultAsync(f => f.Id == id && f.OwnerId == userId && f.IsDeleted);

        if (folder == null)
            return NotFound();

        if (folder.ParentFolderId == null)
            return RedirectToAction(nameof(Trash), new { error = "Root folder tidak valid di Trash." });

        // Check if parent folder is deleted
        var parentFolder = await _db.Folders
            .FirstOrDefaultAsync(f => f.Id == folder.ParentFolderId && f.OwnerId == userId);

        if (parentFolder == null || parentFolder.IsDeleted)
            return RedirectToAction(nameof(Trash), new { error = $"Parent folder dari '{folder.Name}' masih di Trash. Restore parent folder terlebih dahulu." });

        // Conflict check: active folder with same name in same parent folder
        var conflict = await _db.Folders.AnyAsync(f =>
            f.OwnerId == userId &&
            f.ParentFolderId == folder.ParentFolderId &&
            f.Name == folder.Name &&
            !f.IsDeleted);

        if (conflict)
            return RedirectToAction(nameof(Trash), new { error = $"Gagal restore: Folder dengan nama '{folder.Name}' sudah ada di folder tujuan." });

        var allFolders = await _db.Folders.Where(f => f.OwnerId == userId).ToListAsync();
        var descendantFolderIds = GetDescendantFolderIds(allFolders, id);
        descendantFolderIds.Add(id);

        var foldersToRestore = await _db.Folders
            .Where(f => descendantFolderIds.Contains(f.Id) && f.IsDeleted)
            .ToListAsync();

        foreach (var f in foldersToRestore)
        {
            f.IsDeleted = false;
            f.DeletedAt = null;
        }

        var filesToRestore = await _db.FileItems
            .Where(f => descendantFolderIds.Contains(f.FolderId) && f.IsDeleted)
            .ToListAsync();

        foreach (var file in filesToRestore)
        {
            file.IsDeleted = false;
            file.DeletedAt = null;
        }

        await _db.SaveChangesAsync();

        return RedirectToAction(nameof(Trash), new { success = $"Folder '{folder.Name}' dan isinya berhasil dipulihkan." });
    }

    // ──────────────────────────────────────────────
    // PERMANENT DELETE FILE
    // ──────────────────────────────────────────────

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PermanentDeleteFile(Guid id)
    {
        var userId = _userManager.GetUserId(User)!;

        var fileItem = await _db.FileItems
            .FirstOrDefaultAsync(f => f.Id == id && f.OwnerId == userId && f.IsDeleted);

        if (fileItem == null)
            return NotFound();

        if (!string.IsNullOrEmpty(fileItem.StorageKey))
        {
            try
            {
                await _storage.DeleteAsync(fileItem.StorageKey);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal menghapus file fisik storageKey={Key}, tetap melanjutkan hapus metadata.", fileItem.StorageKey);
            }
        }

        _db.FileItems.Remove(fileItem);
        await _db.SaveChangesAsync();

        return RedirectToAction(nameof(Trash), new { success = $"File '{fileItem.Name}' berhasil dihapus secara permanen." });
    }

    // ──────────────────────────────────────────────
    // PERMANENT DELETE FOLDER (Deletes folder + subtree + physical files)
    // ──────────────────────────────────────────────

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PermanentDeleteFolder(Guid id)
    {
        var userId = _userManager.GetUserId(User)!;

        var folder = await _db.Folders
            .FirstOrDefaultAsync(f => f.Id == id && f.OwnerId == userId && f.IsDeleted);

        if (folder == null)
            return NotFound();

        if (folder.ParentFolderId == null)
            return RedirectToAction(nameof(Trash), new { error = "Root folder tidak dapat dihapus secara permanen." });

        var allFolders = await _db.Folders.Where(f => f.OwnerId == userId).ToListAsync();
        var descendantFolderIds = GetDescendantFolderIds(allFolders, id);
        descendantFolderIds.Add(id);

        var filesToDelete = await _db.FileItems
            .Where(f => descendantFolderIds.Contains(f.FolderId))
            .ToListAsync();

        foreach (var file in filesToDelete)
        {
            if (!string.IsNullOrEmpty(file.StorageKey))
            {
                try
                {
                    await _storage.DeleteAsync(file.StorageKey);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Gagal menghapus file fisik storageKey={Key} saat permanent delete folder.", file.StorageKey);
                }
            }
        }

        _db.FileItems.RemoveRange(filesToDelete);

        var foldersToDelete = await _db.Folders
            .Where(f => descendantFolderIds.Contains(f.Id))
            .ToListAsync();

        // Delete leaf folders first to avoid FK constraint issues during EF SaveChanges
        var orderedFoldersToDelete = OrderFoldersBottomUp(foldersToDelete);
        _db.Folders.RemoveRange(orderedFoldersToDelete);

        await _db.SaveChangesAsync();

        return RedirectToAction(nameof(Trash), new { success = $"Folder '{folder.Name}' dan seluruh isinya berhasil dihapus secara permanen." });
    }

    // ──────────────────────────────────────────────
    // EMPTY TRASH
    // ──────────────────────────────────────────────

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EmptyTrash()
    {
        var userId = _userManager.GetUserId(User)!;

        var deletedFiles = await _db.FileItems
            .Where(f => f.OwnerId == userId && f.IsDeleted)
            .ToListAsync();

        foreach (var file in deletedFiles)
        {
            if (!string.IsNullOrEmpty(file.StorageKey))
            {
                try
                {
                    await _storage.DeleteAsync(file.StorageKey);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Gagal menghapus file fisik storageKey={Key} saat Empty Trash.", file.StorageKey);
                }
            }
        }

        _db.FileItems.RemoveRange(deletedFiles);

        var deletedFolders = await _db.Folders
            .Where(f => f.OwnerId == userId && f.IsDeleted && f.ParentFolderId != null)
            .ToListAsync();

        var orderedFoldersToDelete = OrderFoldersBottomUp(deletedFolders);
        _db.Folders.RemoveRange(orderedFoldersToDelete);

        await _db.SaveChangesAsync();

        return RedirectToAction(nameof(Trash), new { success = "Trash berhasil dikosongkan." });
    }

    // ──────────────────────────────────────────────
    // HELPERS
    // ──────────────────────────────────────────────

    private static List<Folder> BuildBreadcrumbs(List<Folder> allFolders, Folder current)
    {
        var crumbs = new List<Folder>();
        var lookup = allFolders.ToDictionary(f => f.Id);
        var visited = new HashSet<Guid>();
        var node = current;

        while (node != null && !visited.Contains(node.Id))
        {
            crumbs.Insert(0, node);
            visited.Add(node.Id);
            if (node.ParentFolderId == null) break;
            lookup.TryGetValue(node.ParentFolderId.Value, out node!);
        }

        return crumbs;
    }

    private static bool IsDescendant(List<Folder> allFolders, Guid descendantId, Guid ancestorId)
    {
        var lookup = allFolders.ToDictionary(f => f.Id);
        var visited = new HashSet<Guid>();
        var current = descendantId;

        while (lookup.TryGetValue(current, out var folder))
        {
            if (visited.Contains(current)) break;
            visited.Add(current);

            if (folder.ParentFolderId == null) break;
            if (folder.ParentFolderId == ancestorId) return true;

            current = folder.ParentFolderId.Value;
        }

        return false;
    }

    private static List<Guid> GetDescendantFolderIds(List<Folder> allFolders, Guid rootFolderId)
    {
        var result = new List<Guid>();
        var children = allFolders.Where(f => f.ParentFolderId == rootFolderId).ToList();

        foreach (var child in children)
        {
            result.Add(child.Id);
            result.AddRange(GetDescendantFolderIds(allFolders, child.Id));
        }

        return result;
    }

    private static List<Folder> OrderFoldersBottomUp(List<Folder> folders)
    {
        var folderIds = folders.Select(f => f.Id).ToHashSet();
        var result = new List<Folder>();
        var remaining = new List<Folder>(folders);

        while (remaining.Any())
        {
            var leaves = remaining.Where(f => !remaining.Any(child => child.ParentFolderId == f.Id)).ToList();
            if (!leaves.Any())
            {
                result.AddRange(remaining);
                break;
            }

            result.AddRange(leaves);
            foreach (var leaf in leaves)
            {
                remaining.Remove(leaf);
            }
        }

        return result;
    }
}
