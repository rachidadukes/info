using Microsoft.EntityFrameworkCore;
using MyApp.Domain.Entities;
using MyApp.Infrastructure.DataAccess;

namespace MyApp.ReferenceData;

public sealed class HistoryService : IHistoryService
{
    private readonly IDbContextFactory<DbContextSQLite> _dbFactory;

    public HistoryService(IDbContextFactory<DbContextSQLite> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task SaveAsync(HistoryModel entry, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.History.Add(entry);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<HistoryModel>> GetAllAsync(int take = 100, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.History
            .AsNoTracking()
            .OrderByDescending(h => h.DateTimeUtc)
            .Take(take)
            .ToListAsync(ct);
    }
}
