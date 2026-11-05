using Microsoft.EntityFrameworkCore;
using OficinaOs.Domain.Common;
using OficinaOs.Domain.Entities;
using OficinaOs.Infrastructure.Messaging;

namespace OficinaOs.Infrastructure.Persistence;

public class OsDbContext : DbContext
{
    public OsDbContext(DbContextOptions<OsDbContext> options) : base(options) { }

    public DbSet<OrdemServico> OrdensServico => Set<OrdemServico>();
    public DbSet<OrdemServicoItem> Itens => Set<OrdemServicoItem>();
    public DbSet<HistoricoStatusOS> Historicos => Set<HistoricoStatusOS>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    /// <summary>
    /// Outbox Pattern: os eventos de domínio levantados pelas entidades viram linhas em outbox_events
    /// DENTRO do mesmo SaveChanges (mesma transação) da mudança de estado. Ou grava tudo, ou nada.
    /// </summary>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var entities = ChangeTracker.Entries<Entity>()
            .Select(e => e.Entity)
            .Where(e => e.DomainEvents.Count > 0)
            .ToList();

        if (entities.Count > 0)
        {
            var correlationId = IntegrationEventMapper.CurrentCorrelationId();
            foreach (var evt in entities.SelectMany(e => e.DomainEvents))
            {
                var (eventType, envelope) = IntegrationEventMapper.ToEnvelope(evt, correlationId);
                OutboxMessages.Add(new OutboxMessage
                {
                    Id = evt.EventId,
                    EventType = eventType,
                    Payload = envelope,
                    OccurredAt = evt.OccurredAt
                });
            }
        }

        var result = await base.SaveChangesAsync(cancellationToken);

        // Só limpa depois de salvar com sucesso: se falhar, os eventos continuam na entidade.
        foreach (var entity in entities) entity.ClearEvents();
        return result;
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<OutboxMessage>(e =>
        {
            e.ToTable("outbox_events");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.EventType).HasMaxLength(100).IsRequired();
            e.Property(x => x.Payload).IsRequired();
            e.Property(x => x.LastError).HasMaxLength(500);
            e.HasIndex(x => new { x.ProcessedAt, x.OccurredAt });
        });

        b.Entity<OrdemServico>(e =>
        {
            e.ToTable("ordens_servico");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever(); // Guid gerado no domínio: sem isso o EF tenta UPDATE em vez de INSERT
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(40).IsRequired();
            e.Property(x => x.DefeitoRelatado).HasMaxLength(2000).IsRequired();
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.ClienteId);
            e.Ignore(x => x.DomainEvents);
            e.Ignore(x => x.PodeCancelar);

            e.HasMany(x => x.Itens).WithOne().HasForeignKey(i => i.OrdemServicoId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Historico).WithOne().HasForeignKey(h => h.OrdemServicoId).OnDelete(DeleteBehavior.Cascade);
            e.Navigation(x => x.Itens).UsePropertyAccessMode(PropertyAccessMode.Property);
            e.Navigation(x => x.Historico).UsePropertyAccessMode(PropertyAccessMode.Property);
        });

        b.Entity<OrdemServicoItem>(e =>
        {
            e.ToTable("ordem_servico_itens");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever(); // Guid gerado no domínio: sem isso o EF tenta UPDATE em vez de INSERT
            e.Property(x => x.Tipo).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Descricao).HasMaxLength(500).IsRequired();
            e.Property(x => x.PrecoUnitario).HasPrecision(12, 2);
            e.Ignore(x => x.Subtotal);
        });

        b.Entity<HistoricoStatusOS>(e =>
        {
            e.ToTable("historico_status_os");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever(); // Guid gerado no domínio: sem isso o EF tenta UPDATE em vez de INSERT
            e.Property(x => x.StatusAnterior).HasConversion<string>().HasMaxLength(40);
            e.Property(x => x.StatusNovo).HasConversion<string>().HasMaxLength(40).IsRequired();
            e.Property(x => x.AlteradoPor).HasMaxLength(100).IsRequired();
            e.Property(x => x.Motivo).HasMaxLength(500);
            e.HasIndex(x => x.OrdemServicoId);
        });
    }
}
