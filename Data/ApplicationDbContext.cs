using CloudStorage.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CloudStorage.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Folder> Folders => Set<Folder>();
    public DbSet<FileItem> FileItems => Set<FileItem>();
    public DbSet<CloudStorage.Models.FileShare> FileShares => Set<CloudStorage.Models.FileShare>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Folder relationships
        builder.Entity<Folder>()
            .HasOne(f => f.Owner)
            .WithMany(u => u.Folders)
            .HasForeignKey(f => f.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Folder>()
            .HasOne(f => f.ParentFolder)
            .WithMany(f => f.SubFolders)
            .HasForeignKey(f => f.ParentFolderId)
            .OnDelete(DeleteBehavior.Restrict);

        // Unique root folder per user (only 1 folder with ParentFolderId = null per OwnerId)
        builder.Entity<Folder>()
            .HasIndex(f => f.OwnerId)
            .IsUnique()
            .HasFilter("\"ParentFolderId\" IS NULL");

        // Unique folder name per owner and parent folder
        builder.Entity<Folder>()
            .HasIndex(f => new { f.OwnerId, f.ParentFolderId, f.Name })
            .IsUnique();

        // FileItem relationships
        builder.Entity<FileItem>()
            .HasOne(f => f.Owner)
            .WithMany(u => u.FileItems)
            .HasForeignKey(f => f.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<FileItem>()
            .HasOne(f => f.Folder)
            .WithMany(f => f.FileItems)
            .HasForeignKey(f => f.FolderId)
            .OnDelete(DeleteBehavior.Restrict);

        // FileShare relationships
        builder.Entity<CloudStorage.Models.FileShare>()
            .HasOne(fs => fs.FileItem)
            .WithMany(f => f.FileShares)
            .HasForeignKey(fs => fs.FileItemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<CloudStorage.Models.FileShare>()
            .HasOne(fs => fs.SharedWithUser)
            .WithMany(u => u.SharedFiles)
            .HasForeignKey(fs => fs.SharedWithUserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Unique constraint: A file can only be shared once with the same user
        builder.Entity<CloudStorage.Models.FileShare>()
            .HasIndex(fs => new { fs.FileItemId, fs.SharedWithUserId })
            .IsUnique();
    }
}