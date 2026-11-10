using Microsoft.EntityFrameworkCore;
using OficinaOs.Application.Abstractions;

namespace OficinaOs.Infrastructure.Persistence;

/// <summary>Linha de processed_events: eventId (chave primária) já tratado pela OS. Retenção sugerida: 7 dias.</summary>
public class ProcessedEvent
{
    public Guid EventId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public DateTime ProcessedAt { get; set; }
}

public class ProcessedEventStore : IProcessedEventStore
{
    private readonly OsDbContext _db;

    public ProcessedEventStore(OsDbContext db) => _db = db;

    public Task<bool> ExistsAsync(Guid eventId, CancellationToken ct = default) =>
        _db.ProcessedEvents.AnyAsync(p => p.EventId == eventId, ct);

    public void Add(Guid eventId, string eventType, string outcome) =>
        _db.ProcessedEvents.Add(new ProcessedEvent
        {
            EventId = eventId, EventType = eventType, Outcome = outcome, ProcessedAt = DateTime.UtcNow
        });
}
