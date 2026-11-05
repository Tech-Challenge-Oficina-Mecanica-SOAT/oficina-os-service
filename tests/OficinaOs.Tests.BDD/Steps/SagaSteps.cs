using OficinaOs.Tests.BDD.Support;
using Reqnroll;

namespace OficinaOs.Tests.BDD.Steps;

[Binding]
public class SagaSteps
{
    private const string CpfPadrao = "12345678900";
    private const string PlacaPadrao = "ABC1D23";

    private readonly World _w;
    private readonly Guid _diagnosticoId = Guid.NewGuid();
    private readonly Guid _orcamentoId = Guid.NewGuid();
    private readonly Guid _pagamentoId = Guid.NewGuid();

    public SagaSteps(World world) => _w = world;

    // ---- Dado ------------------------------------------------------------------------------------

    [Given(@"que existe o cliente ativo com CPF ""(.*)"" e o veículo de placa ""(.*)""")]
    public void DadoClienteAtivo(string cpf, string placa)
    {
        _w.CadastrarCliente(cpf, ativo: true);
        _w.CadastrarVeiculo(placa);
    }

    [Given(@"que existe o cliente inativo com CPF ""(.*)"" e o veículo de placa ""(.*)""")]
    public void DadoClienteInativo(string cpf, string placa)
    {
        _w.CadastrarCliente(cpf, ativo: false);
        _w.CadastrarVeiculo(placa);
    }

    [Given("que existe uma OS aberta")]
    public async Task DadoOsAberta() => await _w.AbrirOsAsync(CpfPadrao, PlacaPadrao, "Defeito de teste");

    [Given("que existe uma OS com orçamento aguardando aprovação")]
    public async Task DadoOsAguardandoAprovacao()
    {
        await DadoOsAberta();
        await QuandoExecucaoConcluiDiagnostico();
        await QuandoBillingGeraOrcamento();
    }

    [Given("que existe uma OS em reparo")]
    public async Task DadoOsEmReparo()
    {
        await DadoOsAguardandoAprovacao();
        await QuandoClienteAprova();
        await QuandoExecucaoIniciaReparo();
    }

    [Given("que existe uma OS aguardando pagamento")]
    public async Task DadoOsAguardandoPagamento()
    {
        await DadoOsEmReparo();
        await QuandoExecucaoFinalizaReparo();
    }

    // ---- Quando ----------------------------------------------------------------------------------

    [When(@"^o cliente abre uma OS com o defeito ""([^""]*)""$")]
    public async Task QuandoAbreOs(string defeito) => await _w.AbrirOsAsync(CpfPadrao, PlacaPadrao, defeito);

    [When(@"^o cliente abre uma OS com o defeito ""([^""]*)"" usando o CPF ""([^""]*)""$")]
    public async Task QuandoAbreOsComCpf(string defeito, string cpf) => await _w.AbrirOsAsync(cpf, PlacaPadrao, defeito);

    [When("a execução conclui o diagnóstico")]
    public async Task QuandoExecucaoConcluiDiagnostico() =>
        await _w.PublicarEventoAsync("diagnostico.concluido", new { osId = _w.OsId, diagnosticoId = _diagnosticoId });

    [When("o billing gera o orçamento")]
    public async Task QuandoBillingGeraOrcamento() =>
        await _w.PublicarEventoAsync("orcamento.gerado", new { osId = _w.OsId, orcamentoId = _orcamentoId, valorTotal = 1250.50 });

    [When("o cliente aprova o orçamento")]
    public async Task QuandoClienteAprova() =>
        await _w.PublicarEventoAsync("orcamento.aprovado", new { osId = _w.OsId, orcamentoId = _orcamentoId });

    [When("o cliente recusa o orçamento")]
    public async Task QuandoClienteRecusa() =>
        await _w.PublicarEventoAsync("orcamento.recusado", new { osId = _w.OsId, orcamentoId = _orcamentoId, motivo = "valor_alto" });

    [When("a execução inicia o reparo")]
    public async Task QuandoExecucaoIniciaReparo() =>
        await _w.PublicarEventoAsync("execucao.iniciada", new { osId = _w.OsId, execucaoId = Guid.NewGuid() });

    [When("a execução finaliza o reparo")]
    public async Task QuandoExecucaoFinalizaReparo() =>
        await _w.PublicarEventoAsync("execucao.finalizada", new { osId = _w.OsId, execucaoId = Guid.NewGuid() });

    [When("o pagamento é confirmado")]
    public async Task QuandoPagamentoConfirmado() =>
        await _w.PublicarEventoAsync("pagamento.confirmado", new { osId = _w.OsId, pagamentoId = _pagamentoId });

    [When("o pagamento é recusado")]
    public async Task QuandoPagamentoRecusado() =>
        await _w.PublicarEventoAsync("pagamento.recusado", new { osId = _w.OsId, pagamentoId = Guid.NewGuid(), motivo = "cartao_invalido" });

    [When("o cliente é desativado")]
    public async Task QuandoClienteDesativado() =>
        await _w.PublicarEventoAsync("cliente.desativado", new { clienteId = _w.ClienteId, motivo = "inadimplencia" });

    [When(@"o mesmo evento de diagnóstico concluído chega (\d+) vezes")]
    public async Task QuandoEventoRepetido(int vezes)
    {
        var eventId = Guid.NewGuid();
        for (var i = 0; i < vezes; i++)
            await _w.PublicarEventoAsync("diagnostico.concluido", new { osId = _w.OsId, diagnosticoId = _diagnosticoId }, eventId);
    }

    [When("alguém tenta cancelar a OS")]
    public async Task QuandoTentaCancelar() => await _w.CancelarAsync();

    // ---- Então -----------------------------------------------------------------------------------

    [Then(@"a OS está com o status ""(.*)""")]
    public async Task EntaoStatus(string esperado) => Assert.Equal(esperado, await _w.StatusAsync());

    [Then(@"o histórico da OS tem (\d+) registros")]
    public async Task EntaoHistorico(int quantidade) => Assert.Equal(quantidade, await _w.QuantidadeHistoricoAsync());

    [Then(@"o evento ""(.*)"" foi enfileirado para publicação")]
    public async Task EntaoEventoNaOutbox(string eventType) =>
        Assert.Contains(eventType, await _w.EventosNaOutboxAsync());

    [Then(@"o evento ""(.*)"" foi enfileirado para publicação com o motivo ""(.*)""")]
    public async Task EntaoEventoNaOutboxComMotivo(string eventType, string motivo)
    {
        var payload = await _w.PayloadDoEventoAsync(eventType);
        Assert.NotNull(payload);
        Assert.Contains($"\"motivo\":\"{motivo}\"", payload);
    }

    [Then(@"a resposta é (\d+)")]
    public void EntaoResposta(int statusCode) => Assert.Equal(statusCode, (int)_w.LastResponse!.StatusCode);
}
