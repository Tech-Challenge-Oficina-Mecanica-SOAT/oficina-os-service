using OficinaOs.Domain.Common;
using OficinaOs.Domain.Enums;

namespace OficinaOs.Domain.Events;

/// <summary>Disparado ao abrir a OS. Na etapa _4 vira o evento SNS os.criada (via Outbox).</summary>
public sealed record OsCriadaEvent(
    Guid OsId, Guid ClienteId, Guid VeiculoId, string DefeitoRelatado, DateTime CriadaEm) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}

/// <summary>Disparado ao cancelar a OS (compensação da Saga). Vira o evento SNS os.cancelada.</summary>
public sealed record OsCanceladaEvent(
    Guid OsId, MotivoCancelamento Motivo, DateTime CanceladaEm) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}
