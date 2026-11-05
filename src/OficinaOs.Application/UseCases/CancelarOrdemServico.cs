using OficinaOs.Application.Abstractions;
using OficinaOs.Application.Exceptions;
using OficinaOs.Domain.Enums;

namespace OficinaOs.Application.UseCases;

/// <summary>Cancelamento manual (compensação da Saga). Levanta os.cancelada no domínio.</summary>
public class CancelarOrdemServico
{
    private readonly IOrdemServicoRepository _repo;

    public CancelarOrdemServico(IOrdemServicoRepository repo) => _repo = repo;

    public async Task ExecuteAsync(Guid id, CancellationToken ct = default)
    {
        var os = await _repo.GetByIdAsync(id, ct) ?? throw new NotFoundException("OS não encontrada.");
        os.Cancelar(MotivoCancelamento.Outro, "api");
        await _repo.SaveChangesAsync(ct);
    }
}
