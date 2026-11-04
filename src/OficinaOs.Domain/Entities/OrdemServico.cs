using OficinaOs.Domain.Common;
using OficinaOs.Domain.Enums;

namespace OficinaOs.Domain.Entities;

/// <summary>
/// Agregado raiz da OS. Guarda apenas IDs de cliente/veículo (dados mestres vivem no Cadastros).
/// As transições de estado (máquina de estados) entram na etapa _3.
/// </summary>
public class OrdemServico : Entity
{
    public Guid Id { get; private set; }
    public StatusOS Status { get; private set; }
    public Guid ClienteId { get; private set; }
    public Guid VeiculoId { get; private set; }
    public string DefeitoRelatado { get; private set; } = string.Empty;
    public DateTime CriadaEm { get; private set; }
    public DateTime AtualizadaEm { get; private set; }

    // Referências preenchidas pelos eventos da Saga
    public Guid? DiagnosticoId { get; private set; }
    public Guid? OrcamentoId { get; private set; }
    public Guid? PagamentoId { get; private set; }

    public ICollection<OrdemServicoItem> Itens { get; private set; } = new List<OrdemServicoItem>();
    public ICollection<HistoricoStatusOS> Historico { get; private set; } = new List<HistoricoStatusOS>();

    private OrdemServico() { }

    public OrdemServico(Guid clienteId, Guid veiculoId, string defeitoRelatado)
    {
        if (clienteId == Guid.Empty) throw new ArgumentException("Cliente é obrigatório.");
        if (veiculoId == Guid.Empty) throw new ArgumentException("Veículo é obrigatório.");
        if (string.IsNullOrWhiteSpace(defeitoRelatado)) throw new ArgumentException("Defeito relatado é obrigatório.");

        Id = Guid.NewGuid();
        ClienteId = clienteId;
        VeiculoId = veiculoId;
        DefeitoRelatado = defeitoRelatado;
        CriadaEm = DateTime.UtcNow;
        AtualizadaEm = CriadaEm;
        Status = StatusOS.AguardandoDiagnostico;

        Historico.Add(new HistoricoStatusOS(Id, null, Status, "sistema", "Criação inicial"));
    }
}
