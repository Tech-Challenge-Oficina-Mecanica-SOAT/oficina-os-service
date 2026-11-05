using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OficinaOs.Application.Abstractions;
using OficinaOs.Domain.Entities;
using OficinaOs.Domain.Enums;
using OficinaOs.Infrastructure.Messaging;
using OficinaOs.Infrastructure.Persistence;

namespace OficinaOs.Tests.Unit;

internal sealed class FakePublisher : IEventPublisher
{
    public List<(string EventType, string Envelope)> Sent { get; } = new();
    public Func<string, Exception?>? FailWhen { get; set; }

    public Task PublishAsync(string eventType, string envelopeJson, CancellationToken ct = default)
    {
        var failure = FailWhen?.Invoke(eventType);
        if (failure is not null) throw failure;
        Sent.Add((eventType, envelopeJson));
        return Task.CompletedTask;
    }
}

public class IntegrationEventMapperTests
{
    private static readonly string Correlation = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";

    [Fact]
    public void Os_criada_segue_o_contrato_do_schemas_md()
    {
        var os = new OrdemServico(Guid.NewGuid(), Guid.NewGuid(), "Barulho no motor");

        var (type, json) = IntegrationEventMapper.ToEnvelope(os.DomainEvents.Single(), Correlation);

        Assert.Equal("os.criada", type);
        var root = JsonDocument.Parse(json).RootElement;
        Assert.Equal("os.criada", root.GetProperty("eventType").GetString());
        Assert.Equal("1.0", root.GetProperty("eventVersion").GetString());
        Assert.Equal("os-service", root.GetProperty("producer").GetString());
        Assert.Equal(Correlation, root.GetProperty("correlationId").GetString());
        Assert.True(Guid.TryParse(root.GetProperty("eventId").GetString(), out _));
        Assert.True(root.TryGetProperty("occurredAt", out _));
        var data = root.GetProperty("data");
        Assert.Equal(os.Id, data.GetProperty("osId").GetGuid());
        Assert.Equal(os.ClienteId, data.GetProperty("clienteId").GetGuid());
        Assert.Equal(os.VeiculoId, data.GetProperty("veiculoId").GetGuid());
        Assert.Equal("Barulho no motor", data.GetProperty("defeitoRelatado").GetString());
        Assert.True(data.TryGetProperty("criadaEm", out _));
    }

    [Theory]
    [InlineData(MotivoCancelamento.OrcamentoRecusado, "orcamento_recusado")]
    [InlineData(MotivoCancelamento.ClienteDesativado, "cliente_desativado")]
    [InlineData(MotivoCancelamento.Outro, "outro")]
    public void Os_cancelada_usa_o_motivo_do_contrato(MotivoCancelamento motivo, string esperado)
    {
        var os = new OrdemServico(Guid.NewGuid(), Guid.NewGuid(), "x");
        os.Cancelar(motivo);

        var (type, json) = IntegrationEventMapper.ToEnvelope(os.DomainEvents.Last(), Correlation);

        Assert.Equal("os.cancelada", type);
        var data = JsonDocument.Parse(json).RootElement.GetProperty("data");
        Assert.Equal(os.Id, data.GetProperty("osId").GetGuid());
        Assert.Equal(esperado, data.GetProperty("motivo").GetString());
        Assert.True(data.TryGetProperty("canceladaEm", out _));
    }

    [Fact]
    public void CorrelationId_gerado_fora_de_requisicao_tem_formato_traceparent()
    {
        var id = IntegrationEventMapper.CurrentCorrelationId();
        Assert.Matches("^00-[0-9a-f]{32}-[0-9a-f]{16}-0[01]$", id);
    }
}

public class OutboxPersistenceTests
{
    private static OsDbContext NewDb() =>
        new(new DbContextOptionsBuilder<OsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task Criar_os_grava_evento_os_criada_na_outbox_junto_com_a_os()
    {
        await using var db = NewDb();
        var os = new OrdemServico(Guid.NewGuid(), Guid.NewGuid(), "x");
        db.OrdensServico.Add(os);

        await db.SaveChangesAsync();

        var msg = await db.OutboxMessages.SingleAsync();
        Assert.Equal("os.criada", msg.EventType);
        Assert.Null(msg.ProcessedAt);
        Assert.Contains(os.Id.ToString(), msg.Payload);
        Assert.Empty(os.DomainEvents); // eventos limpos depois de salvar
    }

    [Fact]
    public async Task Salvar_de_novo_sem_evento_novo_nao_duplica_a_outbox()
    {
        await using var db = NewDb();
        var os = new OrdemServico(Guid.NewGuid(), Guid.NewGuid(), "x");
        db.OrdensServico.Add(os);
        await db.SaveChangesAsync();

        await db.SaveChangesAsync();

        Assert.Equal(1, await db.OutboxMessages.CountAsync());
    }

    [Fact]
    public async Task Cancelar_adiciona_os_cancelada_a_outbox()
    {
        await using var db = NewDb();
        var os = new OrdemServico(Guid.NewGuid(), Guid.NewGuid(), "x");
        db.OrdensServico.Add(os);
        await db.SaveChangesAsync();

        os.Cancelar(MotivoCancelamento.OrcamentoRecusado);
        await db.SaveChangesAsync();

        var tipos = await db.OutboxMessages.OrderBy(m => m.OccurredAt).Select(m => m.EventType).ToListAsync();
        Assert.Equal(new[] { "os.criada", "os.cancelada" }, tipos);
    }

    [Fact]
    public async Task Id_da_outbox_e_o_eventId_do_envelope()
    {
        await using var db = NewDb();
        db.OrdensServico.Add(new OrdemServico(Guid.NewGuid(), Guid.NewGuid(), "x"));
        await db.SaveChangesAsync();

        var msg = await db.OutboxMessages.SingleAsync();
        var eventId = JsonDocument.Parse(msg.Payload).RootElement.GetProperty("eventId").GetGuid();
        Assert.Equal(msg.Id, eventId);
    }
}

public class OutboxProcessorTests
{
    private readonly OsDbContext _db =
        new(new DbContextOptionsBuilder<OsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly FakePublisher _publisher = new();
    private readonly MessagingOptions _options = new() { Enabled = true, MaxAttempts = 3, BatchSize = 10 };

    private OutboxProcessor Sut() =>
        new(_db, _publisher, Options.Create(_options), NullLogger<OutboxProcessor>.Instance);

    private async Task SeedAsync(params (string Type, int MinutosAtras)[] eventos)
    {
        foreach (var (type, minutos) in eventos)
            _db.OutboxMessages.Add(new OutboxMessage
            {
                Id = Guid.NewGuid(), EventType = type, Payload = $"{{\"t\":\"{type}\"}}",
                OccurredAt = DateTime.UtcNow.AddMinutes(-minutos)
            });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Publica_pendentes_em_ordem_e_marca_como_processados()
    {
        await SeedAsync(("os.cancelada", 1), ("os.criada", 5));

        var qtd = await Sut().ProcessBatchAsync();

        Assert.Equal(2, qtd);
        Assert.Equal(new[] { "os.criada", "os.cancelada" }, _publisher.Sent.Select(s => s.EventType));
        Assert.All(_db.OutboxMessages, m => Assert.NotNull(m.ProcessedAt));
    }

    [Fact]
    public async Task Nao_republica_o_que_ja_foi_processado()
    {
        await SeedAsync(("os.criada", 1));
        await Sut().ProcessBatchAsync();

        var segunda = await Sut().ProcessBatchAsync();

        Assert.Equal(0, segunda);
        Assert.Single(_publisher.Sent);
    }

    [Fact]
    public async Task Falha_mantem_pendente_conta_tentativa_e_preserva_ordem()
    {
        await SeedAsync(("os.criada", 5), ("os.cancelada", 1));
        _publisher.FailWhen = type => type == "os.criada" ? new InvalidOperationException("SNS fora do ar") : null;

        var qtd = await Sut().ProcessBatchAsync();

        Assert.Equal(0, qtd);
        Assert.Empty(_publisher.Sent); // o segundo não passa na frente do primeiro
        var criada = _db.OutboxMessages.Single(m => m.EventType == "os.criada");
        Assert.Null(criada.ProcessedAt);
        Assert.Equal(1, criada.Attempts);
        Assert.Equal("SNS fora do ar", criada.LastError);
    }

    [Fact]
    public async Task Recupera_quando_o_publisher_volta()
    {
        await SeedAsync(("os.criada", 1));
        _publisher.FailWhen = _ => new InvalidOperationException("falha");
        await Sut().ProcessBatchAsync();

        _publisher.FailWhen = null;
        var qtd = await Sut().ProcessBatchAsync();

        Assert.Equal(1, qtd);
        var msg = _db.OutboxMessages.Single();
        Assert.NotNull(msg.ProcessedAt);
        Assert.Null(msg.LastError);
    }

    [Fact]
    public async Task Estaciona_apos_MaxAttempts_e_para_de_tentar()
    {
        await SeedAsync(("os.criada", 1));
        _publisher.FailWhen = _ => new InvalidOperationException("falha");
        var sut = Sut();

        for (var i = 0; i < 5; i++) await sut.ProcessBatchAsync();

        var msg = _db.OutboxMessages.Single();
        Assert.Equal(3, msg.Attempts);
        Assert.Null(msg.ProcessedAt);
    }

    [Fact]
    public async Task Respeita_o_tamanho_do_lote()
    {
        _options.BatchSize = 2;
        await SeedAsync(("os.criada", 3), ("os.criada", 2), ("os.criada", 1));

        Assert.Equal(2, await Sut().ProcessBatchAsync());
        Assert.Equal(1, await Sut().ProcessBatchAsync());
    }
}
