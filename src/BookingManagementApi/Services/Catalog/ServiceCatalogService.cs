using BookingManagementApi.Contracts.Catalog;
using BookingManagementApi.Data;
using Microsoft.EntityFrameworkCore;

namespace BookingManagementApi.Services.Catalog;

public sealed class ServiceCatalogService(AppDbContext db, TimeProvider timeProvider)
{
    public Task<List<ServiceResponse>> ListAsync(bool activeOnly, bool? isActive, CancellationToken cancellationToken)
    {
        var query = db.Services.AsNoTracking();
        if (activeOnly) query = query.Where(x => x.IsActive);
        else if (isActive.HasValue) query = query.Where(x => x.IsActive == isActive.Value);
        return query.OrderBy(x => x.Name).ThenBy(x => x.Id).Select(x => Map(x)).ToListAsync(cancellationToken);
    }

    public Task<ServiceResponse?> GetAsync(Guid id, bool activeOnly, CancellationToken cancellationToken) =>
        db.Services.AsNoTracking().Where(x => x.Id == id && (!activeOnly || x.IsActive))
            .Select(x => Map(x)).SingleOrDefaultAsync(cancellationToken);

    public async Task<ServiceResponse> CreateAsync(CreateServiceRequest request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var entity = new Entities.Service
        {
            Id = Guid.NewGuid(), Name = request.Name.Trim(), Description = Normalize(request.Description),
            DurationMinutes = request.DurationMinutes, IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now
        };
        db.Services.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    public async Task<ServiceResponse?> UpdateAsync(Guid id, UpdateServiceRequest request, CancellationToken cancellationToken)
    {
        var entity = await db.Services.FindAsync([id], cancellationToken);
        if (entity is null) return null;
        entity.Name = request.Name.Trim();
        entity.Description = Normalize(request.Description);
        entity.DurationMinutes = request.DurationMinutes;
        entity.UpdatedAtUtc = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    public async Task<bool> SetActivationAsync(Guid id, bool isActive, CancellationToken cancellationToken)
    {
        var entity = await db.Services.FindAsync([id], cancellationToken);
        if (entity is null) return false;
        entity.IsActive = isActive;
        entity.UpdatedAtUtc = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    internal static ServiceResponse Map(Entities.Service entity) => new(entity.Id, entity.Name, entity.Description,
        entity.DurationMinutes, entity.IsActive, entity.CreatedAtUtc, entity.UpdatedAtUtc);
    internal static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
