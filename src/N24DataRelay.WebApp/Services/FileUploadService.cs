using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using N24DataRelay.Core.Interfaces;
using N24DataRelay.Core.Models;

namespace N24DataRelay.WebApp.Services;

public class FileUploadService
{
    private readonly IOptionsMonitor<N24DataRelay.Core.Models.N24DataRelayConfiguration> _configMonitor;
    private readonly ILogger<FileUploadService> _logger;
    private readonly ITransferTracker _tracker;

    private N24DataRelay.Core.Models.N24DataRelayConfiguration Config => _configMonitor.CurrentValue;

    public FileUploadService(
        IOptionsMonitor<N24DataRelay.Core.Models.N24DataRelayConfiguration> configMonitor,
        ILogger<FileUploadService> logger,
        ITransferTracker tracker)
    {
        _configMonitor = configMonitor ?? throw new ArgumentNullException(nameof(configMonitor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
    }

    public async Task<UploadResult> UploadFileAsync(Stream fileStream, string fileName, string destination, string uploadedBy, bool requiresTransfer = false, string? notes = null, CancellationToken cancellationToken = default)
    {
        var result = new UploadResult
        {
            FileName = fileName,
            FileSize = fileStream.Length,
            UploadTime = DateTime.UtcNow,
            UploadedBy = uploadedBy,
            Destination = destination,
            RequiresTransfer = requiresTransfer,
            Notes = notes
        };

        try
        {
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            if (IsExtensionBlocked(extension))
            {
                result.ErrorMessage = $"File extension '{extension}' is blocked.";
                return result;
            }
            if (fileStream.Length > Config.WebPortal.MaxFileSizeBytes)
            {
                result.ErrorMessage = $"File size ({fileStream.Length} bytes) exceeds maximum ({Config.WebPortal.MaxFileSizeBytes} bytes).";
                return result;
            }

            var userFolderName = SanitizeUsername(uploadedBy);
            var userUploadPath = Path.Combine(destination, userFolderName);
            Directory.CreateDirectory(userUploadPath);

            var safeFileName = Path.GetFileName(fileName);
            var filePath = Path.Combine(userUploadPath, safeFileName);

            // Enqueue the tracker record BEFORE creating the file.
            // FileSystemWatcher fires the Created event the moment the file handle opens,
            // which can happen before we return from this method. Pre-registering the path
            // lets TransferWorker.FindBySourcePath find the same record rather than creating
            // a duplicate with a different ID that the client never knows about.
            if (requiresTransfer)
            {
                var record = _tracker.Enqueue(
                    fileName: safeFileName,
                    sourcePath: filePath,
                    uploadedBy: uploadedBy,
                    fileSize: fileStream.Length);
                result.TrackerId = record.Id;
            }

            await using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await fileStream.CopyToAsync(stream, cancellationToken).ConfigureAwait(false);
            }

            result.Success = true;
            result.FilePath = filePath;
            _logger.LogInformation("File saved: {FilePath}", filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading file: {FileName}", fileName);
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    public async Task<List<UploadResult>> UploadFormFilesAsync(List<IFormFile> files, string uploadedBy, bool requiresTransfer = false, string? notes = null, CancellationToken cancellationToken = default)
    {
        var destination = requiresTransfer ? Config.Service.WatchDirectory : Config.Paths.UploadDirectory;
        var results = new List<UploadResult>();
        foreach (var file in files)
        {
            await using var stream = file.OpenReadStream();
            var result = await UploadFileAsync(stream, file.FileName, destination, uploadedBy, requiresTransfer, notes, cancellationToken).ConfigureAwait(false);
            results.Add(result);
        }
        return results;
    }

    public string GetUploadDestination(bool requiresTransfer) =>
        requiresTransfer ? Config.Service.WatchDirectory : Config.Paths.UploadDirectory;

    private bool IsExtensionBlocked(string extension)
    {
        var blocked = Config.WebPortal.BlockedFileExtensions ?? new List<string>();
        return blocked.Any(b => string.Equals(b, extension, StringComparison.OrdinalIgnoreCase));
    }

    private static string SanitizeUsername(string username)
    {
        if (string.IsNullOrWhiteSpace(username)) return "unknown_user";
        var s = username;
        if (s.Contains('\\')) s = s[(s.LastIndexOf('\\') + 1)..];
        if (s.Contains('@')) s = s[..s.IndexOf('@')];
        var sanitized = new string(s.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-').ToArray());
        return string.IsNullOrEmpty(sanitized) ? "unknown_user" : sanitized;
    }
}
