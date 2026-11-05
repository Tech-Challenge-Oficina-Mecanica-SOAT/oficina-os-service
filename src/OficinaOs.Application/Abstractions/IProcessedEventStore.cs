namespace OficinaOs.Application.Abstractions;

/// <summary>
/// Registro de eventos já processados (idempotência). A marcação é adicionada ao mesmo unit of work da mudança
/// de estado: o IOrdemServicoRepository.SaveChangesAsync grava os dois na mesma transação.
/// </summary>
public interface IProcessedEventStore
{
    Task<bool> ExistsAsync(Guid eventId, CancellationToken ct = default);

    /// <summary>Marca o evento como processado (não salva; quem salva é o SaveChangesAsync do repositório).</summary>
    void Add(Guid eventId, string eventType, string outcome);
}
