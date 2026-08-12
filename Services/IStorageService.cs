namespace CloudStorage.Services;

public interface IStorageService
{
    Task<string> SaveAsync(Stream contentStream, CancellationToken cancellationToken = default);
    Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string storageKey, CancellationToken cancellationToken = default);
    bool Exists(string storageKey);
}
