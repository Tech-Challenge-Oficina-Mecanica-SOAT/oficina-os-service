using OficinaOs.Domain.Entities;
using OficinaOs.Domain.Enums;

namespace OficinaOs.Application.Abstractions;

public interface IOrdemServicoRepository
{
    Task AddAsync(OrdemServico os, CancellationToken ct = default);

    /// <summary>Carrega a OS com o histórico de status.</summary>
    Task<OrdemServico?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<OrdemServico>> ListAsync(StatusOS? status, Guid? clienteId, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
