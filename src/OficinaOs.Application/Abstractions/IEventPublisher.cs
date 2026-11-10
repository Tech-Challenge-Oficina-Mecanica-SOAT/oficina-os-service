namespace OficinaOs.Application.Abstractions;

/// <summary>
/// Porta de publicação de eventos de integração (SNS). Quem chama é o OutboxProcessor, nunca o caso de uso:
/// o caso de uso só grava a OS e o evento na mesma transação (Outbox Pattern).
/// </summary>
public interface IEventPublisher
{
    /// <param name="eventType">Nome do evento, ex.: os.criada (define o tópico).</param>
    /// <param name="envelopeJson">Envelope completo (eventId, eventType, eventVersion, occurredAt, correlationId, producer, data).</param>
    Task PublishAsync(string eventType, string envelopeJson, CancellationToken ct = default);
}
