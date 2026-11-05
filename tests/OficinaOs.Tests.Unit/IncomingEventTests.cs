using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OficinaOs.Application.Events;
using OficinaOs.Domain.Entities;
using OficinaOs.Domain.Enums;
using OficinaOs.Infrastructure.Messaging;
using OficinaOs.Infrastructure.Persistence;

namespace OficinaOs.Tests.Unit;

internal static class Env
{
    public static string Body(string eventType, object data, Guid? eventId = null, string? correlation = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01") =>
        JsonSerializer.Serialize(new
        {
            eventId = eventId ?? Guid.NewGuid(), eventType, eventVersion = "1.0",
            occurredAt = DateTime.UtcNow, correlationId = correlation, producer = "teste", data
        });

    public static string SnsWrapped(string envelope) =>
        JsonSerializer.Serialize(new { Type = "Notification", MessageId = Guid.NewGuid(), Message = envelope });
}

public class IncomingEventParserTests
{
    [Fact]
    public void Le_envelope_direto()
    {
        var id = Guid.NewGuid();
        var evt = IncomingEventParser.Parse(Env.Body("orcamento.aprovado", new { osId = Guid.NewGuid() }, id));

        Assert.Equal(id, evt.EventId);
        Assert.Equal("orcamento.aprovado", evt.EventType);
        Assert.Equal("00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01", evt.CorrelationId);
        Assert.True(evt.Data.TryGetProperty("osId", out _));
    }

    [Fact]
    public void Desembrulha_envelope_do_SNS()
    {
        var id = Guid.NewGuid();
        var envelope = Env.Body("pagamento.confirmado", new { osId = Guid.NewGuid() }, id);

        var evt = IncomingEventParser.Parse(Env.SnsWrapped(envelope));

        Assert.Equal(id, evt.EventId);
        Assert.Equal("pagamento.confirmado", evt.EventType);
    }

    [Fact]
    public void Correlation_ausente_vira_null() =>
        Assert.Null(IncomingEventParser.Parse(Env.Body("orcamento.aprovado", new { osId = Guid.NewGuid() }, correlation: null)).CorrelationId);

    [Theory]
    [InlineData("isso nao e json")]
    [InlineData("[]")]
    [InlineData("{\"eventType\":\"x\",\"data\":{}}")]                                          // sem eventId
    [InlineData("{\"eventId\":\"nao-guid\",\"eventType\":\"x\",\"data\":{}}")]                // eventId inválido
    [InlineData("{\"eventId\":\"3f2504e0-4f89-11d3-9a0c-0305e82c3301\",\"data\":{}}")]        // sem eventType
    [InlineData("{\"eventId\":\"3f2504e0-4f89-11d3-9a0c-0305e82c3301\",\"eventType\":\"x\"}")] // sem data
    public void Mensagem_malformada_lanca_MalformedEventException(string body) =>
        Assert.Throws<MalformedEventException>(() => IncomingEventParser.Parse(body));
}

/// <summary>Handler com repositório e store em memória (EF InMemory): exercita a Saga de ponta a ponta sem mensageria real.</summary>
public class IncomingEventHandlerTests
{
    private readonly OsDbContext _db =
        new(new DbContextOptionsBuilder<OsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private IncomingEventHandler Handler() =>
        new(new OrdemServicoRepository(_db), new ProcessedEventStore(_db), NullLogger<IncomingEventHandler>.Instance);

    private async Task<OrdemServico> NovaOs(Guid? clienteId = null)
    {
        var os = new OrdemServico(clienteId ?? Guid.NewGuid(), Guid.NewGuid(), "Defeito");
        _db.OrdensServico.Add(os);
        await _db.SaveChangesAsync();
        return os;
    }

    private async Task<HandleResult> Send(string type, object data, Guid? eventId = null) =>
        await Handler().HandleAsync(IncomingEventParser.Parse(Env.Body(type, data, eventId)));

    private async Task<OrdemServico> Reload(Guid id)
    {
        _db.ChangeTracker.Clear();
        return await _db.OrdensServico.Include(o => o.Historico).SingleAsync(o => o.Id == id);
    }

    [Fact]
    public async Task Fluxo_feliz_pela_Saga_leva_a_OS_ate_Concluida()
    {
        var os = await NovaOs();
        var diag = Guid.NewGuid(); var orc = Guid.NewGuid(); var pag = Guid.NewGuid();

        Assert.Equal(HandleResult.Applied, await Send("diagnostico.concluido", new { osId = os.Id, diagnosticoId = diag }));
        Assert.Equal(HandleResult.Applied, await Send("orcamento.gerado", new { osId = os.Id, orcamentoId = orc }));
        Assert.Equal(HandleResult.Applied, await Send("orcamento.aprovado", new { osId = os.Id, orcamentoId = orc }));
        Assert.Equal(HandleResult.Applied, await Send("execucao.iniciada", new { osId = os.Id }));
        Assert.Equal(HandleResult.Applied, await Send("execucao.finalizada", new { osId = os.Id }));
        Assert.Equal(HandleResult.Applied, await Send("pagamento.confirmado", new { osId = os.Id, pagamentoId = pag }));

        var final = await Reload(os.Id);
        Assert.Equal(StatusOS.Concluida, final.Status);
        Assert.Equal(diag, final.DiagnosticoId);
        Assert.Equal(orc, final.OrcamentoId);
        Assert.Equal(pag, final.PagamentoId);
        Assert.Equal(7, final.Historico.Count);
    }

    [Fact]
    public async Task Orcamento_recusado_cancela_a_OS_e_publica_os_cancelada_pela_outbox()
    {
        var os = await NovaOs();
        await Send("diagnostico.concluido", new { osId = os.Id, diagnosticoId = Guid.NewGuid() });
        await Send("orcamento.gerado", new { osId = os.Id, orcamentoId = Guid.NewGuid() });

        var result = await Send("orcamento.recusado", new { osId = os.Id, orcamentoId = Guid.NewGuid(), motivo = "valor_alto" });

        Assert.Equal(HandleResult.Applied, result);
        Assert.Equal(StatusOS.Cancelada, (await Reload(os.Id)).Status);
        var cancelada = await _db.OutboxMessages.SingleAsync(m => m.EventType == "os.cancelada");
        Assert.Contains("orcamento_recusado", cancelada.Payload);
    }

    [Fact]
    public async Task Evento_duplicado_nao_e_aplicado_duas_vezes()
    {
        var os = await NovaOs();
        var eventId = Guid.NewGuid();
        var data = new { osId = os.Id, diagnosticoId = Guid.NewGuid() };

        Assert.Equal(HandleResult.Applied, await Send("diagnostico.concluido", data, eventId));
        Assert.Equal(HandleResult.Duplicate, await Send("diagnostico.concluido", data, eventId));

        Assert.Equal(2, (await Reload(os.Id)).Historico.Count); // criação + EmDiagnostico, sem repetir
        Assert.Equal(1, await _db.ProcessedEvents.CountAsync(p => p.EventId == eventId));
    }

    [Fact]
    public async Task Evento_fora_de_ordem_e_ignorado_sem_erro_e_registrado()
    {
        var os = await NovaOs(); // AguardandoDiagnostico
        var eventId = Guid.NewGuid();

        var result = await Send("execucao.finalizada", new { osId = os.Id }, eventId);

        Assert.Equal(HandleResult.Ignored, result);
        Assert.Equal(StatusOS.AguardandoDiagnostico, (await Reload(os.Id)).Status);
        var registro = await _db.ProcessedEvents.SingleAsync(p => p.EventId == eventId);
        Assert.Equal("Ignored", registro.Outcome);
    }

    [Fact]
    public async Task Evento_ignorado_tambem_nao_e_reprocessado()
    {
        var os = await NovaOs();
        var eventId = Guid.NewGuid();
        await Send("execucao.finalizada", new { osId = os.Id }, eventId);

        Assert.Equal(HandleResult.Duplicate, await Send("execucao.finalizada", new { osId = os.Id }, eventId));
    }

    [Fact]
    public async Task Evento_para_os_inexistente_e_ignorado() =>
        Assert.Equal(HandleResult.Ignored, await Send("orcamento.aprovado", new { osId = Guid.NewGuid() }));

    [Fact]
    public async Task Tipo_de_evento_desconhecido_e_ignorado() =>
        Assert.Equal(HandleResult.Ignored, await Send("evento.inventado", new { osId = Guid.NewGuid() }));

    [Fact]
    public async Task Evento_sem_osId_lanca_MalformedEvent_para_ir_a_DLQ()
    {
        await Assert.ThrowsAsync<MalformedEventException>(() => Send("orcamento.aprovado", new { foo = "bar" }));
        Assert.Equal(0, await _db.ProcessedEvents.CountAsync()); // não registra: deve ser retentado/DLQ
    }

    [Fact]
    public async Task Diagnostico_sem_diagnosticoId_lanca_MalformedEvent_e_nao_altera_a_OS()
    {
        var os = await NovaOs();
        await Assert.ThrowsAsync<MalformedEventException>(() => Send("diagnostico.concluido", new { osId = os.Id }));
        Assert.Equal(StatusOS.AguardandoDiagnostico, (await Reload(os.Id)).Status);
    }

    [Fact]
    public async Task Pagamento_recusado_leva_a_PendenteCobranca_e_confirmado_depois_conclui()
    {
        var os = await NovaOs();
        await Send("diagnostico.concluido", new { osId = os.Id, diagnosticoId = Guid.NewGuid() });
        await Send("orcamento.gerado", new { osId = os.Id, orcamentoId = Guid.NewGuid() });
        await Send("orcamento.aprovado", new { osId = os.Id });
        await Send("execucao.iniciada", new { osId = os.Id });
        await Send("execucao.finalizada", new { osId = os.Id });

        await Send("pagamento.recusado", new { osId = os.Id, pagamentoId = Guid.NewGuid(), motivo = "cartao_invalido" });
        Assert.Equal(StatusOS.PendenteCobranca, (await Reload(os.Id)).Status);

        await Send("pagamento.confirmado", new { osId = os.Id, pagamentoId = Guid.NewGuid() });
        Assert.Equal(StatusOS.Concluida, (await Reload(os.Id)).Status);
    }

    [Fact]
    public async Task Orcamento_recusado_depois_do_reparo_comecar_nao_cancela()
    {
        var os = await NovaOs();
        await Send("diagnostico.concluido", new { osId = os.Id, diagnosticoId = Guid.NewGuid() });
        await Send("orcamento.gerado", new { osId = os.Id, orcamentoId = Guid.NewGuid() });
        await Send("orcamento.aprovado", new { osId = os.Id });
        await Send("execucao.iniciada", new { osId = os.Id });

        var result = await Send("orcamento.recusado", new { osId = os.Id });

        Assert.Equal(HandleResult.Ignored, result);
        Assert.Equal(StatusOS.EmReparo, (await Reload(os.Id)).Status);
    }

    [Fact]
    public async Task Cliente_desativado_cancela_apenas_as_OS_canceláveis_do_cliente()
    {
        var clienteId = Guid.NewGuid();
        var aberta1 = await NovaOs(clienteId);
        var aberta2 = await NovaOs(clienteId);
        var emReparo = await NovaOs(clienteId);
        var outroCliente = await NovaOs();

        await Send("diagnostico.concluido", new { osId = emReparo.Id, diagnosticoId = Guid.NewGuid() });
        await Send("orcamento.gerado", new { osId = emReparo.Id, orcamentoId = Guid.NewGuid() });
        await Send("orcamento.aprovado", new { osId = emReparo.Id });
        await Send("execucao.iniciada", new { osId = emReparo.Id });

        var result = await Send("cliente.desativado", new { clienteId, motivo = "inadimplencia" });

        Assert.Equal(HandleResult.Applied, result);
        Assert.Equal(StatusOS.Cancelada, (await Reload(aberta1.Id)).Status);
        Assert.Equal(StatusOS.Cancelada, (await Reload(aberta2.Id)).Status);
        Assert.Equal(StatusOS.EmReparo, (await Reload(emReparo.Id)).Status);
        Assert.Equal(StatusOS.AguardandoDiagnostico, (await Reload(outroCliente.Id)).Status);
        Assert.Equal(2, await _db.OutboxMessages.CountAsync(m => m.EventType == "os.cancelada"));
        Assert.All(_db.OutboxMessages.Where(m => m.EventType == "os.cancelada"),
            m => Assert.Contains("cliente_desativado", m.Payload));
    }

    [Fact]
    public async Task Cliente_desativado_sem_OS_abertas_e_ignorado() =>
        Assert.Equal(HandleResult.Ignored, await Send("cliente.desativado", new { clienteId = Guid.NewGuid() }));

    [Fact]
    public void Lista_de_eventos_suportados_cobre_os_9_do_contrato() =>
        Assert.Equal(9, IncomingEventHandler.SupportedEvents.Length);
}

public class SqsMessageProcessorTests
{
    [Fact]
    public async Task Processa_mensagem_e_o_os_cancelada_herda_o_correlationId_do_evento_recebido()
    {
        var dbName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<OsDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddScoped<OficinaOs.Application.Abstractions.IOrdemServicoRepository, OrdemServicoRepository>();
        services.AddScoped<OficinaOs.Application.Abstractions.IProcessedEventStore, ProcessedEventStore>();
        services.AddScoped<IncomingEventHandler>();
        var provider = services.BuildServiceProvider();

        Guid osId;
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OsDbContext>();
            var os = new OrdemServico(Guid.NewGuid(), Guid.NewGuid(), "x");
            db.OrdensServico.Add(os);
            await db.SaveChangesAsync();
            osId = os.Id;
        }

        const string correlation = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";
        var processor = new SqsMessageProcessor(provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SqsMessageProcessor>.Instance);

        var result = await processor.ProcessAsync(Env.Body("orcamento.recusado", new { osId }, correlation: correlation));

        Assert.Equal(HandleResult.Applied, result);
        using var check = provider.CreateScope();
        var cancelada = await check.ServiceProvider.GetRequiredService<OsDbContext>().OutboxMessages
            .SingleAsync(m => m.EventType == "os.cancelada");
        // mesmo trace id (4bf92f35...) — o fluxo continua rastreável entre os serviços
        Assert.Contains("4bf92f3577b34da6a3ce929d0e0e4736", cancelada.Payload);
    }

    [Fact]
    public async Task Mensagem_malformada_propaga_excecao_para_a_mensagem_nao_ser_apagada()
    {
        var provider = new ServiceCollection().AddLogging().BuildServiceProvider();
        var processor = new SqsMessageProcessor(provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SqsMessageProcessor>.Instance);

        await Assert.ThrowsAsync<MalformedEventException>(() => processor.ProcessAsync("lixo"));
    }
}
