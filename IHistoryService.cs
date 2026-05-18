using MyApp.Domain.Entities;

namespace MyApp.ReferenceData;

public interface IHistoryService
{
    Task SaveAsync(HistoryModel entry, CancellationToken ct = default);
    Task<IReadOnlyList<HistoryModel>> GetAllAsync(int take = 100, CancellationToken ct = default);
}
