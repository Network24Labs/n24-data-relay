using System.Text.Json;
using System.Threading.Channels;
using N24DataRelay.Core.Interfaces;
using N24DataRelay.WebApp.Data;

namespace N24DataRelay.WebApp.Services;

/// <summary>
/// SQLite-backed audit logger. Enqueues events synchronously and persists them via a
/// single-reader background channel to avoid database-locked contention.
/// </summary>
public sealed class SqliteAuditLogger : IAuditLogger, IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SqliteAuditLogger> _logger;

    private readonly Channel<AuditEvent> _queue =
        Channel.CreateUnbounded<AuditEvent>(new() { SingleReader = true });

    private CancellationTokenSource? _cts;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public SqliteAuditLogger(IServiceScopeFactory scopeFactory, ILogger<SqliteAuditLogger> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public void Log(string eventType, string? actor, string? subject = null,
                    object? details = null, string? ipAddress = null)
    {
        var ev = new AuditEvent
        {
            EventType = eventType,
            Actor = actor,
            Subject = subject,
            IpAddress = ipAddress,
            Details = details == null ? null : JsonSerializer.Serialize(details, _jsonOptions)
        };
        _queue.Writer.TryWrite(ev);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = ProcessQueueAsync(_cts.Token);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _queue.Writer.TryComplete();
        _cts?.Cancel();
        return Task.CompletedTask;
    }

    private async Task ProcessQueueAsync(CancellationToken ct)
    {
        await foreach (var ev in _queue.Reader.ReadAllAsync(ct))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                db.AuditEvents.Add(ev);
                await db.SaveChangesAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist audit event {EventType} for {Actor}", ev.EventType, ev.Actor);
            }
        }
    }
}
