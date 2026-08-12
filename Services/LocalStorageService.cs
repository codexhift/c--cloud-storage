namespace CloudStorage.Services;

public class LocalStorageService : IStorageService
{
    private readonly string _rootDirectory;
    private readonly ILogger<LocalStorageService> _logger;

    public LocalStorageService(IConfiguration configuration, ILogger<LocalStorageService> logger)
    {
        _logger = logger;
        var configuredPath = configuration["Storage:RootPath"];

        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            configuredPath = Path.Combine(Directory.GetCurrentDirectory(), "Storage", "files");
        }
        else if (!Path.IsPathRooted(configuredPath))
        {
            configuredPath = Path.Combine(Directory.GetCurrentDirectory(), configuredPath);
        }

        _rootDirectory = Path.GetFullPath(configuredPath);

        if (!Directory.Exists(_rootDirectory))
        {
            Directory.CreateDirectory(_rootDirectory);
        }
    }

    public async Task<string> SaveAsync(Stream contentStream, CancellationToken cancellationToken = default)
    {
        var storageKey = Guid.NewGuid().ToString("N");
        var filePath = GetSafeFilePath(storageKey);

        try
        {
            using var fileStream = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, useAsync: true);
            await contentStream.CopyToAsync(fileStream, cancellationToken);
            return storageKey;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gagal menyimpan file fisik dengan storageKey {StorageKey}", storageKey);

            if (File.Exists(filePath))
            {
                try { File.Delete(filePath); } catch { }
            }

            throw;
        }
    }

    public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var filePath = GetSafeFilePath(storageKey);

        if (!File.Exists(filePath))
        {
            _logger.LogWarning("File fisik tidak ditemukan untuk storageKey {StorageKey}", storageKey);
            return Task.FromResult<Stream?>(null);
        }

        try
        {
            Stream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, useAsync: true);
            return Task.FromResult<Stream?>(stream);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gagal membuka file stream untuk storageKey {StorageKey}", storageKey);
            return Task.FromResult<Stream?>(null);
        }
    }

    public Task<bool> DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var filePath = GetSafeFilePath(storageKey);

        if (!File.Exists(filePath))
        {
            return Task.FromResult(true);
        }

        try
        {
            File.Delete(filePath);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gagal menghapus file fisik dengan storageKey {StorageKey}", storageKey);
            return Task.FromResult(false);
        }
    }

    public bool Exists(string storageKey)
    {
        var filePath = GetSafeFilePath(storageKey);
        return File.Exists(filePath);
    }

    private string GetSafeFilePath(string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey) ||
            storageKey.Contains("..") ||
            storageKey.Contains('/') ||
            storageKey.Contains('\\'))
        {
            throw new ArgumentException("StorageKey tidak valid.", nameof(storageKey));
        }

        var fullPath = Path.GetFullPath(Path.Combine(_rootDirectory, storageKey));

        if (!fullPath.StartsWith(_rootDirectory, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Akses file di luar storage root dilarang.");
        }

        return fullPath;
    }
}
