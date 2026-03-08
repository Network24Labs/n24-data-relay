namespace N24DataRelay.WebApp.Controllers;

// ── Response DTOs for the monitoring API ─────────────────────────────────────
// Named types are required so Swagger can generate accurate JSON schemas.
// All properties are intentionally non-nullable where the value is always present.

/// <summary>Service heartbeat — no authentication required.</summary>
/// <param name="Status">Always "ok" when the service is running.</param>
/// <param name="Timestamp">UTC time of this response.</param>
/// <param name="LastTransferAt">UTC time the most recent transfer completed, or null if none recorded.</param>
/// <param name="LastTransferFile">File name of the most recent transfer, or null if none recorded.</param>
/// <param name="QueueDepth">Number of transfers currently queued or in-progress.</param>
public record HealthResponse(
    string Status,
    DateTime Timestamp,
    DateTime? LastTransferAt,
    string? LastTransferFile,
    int QueueDepth);

/// <summary>A single transfer record with full observability metrics.</summary>
/// <param name="Id">Unique identifier (GUID string).</param>
/// <param name="FileName">Original file name.</param>
/// <param name="UploadedBy">Username that uploaded the file.</param>
/// <param name="Status">Current status: Queued, Transferring, Completed, Failed, or Archived.</param>
/// <param name="QueuedAt">UTC time the file entered the transfer queue.</param>
/// <param name="TransferStartedAt">UTC time the transfer attempt began, or null if not yet started.</param>
/// <param name="CompletedAt">UTC time the transfer finished (success or final failure), or null if still in progress.</param>
/// <param name="FileSize">File size in bytes, or null if unavailable.</param>
/// <param name="DestinationPath">Destination path on the remote system, or null if not yet transferred.</param>
/// <param name="RetryCount">Number of retry attempts made (0 = succeeded on first try).</param>
/// <param name="ThroughputBytesPerSec">Measured transfer throughput in bytes/second, or null if unavailable.</param>
/// <param name="Verified">Whether file integrity was verified after transfer.</param>
/// <param name="ErrorMessage">Short error message on failure, or null on success.</param>
/// <param name="TransferMethod">Transfer method used: SSH/SCP or SMB.</param>
/// <param name="RemoteHost">Remote host or share the file was sent to.</param>
public record TransferRecordDto(
    string Id,
    string FileName,
    string UploadedBy,
    string Status,
    DateTime QueuedAt,
    DateTime? TransferStartedAt,
    DateTime? CompletedAt,
    long? FileSize,
    string? DestinationPath,
    int RetryCount,
    double? ThroughputBytesPerSec,
    bool Verified,
    string? ErrorMessage,
    string? TransferMethod,
    string? RemoteHost);

/// <summary>A single audit log entry.</summary>
/// <param name="Id">Unique identifier (GUID string).</param>
/// <param name="Timestamp">UTC time the event occurred.</param>
/// <param name="EventType">
/// Event type constant — see AuditEventTypes for possible values.
/// Common values: UserLogin, UserLoginFailed, UserLogout, UserRegistered,
/// PasswordChanged, ConfigSaved, TransferFailed, ServiceStarted, ServiceStopped.
/// </param>
/// <param name="Actor">User or system actor that triggered the event, or null for anonymous/system events.</param>
/// <param name="Subject">Subject of the event (e.g. username or file name), or null.</param>
/// <param name="Details">JSON-serialised additional details, or null.</param>
/// <param name="IpAddress">IP address of the client that triggered the event, or null.</param>
public record AuditEventDto(
    string Id,
    DateTime Timestamp,
    string EventType,
    string? Actor,
    string? Subject,
    string? Details,
    string? IpAddress);

/// <summary>Standard error envelope returned on auth failures.</summary>
/// <param name="Error">Human-readable error message.</param>
public record ErrorResponse(string Error);
