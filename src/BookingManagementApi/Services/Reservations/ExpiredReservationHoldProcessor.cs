using BookingManagementApi.Data;
using BookingManagementApi.Enums;
using Microsoft.EntityFrameworkCore;

namespace BookingManagementApi.Services.Reservations;

public sealed class ExpiredReservationHoldProcessor(AppDbContext db, TimeProvider timeProvider)
{
    public const int BatchSize = 100;

    public async Task<int> ProcessBatchAsync(CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var candidateIds = await db.Reservations
            .Where(x => x.Status == ReservationStatus.Held && x.HoldExpiresAtUtc <= now)
            .OrderBy(x => x.HoldExpiresAtUtc)
            .ThenBy(x => x.Id)
            .Select(x => x.Id)
            .Take(BatchSize)
            .ToArrayAsync(cancellationToken);

        if (candidateIds.Length == 0)
            return 0;

        return await db.Reservations
            .Where(x => candidateIds.Contains(x.Id)
                && x.Status == ReservationStatus.Held
                && x.HoldExpiresAtUtc <= now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, ReservationStatus.Expired)
                .SetProperty(x => x.UpdatedAtUtc, now), cancellationToken);
    }
}
