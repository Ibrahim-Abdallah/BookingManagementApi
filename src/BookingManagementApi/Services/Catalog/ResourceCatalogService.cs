using BookingManagementApi.Contracts.Catalog;
using BookingManagementApi.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BookingManagementApi.Services.Catalog;

public enum AssignmentResult { Success, ResourceNotFound, ServiceNotFound, AssignmentNotFound, Conflict }

public sealed class ResourceCatalogService(AppDbContext db, TimeProvider timeProvider)
{
    public Task<List<ResourceResponse>> ListAsync(bool activeOnly, bool? isActive, CancellationToken cancellationToken)
    {
        var query = db.Resources.AsNoTracking();
        if (activeOnly) query = query.Where(x => x.IsActive);
        else if (isActive.HasValue) query = query.Where(x => x.IsActive == isActive.Value);
        return query.OrderBy(x => x.Name).ThenBy(x => x.Id).Select(x => Map(x)).ToListAsync(cancellationToken);
    }

    public Task<ResourceResponse?> GetAsync(Guid id, bool activeOnly, CancellationToken cancellationToken) =>
        db.Resources.AsNoTracking().Where(x => x.Id == id && (!activeOnly || x.IsActive))
            .Select(x => Map(x)).SingleOrDefaultAsync(cancellationToken);

    public async Task<ResourceResponse> CreateAsync(CreateResourceRequest request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var entity = new Entities.Resource { Id = Guid.NewGuid(), Name = request.Name.Trim(),
            Description = ServiceCatalogService.Normalize(request.Description), IsActive = true,
            CreatedAtUtc = now, UpdatedAtUtc = now };
        db.Resources.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    public async Task<ResourceResponse?> UpdateAsync(Guid id, UpdateResourceRequest request, CancellationToken cancellationToken)
    {
        var entity = await db.Resources.FindAsync([id], cancellationToken);
        if (entity is null) return null;
        entity.Name = request.Name.Trim();
        entity.Description = ServiceCatalogService.Normalize(request.Description);
        entity.UpdatedAtUtc = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    public async Task<bool> SetActivationAsync(Guid id, bool isActive, CancellationToken cancellationToken)
    {
        var entity = await db.Resources.FindAsync([id], cancellationToken);
        if (entity is null) return false;
        entity.IsActive = isActive;
        entity.UpdatedAtUtc = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<(AssignmentResult Result, List<ServiceResponse>? Services)> GetServicesAsync(Guid resourceId, CancellationToken cancellationToken)
    {
        if (!await db.Resources.AsNoTracking().AnyAsync(x => x.Id == resourceId, cancellationToken))
            return (AssignmentResult.ResourceNotFound, null);
        var services = await db.ResourceServices.AsNoTracking().Where(x => x.ResourceId == resourceId)
            .Select(x => x.Service).OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Select(x => ServiceCatalogService.Map(x)).ToListAsync(cancellationToken);
        return (AssignmentResult.Success, services);
    }

    public async Task<AssignmentResult> AssignAsync(Guid resourceId, Guid serviceId, CancellationToken cancellationToken)
    {
        if (!await db.Resources.AsNoTracking().AnyAsync(x => x.Id == resourceId, cancellationToken)) return AssignmentResult.ResourceNotFound;
        if (!await db.Services.AsNoTracking().AnyAsync(x => x.Id == serviceId, cancellationToken)) return AssignmentResult.ServiceNotFound;
        if (await db.ResourceServices.AsNoTracking().AnyAsync(x => x.ResourceId == resourceId && x.ServiceId == serviceId, cancellationToken)) return AssignmentResult.Conflict;
        db.ResourceServices.Add(new Entities.ResourceService { ResourceId = resourceId, ServiceId = serviceId });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 }) { return AssignmentResult.Conflict; }
        return AssignmentResult.Success;
    }

    public async Task<AssignmentResult> RemoveAssignmentAsync(Guid resourceId, Guid serviceId, CancellationToken cancellationToken)
    {
        if (!await db.Resources.AsNoTracking().AnyAsync(x => x.Id == resourceId, cancellationToken)) return AssignmentResult.ResourceNotFound;
        if (!await db.Services.AsNoTracking().AnyAsync(x => x.Id == serviceId, cancellationToken)) return AssignmentResult.ServiceNotFound;
        var assignment = await db.ResourceServices.FindAsync([resourceId, serviceId], cancellationToken);
        if (assignment is null) return AssignmentResult.AssignmentNotFound;
        db.ResourceServices.Remove(assignment);
        await db.SaveChangesAsync(cancellationToken);
        return AssignmentResult.Success;
    }

    private static ResourceResponse Map(Entities.Resource entity) => new(entity.Id, entity.Name, entity.Description,
        entity.IsActive, entity.CreatedAtUtc, entity.UpdatedAtUtc);
}
