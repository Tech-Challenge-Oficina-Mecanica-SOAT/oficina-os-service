using OficinaOs.Domain.Enums;

namespace OficinaOs.Domain.Entities;

public class HistoricoStatusOS
{
    public Guid Id { get; private set; }
    public Guid OrdemServicoId { get; private set; }
    public StatusOS? StatusAnterior { get; private set; }
    public StatusOS StatusNovo { get; private set; }
    public DateTime MudouEm { get; private set; }
    public string AlteradoPor { get; private set; } = string.Empty;
    public string Motivo { get; private set; } = string.Empty;

    private HistoricoStatusOS() { }

    public HistoricoStatusOS(Guid ordemServicoId, StatusOS? anterior, StatusOS novo, string alteradoPor, string motivo)
    {
        if (ordemServicoId == Guid.Empty)
            throw new ArgumentException("OrdemServicoId é obrigatório.");
        if (string.IsNullOrWhiteSpace(alteradoPor))
            throw new ArgumentException("AlteradoPor é obrigatório.");
        if (anterior.HasValue && anterior.Value == novo)
            throw new ArgumentException("StatusAnterior e StatusNovo não podem ser o mesmo.");

        Id = Guid.NewGuid();
        OrdemServicoId = ordemServicoId;
        StatusAnterior = anterior;
        StatusNovo = novo;
        MudouEm = DateTime.UtcNow;
        AlteradoPor = alteradoPor;
        Motivo = motivo ?? string.Empty;
    }
}
