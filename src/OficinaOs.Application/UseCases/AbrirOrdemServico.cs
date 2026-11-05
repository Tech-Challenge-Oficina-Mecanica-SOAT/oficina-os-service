using OficinaOs.Application.Abstractions;
using OficinaOs.Application.Dtos;
using OficinaOs.Application.Exceptions;
using OficinaOs.Domain.Entities;

namespace OficinaOs.Application.UseCases;

/// <summary>
/// Início da Saga: valida cliente e veículo no Cadastros (REST) e cria a OS em AguardandoDiagnostico.
/// O evento os.criada é levantado no domínio; a publicação (Outbox → SNS) entra na etapa _4.
/// </summary>
public class AbrirOrdemServico
{
    private readonly ICadastrosClient _cadastros;
    private readonly IOrdemServicoRepository _repo;

    public AbrirOrdemServico(ICadastrosClient cadastros, IOrdemServicoRepository repo)
    {
        _cadastros = cadastros;
        _repo = repo;
    }

    public async Task<OsResponse> ExecuteAsync(AbrirOsRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Cpf) || string.IsNullOrWhiteSpace(request.Placa) ||
            string.IsNullOrWhiteSpace(request.DefeitoRelatado))
            throw new InvalidRequestException("cpf, placa e defeitoRelatado são obrigatórios.");

        var cliente = await _cadastros.GetClientePorCpfAsync(request.Cpf, ct)
            ?? throw new NotFoundException("Cliente não encontrado.");
        if (!cliente.Ativo)
            throw new ForbiddenOperationException("Cliente inativo.");

        var veiculo = await _cadastros.GetVeiculoPorPlacaAsync(request.Placa, ct)
            ?? throw new NotFoundException("Veículo não encontrado.");
        if (veiculo.ClienteId != cliente.Id)
            throw new InvalidRequestException("O veículo informado não pertence ao cliente.");

        var os = new OrdemServico(cliente.Id, veiculo.Id, request.DefeitoRelatado.Trim());
        await _repo.AddAsync(os, ct);
        await _repo.SaveChangesAsync(ct);
        return OsResponse.From(os);
    }
}
