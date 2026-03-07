using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using N24DataRelay.Core.Models;

namespace N24DataRelay.WebApp.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<TransferRecord> TransferRecords { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<TransferRecord>(e =>
        {
            e.HasKey(r => r.Id);
            e.HasIndex(r => r.QueuedAt);
            e.HasIndex(r => r.SourcePath);
        });
    }
}

public class ApplicationUser : IdentityUser
{
    public bool IsApproved { get; set; }
    public DateTime? ApprovedDate { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime RegistrationDate { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Persisted transfer status record (EF entity). Mirrors TransferStatusRecord fields
/// but uses mutable properties suitable for EF Core tracking.
/// </summary>
public class TransferRecord
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string FileName { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public string UploadedBy { get; set; } = string.Empty;
    public string Status { get; set; } = nameof(TransferStatus.Queued);
    public DateTime QueuedAt { get; set; } = DateTime.UtcNow;
    public DateTime? TransferStartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public long? FileSize { get; set; }
    public string? DestinationPath { get; set; }

    public TransferStatusRecord ToStatusRecord() => new()
    {
        Id = Id,
        FileName = FileName,
        SourcePath = SourcePath,
        UploadedBy = UploadedBy,
        FileSize = FileSize,
        Status = Enum.TryParse<TransferStatus>(Status, out var s) ? s : TransferStatus.Queued,
        QueuedAt = QueuedAt,
        TransferStartedAt = TransferStartedAt,
        CompletedAt = CompletedAt,
        ErrorMessage = ErrorMessage,
        DestinationPath = DestinationPath
    };

    public static TransferRecord FromStatusRecord(TransferStatusRecord r) => new()
    {
        Id = r.Id,
        FileName = r.FileName,
        SourcePath = r.SourcePath,
        UploadedBy = r.UploadedBy,
        FileSize = r.FileSize,
        Status = r.Status.ToString(),
        QueuedAt = r.QueuedAt,
        TransferStartedAt = r.TransferStartedAt,
        CompletedAt = r.CompletedAt,
        ErrorMessage = r.ErrorMessage,
        DestinationPath = r.DestinationPath
    };
}
