using OficinaOs.Domain.Entities;
using OficinaOs.Domain.Enums;

namespace OficinaOs.Application.Dtos;

public sealed record AbrirOsRequest(string Cpf, string Placa, string DefeitoRelatado);

public record OsResponse(
    Guid Id, StatusOS Status, Guid ClienteId, Guid VeiculoId, string DefeitoRelatado,
    DateTime CriadaEm, DateTime AtualizadaEm)
{
    public static OsResponse From(OrdemServico os) =>
        new(os.Id, os.Status, os.ClienteId, os.VeiculoId, os.DefeitoRelatado, os.CriadaEm, os.AtualizadaEm);
}

public sealed record OsDetalhadaResponse(
    Guid Id, StatusOS Status, Guid ClienteId, Guid VeiculoId, string DefeitoRelatado,
    DateTime CriadaEm, DateTime AtualizadaEm, Guid? DiagnosticoId, Guid? OrcamentoId, Guid? PagamentoId)
    : OsResponse(Id, Status, ClienteId, VeiculoId, DefeitoRelatado, CriadaEm, AtualizadaEm)
{
    public static OsDetalhadaResponse FromDetalhada(OrdemServico os) =>
        new(os.Id, os.Status, os.ClienteId, os.VeiculoId, os.DefeitoRelatado, os.CriadaEm, os.AtualizadaEm,
            os.DiagnosticoId, os.OrcamentoId, os.PagamentoId);
}

public sealed record HistoricoStatusResponse(StatusOS? StatusAnterior, StatusOS StatusNovo, DateTime MudouEm, string Motivo)
{
    public static HistoricoStatusResponse From(HistoricoStatusOS h) =>
        new(h.StatusAnterior, h.StatusNovo, h.MudouEm, h.Motivo);
}
