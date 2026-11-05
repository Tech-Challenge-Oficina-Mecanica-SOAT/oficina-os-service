using OficinaOs.Application.Abstractions;
using OficinaOs.Application.Dtos;
using OficinaOs.Application.Exceptions;
using OficinaOs.Domain.Enums;

namespace OficinaOs.Application.UseCases;

public class ConsultarOrdemServico
{
    private readonly IOrdemServicoRepository _repo;

    public ConsultarOrdemServico(IOrdemServicoRepository repo) => _repo = repo;

    public async Task<OsDetalhadaResponse> ObterAsync(Guid id, CancellationToken ct = default)
    {
        var os = await _repo.GetByIdAsync(id, ct) ?? throw new NotFoundException("OS não encontrada.");
        return OsDetalhadaResponse.FromDetalhada(os);
    }

    public async Task<IReadOnlyList<OsResponse>> ListarAsync(StatusOS? status, Guid? clienteId, CancellationToken ct = default)
    {
        var lista = await _repo.ListAsync(status, clienteId, ct);
        return lista.Select(OsResponse.From).ToList();
    }

    public async Task<IReadOnlyList<HistoricoStatusResponse>> HistoricoAsync(Guid id, CancellationToken ct = default)
    {
        var os = await _repo.GetByIdAsync(id, ct) ?? throw new NotFoundException("OS não encontrada.");
        return os.Historico.OrderBy(h => h.MudouEm).Select(HistoricoStatusResponse.From).ToList();
    }
}
