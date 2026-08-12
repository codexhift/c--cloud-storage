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
    // INDEX — list current folder
    // ──────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> Index(Guid? folderId, string? error, string? success)
    {
        var userId = _userManager.GetUserId(User)!;

        Folder? folder;
        if (folderId == null)
        {
            folder = await _db.Folders
                .FirstOrDefaultAsync(f => f.OwnerId == userId && f.ParentFolderId == null);
        }
        else
        {
            folder = await _db.Folders
                .FirstOrDefaultAsync(f => f.Id == folderId && f.OwnerId == userId);
        }

        if (folder == null)
            return NotFound();

        var subFolders = await _db.Folders
            .Where(f => f.ParentFolderId == folder.Id && f.OwnerId == userId)
            .OrderBy(f => f.Name)
            .ToListAsync();

        var fileItems = await _db.FileItems
            .Where(f => f.FolderId == folder.Id && f.OwnerId == userId)
            .OrderBy(f => f.Name)
            .ToListAsync();

        var allUserFolders = await _db.Folders
            .Where(f => f.OwnerId == userId)
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
    // CREATE FOLDER
    // ──────────────────────────────────────────────

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateFolder(Guid parentFolderId, string name)
    {
        var userId = _userManager.GetUserId(User)!;

        var parent = await _db.Folders
            .FirstOrDefaultAsync(f => f.Id == parentFolderId && f.OwnerId == userId);

        if (parent == null)
            return NotFound();

        if (string.IsNullOrWhiteSpace(name))
            return RedirectToAction(nameof(Index), new { folderId = parentFolderId, error = "Nama folder tidak boleh kosong." });

        name = name.Trim();

        var exists = await _db.Folders.AnyAsync(f =>
            f.OwnerId == userId &&
            f.ParentFolderId == parentFolderId &&
            f.Name == name);

        if (exists)
            return RedirectToAction(nameof(Index), new { folderId = parentFolderId, error = $"Folder '{name}' sudah ada di sini." });

        var folder = new Folder
        {
            Id = Guid.NewGuid(),
            Name = name,
            OwnerId = userId,
            ParentFolderId = parentFolderId,
            CreatedAt = DateTime.UtcNow
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
            .FirstOrDefaultAsync(f => f.Id == folderId && f.OwnerId == userId);

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
            UpdatedAt = DateTime.UtcNow
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
    // DOWNLOAD FILE
    // ──────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> DownloadFile(Guid id)
    {
        var userId = _userManager.GetUserId(User)!;

        var fileItem = await _db.FileItems
            .FirstOrDefaultAsync(f => f.Id == id && f.OwnerId == userId);

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
            .FirstOrDefaultAsync(f => f.Id == id && f.OwnerId == userId);

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
            .FirstOrDefaultAsync(f => f.Id == id && f.OwnerId == userId);

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
            f.Id != id);

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
            .FirstOrDefaultAsync(f => f.Id == id && f.OwnerId == userId);

        if (fileItem == null)
            return NotFound();

        var targetFolder = await _db.Folders
            .FirstOrDefaultAsync(f => f.Id == targetFolderId && f.OwnerId == userId);

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
            .FirstOrDefaultAsync(f => f.Id == id && f.OwnerId == userId);

        if (folder == null)
            return NotFound();

        if (folder.ParentFolderId == null)
            return RedirectToAction(nameof(Index), new { error = "Root folder tidak dapat dipindahkan." });

        if (folder.Id == targetFolderId)
            return RedirectToAction(nameof(Index), new { folderId = folder.ParentFolderId, error = "Folder tidak dapat dipindahkan ke dalam dirinya sendiri." });

        var targetFolder = await _db.Folders
            .FirstOrDefaultAsync(f => f.Id == targetFolderId && f.OwnerId == userId);

        if (targetFolder == null)
            return NotFound();

        // Cek apakah targetFolder adalah descendant dari folder yang akan dipindahkan
        var allFolders = await _db.Folders.Where(f => f.OwnerId == userId).ToListAsync();
        if (IsDescendant(allFolders, descendantId: targetFolderId, ancestorId: id))
            return RedirectToAction(nameof(Index), new { folderId = folder.ParentFolderId, error = "Tidak dapat memindahkan folder ke dalam sub-folder miliknya sendiri." });

        var existsName = await _db.Folders.AnyAsync(f =>
            f.OwnerId == userId &&
            f.ParentFolderId == targetFolderId &&
            f.Name == folder.Name &&
            f.Id != id);

        if (existsName)
            return RedirectToAction(nameof(Index), new { folderId = folder.ParentFolderId, error = $"Folder '{folder.Name}' sudah ada di tujuan." });

        var oldParentId = folder.ParentFolderId;
        folder.ParentFolderId = targetFolderId;
        await _db.SaveChangesAsync();

        return RedirectToAction(nameof(Index), new { folderId = oldParentId, success = $"Folder '{folder.Name}' berhasil dipindahkan." });
    }

    // ──────────────────────────────────────────────
    // DELETE FILE
    // ──────────────────────────────────────────────

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteFile(Guid id)
    {
        var userId = _userManager.GetUserId(User)!;

        var fileItem = await _db.FileItems
            .FirstOrDefaultAsync(f => f.Id == id && f.OwnerId == userId);

        if (fileItem == null)
            return NotFound();

        var folderId = fileItem.FolderId;

        if (!string.IsNullOrEmpty(fileItem.StorageKey))
        {
            var deleted = await _storage.DeleteAsync(fileItem.StorageKey);
            if (!deleted)
                _logger.LogWarning("Gagal menghapus file fisik storageKey={Key}, tetap melanjutkan hapus metadata.", fileItem.StorageKey);
        }

        try
        {
            _db.FileItems.Remove(fileItem);
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gagal menghapus metadata FileItem {Id} dari database.", id);
            return RedirectToAction(nameof(Index), new { folderId, error = "Gagal menghapus file dari database." });
        }

        return RedirectToAction(nameof(Index), new { folderId, success = $"File '{fileItem.Name}' berhasil dihapus." });
    }

    // ──────────────────────────────────────────────
    // DELETE FOLDER
    // ──────────────────────────────────────────────

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteFolder(Guid id)
    {
        var userId = _userManager.GetUserId(User)!;

        var folder = await _db.Folders
            .FirstOrDefaultAsync(f => f.Id == id && f.OwnerId == userId);

        if (folder == null)
            return NotFound();

        if (folder.ParentFolderId == null)
            return RedirectToAction(nameof(Index), new { error = "Root folder tidak dapat dihapus." });

        var hasChildren = await _db.Folders.AnyAsync(f => f.ParentFolderId == id && f.OwnerId == userId);
        if (hasChildren)
            return RedirectToAction(nameof(Index), new { folderId = folder.ParentFolderId, error = "Folder masih berisi subfolder. Kosongkan terlebih dahulu." });

        var hasFiles = await _db.FileItems.AnyAsync(f => f.FolderId == id && f.OwnerId == userId);
        if (hasFiles)
            return RedirectToAction(nameof(Index), new { folderId = folder.ParentFolderId, error = "Folder masih berisi file. Hapus semua file terlebih dahulu." });

        var parentId = folder.ParentFolderId;
        _db.Folders.Remove(folder);
        await _db.SaveChangesAsync();

        return RedirectToAction(nameof(Index), new { folderId = parentId, success = $"Folder '{folder.Name}' berhasil dihapus." });
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

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
    }
}
