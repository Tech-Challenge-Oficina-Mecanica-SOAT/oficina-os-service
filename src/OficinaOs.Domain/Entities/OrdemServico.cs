using OficinaOs.Domain.Common;
using OficinaOs.Domain.Enums;
using OficinaOs.Domain.Events;

namespace OficinaOs.Domain.Entities;

/// <summary>
/// Agregado raiz da OS e dono da máquina de estados da Saga.
/// Guarda apenas IDs de cliente/veículo (dados mestres vivem no Cadastros).
///
/// Fluxo feliz:
/// AguardandoDiagnostico → EmDiagnostico → AguardandoAprovacao → Aprovada → EmReparo → AguardandoPagamento → Concluida
/// Compensação: Cancelada (até Aprovada) · Falha de pagamento: PendenteCobranca → Concluida
/// </summary>
public class OrdemServico : Entity
{
    private const string Sistema = "sistema";

    private static readonly StatusOS[] CancelaveisEm =
    {
        StatusOS.AguardandoDiagnostico, StatusOS.EmDiagnostico, StatusOS.AguardandoAprovacao, StatusOS.Aprovada
    };

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

        Historico.Add(new HistoricoStatusOS(Id, null, Status, Sistema, "Criação inicial"));
        RaiseEvent(new OsCriadaEvent(Id, ClienteId, VeiculoId, DefeitoRelatado, CriadaEm));
    }

    public bool PodeCancelar => CancelaveisEm.Contains(Status);

    /// <summary>
    /// diagnostico.concluido: não existe evento de "diagnóstico iniciado" no contrato, então a OS entra
    /// em EmDiagnostico aqui e guarda o diagnosticoId. Reentrega do mesmo diagnóstico é tolerada.
    /// </summary>
    public void RegistrarDiagnostico(Guid diagnosticoId, string alteradoPor = Sistema)
    {
        if (diagnosticoId == Guid.Empty) throw new ArgumentException("DiagnosticoId é obrigatório.");

        if (Status == StatusOS.EmDiagnostico && DiagnosticoId == diagnosticoId) return;

        Transitar(new[] { StatusOS.AguardandoDiagnostico }, StatusOS.EmDiagnostico, alteradoPor,
            "Diagnóstico concluído pela execução");
        DiagnosticoId = diagnosticoId;
    }

    /// <summary>orcamento.gerado → aguarda decisão do cliente.</summary>
    public void RegistrarOrcamentoGerado(Guid orcamentoId, string alteradoPor = Sistema)
    {
        if (orcamentoId == Guid.Empty) throw new ArgumentException("OrcamentoId é obrigatório.");
        Transitar(new[] { StatusOS.EmDiagnostico }, StatusOS.AguardandoAprovacao, alteradoPor, "Orçamento gerado");
        OrcamentoId = orcamentoId;
    }

    /// <summary>orcamento.aprovado.</summary>
    public void AprovarOrcamento(string alteradoPor = Sistema) =>
        Transitar(new[] { StatusOS.AguardandoAprovacao }, StatusOS.Aprovada, alteradoPor, "Orçamento aprovado");

    /// <summary>execucao.iniciada.</summary>
    public void IniciarReparo(string alteradoPor = Sistema) =>
        Transitar(new[] { StatusOS.Aprovada }, StatusOS.EmReparo, alteradoPor, "Reparo iniciado");

    /// <summary>execucao.finalizada.</summary>
    public void FinalizarReparo(string alteradoPor = Sistema) =>
        Transitar(new[] { StatusOS.EmReparo }, StatusOS.AguardandoPagamento, alteradoPor, "Reparo finalizado");

    /// <summary>pagamento.confirmado (também resolve PendenteCobranca).</summary>
    public void ConfirmarPagamento(Guid pagamentoId, string alteradoPor = Sistema)
    {
        if (pagamentoId == Guid.Empty) throw new ArgumentException("PagamentoId é obrigatório.");
        Transitar(new[] { StatusOS.AguardandoPagamento, StatusOS.PendenteCobranca }, StatusOS.Concluida,
            alteradoPor, "Pagamento confirmado");
        PagamentoId = pagamentoId;
    }

    /// <summary>pagamento.recusado — o serviço já foi feito, então não cancela: fica pendente de cobrança.</summary>
    public void RecusarPagamento(string alteradoPor = Sistema) =>
        Transitar(new[] { StatusOS.AguardandoPagamento }, StatusOS.PendenteCobranca, alteradoPor, "Pagamento recusado");

    /// <summary>Compensação da Saga. Só até Aprovada; depois que o reparo começa não cancela.</summary>
    public void Cancelar(MotivoCancelamento motivo, string alteradoPor = Sistema)
    {
        Transitar(CancelaveisEm, StatusOS.Cancelada, alteradoPor, $"Cancelada: {motivo.ToContrato()}");
        RaiseEvent(new OsCanceladaEvent(Id, motivo, AtualizadaEm));
    }

    private void Transitar(StatusOS[] permitidosDe, StatusOS novo, string alteradoPor, string motivo)
    {
        if (!permitidosDe.Contains(Status))
            throw new DomainException(
                $"Transição inválida: OS {Id} está em {Status} e não pode ir para {novo}.");

        var anterior = Status;
        Status = novo;
        AtualizadaEm = DateTime.UtcNow;
        Historico.Add(new HistoricoStatusOS(Id, anterior, novo, alteradoPor, motivo));
    }
}
