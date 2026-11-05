using OficinaOs.Domain.Common;
using OficinaOs.Domain.Entities;
using OficinaOs.Domain.Enums;
using OficinaOs.Domain.Events;

namespace OficinaOs.Tests.Unit;

public class OrdemServicoStateMachineTests
{
    private static OrdemServico NovaOs() => new(Guid.NewGuid(), Guid.NewGuid(), "Barulho no motor");

    private static OrdemServico EmDiagnostico()
    {
        var os = NovaOs();
        os.RegistrarDiagnostico(Guid.NewGuid());
        return os;
    }

    private static OrdemServico AguardandoAprovacao()
    {
        var os = EmDiagnostico();
        os.RegistrarOrcamentoGerado(Guid.NewGuid());
        return os;
    }

    private static OrdemServico Aprovada()
    {
        var os = AguardandoAprovacao();
        os.AprovarOrcamento();
        return os;
    }

    private static OrdemServico EmReparo()
    {
        var os = Aprovada();
        os.IniciarReparo();
        return os;
    }

    private static OrdemServico AguardandoPagamento()
    {
        var os = EmReparo();
        os.FinalizarReparo();
        return os;
    }

    [Fact]
    public void Fluxo_feliz_percorre_todos_os_estados_e_registra_historico()
    {
        var os = NovaOs();
        var diagnosticoId = Guid.NewGuid();
        var orcamentoId = Guid.NewGuid();
        var pagamentoId = Guid.NewGuid();

        os.RegistrarDiagnostico(diagnosticoId);
        os.RegistrarOrcamentoGerado(orcamentoId);
        os.AprovarOrcamento();
        os.IniciarReparo();
        os.FinalizarReparo();
        os.ConfirmarPagamento(pagamentoId);

        Assert.Equal(StatusOS.Concluida, os.Status);
        Assert.Equal(diagnosticoId, os.DiagnosticoId);
        Assert.Equal(orcamentoId, os.OrcamentoId);
        Assert.Equal(pagamentoId, os.PagamentoId);
        // Historico é uma lista em ordem de inserção: cada passo do fluxo gera uma linha.
        Assert.Equal(
            new[]
            {
                StatusOS.AguardandoDiagnostico, StatusOS.EmDiagnostico, StatusOS.AguardandoAprovacao,
                StatusOS.Aprovada, StatusOS.EmReparo, StatusOS.AguardandoPagamento, StatusOS.Concluida
            },
            os.Historico.Select(h => h.StatusNovo).ToArray());
    }

    [Fact]
    public void Diagnostico_concluido_leva_AguardandoDiagnostico_para_EmDiagnostico()
    {
        var os = NovaOs();
        os.RegistrarDiagnostico(Guid.NewGuid());
        Assert.Equal(StatusOS.EmDiagnostico, os.Status);
    }

    [Fact]
    public void Diagnostico_reentregue_com_mesmo_id_nao_duplica_historico()
    {
        var os = NovaOs();
        var id = Guid.NewGuid();
        os.RegistrarDiagnostico(id);
        var qtd = os.Historico.Count;

        os.RegistrarDiagnostico(id);

        Assert.Equal(qtd, os.Historico.Count);
    }

    [Fact]
    public void Orcamento_gerado_leva_para_AguardandoAprovacao()
    {
        var os = EmDiagnostico();
        os.RegistrarOrcamentoGerado(Guid.NewGuid());
        Assert.Equal(StatusOS.AguardandoAprovacao, os.Status);
    }

    [Fact]
    public void Pagamento_recusado_leva_para_PendenteCobranca_e_nao_cancela()
    {
        var os = AguardandoPagamento();
        os.RecusarPagamento();
        Assert.Equal(StatusOS.PendenteCobranca, os.Status);
        Assert.False(os.PodeCancelar);
    }

    [Fact]
    public void Pagamento_confirmado_depois_resolve_PendenteCobranca()
    {
        var os = AguardandoPagamento();
        os.RecusarPagamento();
        os.ConfirmarPagamento(Guid.NewGuid());
        Assert.Equal(StatusOS.Concluida, os.Status);
    }

    [Theory]
    [InlineData("novo")]
    [InlineData("diagnostico")]
    [InlineData("aprovacao")]
    [InlineData("aprovada")]
    public void Cancelar_e_permitido_ate_Aprovada(string estado)
    {
        var os = estado switch
        {
            "novo" => NovaOs(),
            "diagnostico" => EmDiagnostico(),
            "aprovacao" => AguardandoAprovacao(),
            _ => Aprovada()
        };

        os.Cancelar(MotivoCancelamento.OrcamentoRecusado);

        Assert.Equal(StatusOS.Cancelada, os.Status);
        var evt = Assert.IsType<OsCanceladaEvent>(os.DomainEvents.Last());
        Assert.Equal(MotivoCancelamento.OrcamentoRecusado, evt.Motivo);
        Assert.Equal(os.Id, evt.OsId);
    }

    [Theory]
    [InlineData("reparo")]
    [InlineData("pagamento")]
    public void Cancelar_apos_reparo_iniciado_e_recusado(string estado)
    {
        var os = estado == "reparo" ? EmReparo() : AguardandoPagamento();
        Assert.False(os.PodeCancelar);
        Assert.Throws<DomainException>(() => os.Cancelar(MotivoCancelamento.Outro));
    }

    [Fact]
    public void Cancelar_duas_vezes_lanca_excecao()
    {
        var os = NovaOs();
        os.Cancelar(MotivoCancelamento.Outro);
        Assert.Throws<DomainException>(() => os.Cancelar(MotivoCancelamento.Outro));
    }

    [Fact]
    public void Criar_os_levanta_evento_OsCriada()
    {
        var os = NovaOs();
        var evt = Assert.IsType<OsCriadaEvent>(Assert.Single(os.DomainEvents));
        Assert.Equal(os.Id, evt.OsId);
        Assert.Equal(os.ClienteId, evt.ClienteId);
    }

    [Fact]
    public void Transicoes_fora_de_ordem_lancam_DomainException()
    {
        Assert.Throws<DomainException>(() => NovaOs().RegistrarOrcamentoGerado(Guid.NewGuid()));
        Assert.Throws<DomainException>(() => NovaOs().AprovarOrcamento());
        Assert.Throws<DomainException>(() => NovaOs().IniciarReparo());
        Assert.Throws<DomainException>(() => EmDiagnostico().FinalizarReparo());
        Assert.Throws<DomainException>(() => Aprovada().ConfirmarPagamento(Guid.NewGuid()));
        Assert.Throws<DomainException>(() => EmReparo().RecusarPagamento());
        Assert.Throws<DomainException>(() => AguardandoAprovacao().RegistrarDiagnostico(Guid.NewGuid()));
    }

    [Fact]
    public void Ids_vazios_sao_rejeitados()
    {
        Assert.Throws<ArgumentException>(() => NovaOs().RegistrarDiagnostico(Guid.Empty));
        Assert.Throws<ArgumentException>(() => EmDiagnostico().RegistrarOrcamentoGerado(Guid.Empty));
        Assert.Throws<ArgumentException>(() => AguardandoPagamento().ConfirmarPagamento(Guid.Empty));
    }

    [Fact]
    public void Motivo_de_cancelamento_segue_o_contrato_de_eventos()
    {
        Assert.Equal("orcamento_recusado", MotivoCancelamento.OrcamentoRecusado.ToContrato());
        Assert.Equal("cliente_desativado", MotivoCancelamento.ClienteDesativado.ToContrato());
        Assert.Equal("outro", MotivoCancelamento.Outro.ToContrato());
    }
}
