using Microsoft.EntityFrameworkCore;
using OficinaOs.Application.Abstractions;
using OficinaOs.Domain.Entities;
using OficinaOs.Domain.Enums;

namespace OficinaOs.Infrastructure.Persistence;

public class OrdemServicoRepository : IOrdemServicoRepository
{
    private readonly OsDbContext _db;

    public OrdemServicoRepository(OsDbContext db) => _db = db;

    public async Task AddAsync(OrdemServico os, CancellationToken ct = default) =>
        await _db.OrdensServico.AddAsync(os, ct);

    public Task<OrdemServico?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.OrdensServico.Include(o => o.Historico).FirstOrDefaultAsync(o => o.Id == id, ct);

    public async Task<IReadOnlyList<OrdemServico>> ListAsync(StatusOS? status, Guid? clienteId, CancellationToken ct = default)
    {
        var query = _db.OrdensServico.AsNoTracking().AsQueryable();
        if (status.HasValue) query = query.Where(o => o.Status == status.Value);
        if (clienteId.HasValue) query = query.Where(o => o.ClienteId == clienteId.Value);
        return await query.OrderByDescending(o => o.CriadaEm).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<OrdemServico>> ListarCancelaveisDoClienteAsync(Guid clienteId, CancellationToken ct = default)
    {
        var cancelaveis = new[]
        {
            StatusOS.AguardandoDiagnostico, StatusOS.EmDiagnostico, StatusOS.AguardandoAprovacao, StatusOS.Aprovada
        };
        return await _db.OrdensServico
            .Include(o => o.Historico)
            .Where(o => o.ClienteId == clienteId && cancelaveis.Contains(o.Status))
            .ToListAsync(ct);
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
