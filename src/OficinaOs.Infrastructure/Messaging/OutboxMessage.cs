namespace OficinaOs.Infrastructure.Messaging;

/// <summary>
/// Linha da tabela outbox_events. Gravada na MESMA transação da mudança de estado da OS;
/// um worker em background publica no SNS depois e marca ProcessedAt.
/// </summary>
public class OutboxMessage
{
    /// <summary>É o próprio eventId do envelope (consumidores deduplicam por ele).</summary>
    public Guid Id { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
}
