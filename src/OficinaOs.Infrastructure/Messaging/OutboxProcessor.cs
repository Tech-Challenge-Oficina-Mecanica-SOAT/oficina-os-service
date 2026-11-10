using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OficinaOs.Application.Abstractions;
using OficinaOs.Infrastructure.Persistence;

namespace OficinaOs.Infrastructure.Messaging;

/// <summary>
/// Lê eventos pendentes da outbox (em ordem), publica e marca como processados.
/// Entrega "pelo menos uma vez": se publicar mas cair antes de marcar, o evento sai de novo —
/// por isso consumidores deduplicam por eventId.
/// </summary>
public class OutboxProcessor
{
    private readonly OsDbContext _db;
    private readonly IEventPublisher _publisher;
    private readonly MessagingOptions _options;
    private readonly ILogger<OutboxProcessor> _logger;

    public OutboxProcessor(OsDbContext db, IEventPublisher publisher, IOptions<MessagingOptions> options, ILogger<OutboxProcessor> logger)
    {
        _db = db;
        _publisher = publisher;
        _options = options.Value;
        _logger = logger;
    }

    /// <returns>Quantidade de eventos publicados com sucesso neste lote.</returns>
    public async Task<int> ProcessBatchAsync(CancellationToken ct = default)
    {
        var pending = await _db.OutboxMessages
            .Where(m => m.ProcessedAt == null && m.Attempts < _options.MaxAttempts)
            .OrderBy(m => m.OccurredAt)
            .Take(_options.BatchSize)
            .ToListAsync(ct);

        var published = 0;
        foreach (var message in pending)
        {
            try
            {
                await _publisher.PublishAsync(message.EventType, message.Payload, ct);
                message.ProcessedAt = DateTime.UtcNow;
                message.LastError = null;
                published++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                message.Attempts++;
                message.LastError = Truncate(ex.Message, 500);
                _logger.LogWarning(ex, "Falha ao publicar {EventType} ({EventId}), tentativa {Attempt}/{Max}",
                    message.EventType, message.Id, message.Attempts, _options.MaxAttempts);
                if (message.Attempts >= _options.MaxAttempts)
                    _logger.LogError("Evento {EventId} ({EventType}) estacionado após {Max} tentativas", message.Id, message.EventType, _options.MaxAttempts);

                // Mantém a ordem: se um evento falhou, não publica os seguintes deste lote.
                await _db.SaveChangesAsync(ct);
                return published;
            }

            await _db.SaveChangesAsync(ct);
        }

        return published;
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max];
}
