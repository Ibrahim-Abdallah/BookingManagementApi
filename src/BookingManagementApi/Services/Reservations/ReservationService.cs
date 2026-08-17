using BookingManagementApi.Common.Security;
using BookingManagementApi.Configuration;
using BookingManagementApi.Contracts.Reservations;
using BookingManagementApi.Data;
using BookingManagementApi.Entities;
using BookingManagementApi.Enums;
using BookingManagementApi.Services.Auth;
using BookingManagementApi.Services.Availability;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BookingManagementApi.Services.Reservations;

public sealed class ReservationService(AppDbContext db, ICurrentUserService currentUser, IOptions<SchedulingOptions> options,
    TimeProvider timeProvider, ILogger<ReservationService> logger)
{
    private readonly SchedulingOptions _options = options.Value;

    public async Task<PagedResponse<ReservationResponse>?> ListAsync(ReservationQuery query, CancellationToken ct)
    {
        if (currentUser.UserId is not Guid userId) return null;
        var source = ApplyFilters(db.Reservations.AsNoTracking().Where(x => x.UserId == userId), query);
        var total = await source.CountAsync(ct);
        var rows = await source.OrderByDescending(x => x.StartsAtUtc).ThenByDescending(x => x.Id)
            .Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return new(rows.Select(Map).ToList(), query.PageNumber, query.PageSize, total,
            (int)Math.Ceiling(total / (double)query.PageSize));
    }

    public async Task<ReservationResponse?> GetAsync(Guid id, CancellationToken ct) => currentUser.UserId is Guid userId
        ? (await db.Reservations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct) is { } row ? Map(row) : null)
        : null;

    public async Task<ReservationOperation> ConfirmAsync(Guid id, CancellationToken ct)
    {
        if (currentUser.UserId is not Guid userId) return new(new ReservationResult.NotFound());
        var row = await db.Reservations.SingleOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);
        if (row is null) return new(new ReservationResult.NotFound());
        if (row.Status == ReservationStatus.Confirmed) return new(new ReservationResult.Success(Map(row)));
        var now = timeProvider.GetUtcNow();
        if (!ReservationStatusRules.CanTransition(row.Status, ReservationStatus.Confirmed) || row.HoldExpiresAtUtc <= now)
            return Conflict("The reservation cannot be confirmed.");
        row.Status = ReservationStatus.Confirmed; row.ConfirmedAtUtc = now; row.UpdatedAtUtc = now; row.HoldExpiresAtUtc = null;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            var latest = await db.Reservations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);
            return latest?.Status == ReservationStatus.Confirmed ? new(new ReservationResult.Success(Map(latest))) : Conflict("The reservation changed concurrently.");
        }
        logger.LogInformation("Reservation {ReservationId} confirmed", row.Id);
        return new(new ReservationResult.Success(Map(row)));
    }

    public async Task<ReservationOperation> CancelAsync(Guid id, CancelReservationRequest? request, CancellationToken ct)
    {
        if (currentUser.UserId is not Guid userId) return new(new ReservationResult.NotFound());
        var row = await db.Reservations.SingleOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);
        if (row is null) return new(new ReservationResult.NotFound());
        var now = timeProvider.GetUtcNow();
        if (!ReservationStatusRules.CanTransition(row.Status, ReservationStatus.Cancelled)) return Conflict("The reservation cannot be cancelled.");
        if (row.Status == ReservationStatus.Held && row.HoldExpiresAtUtc <= now) return Conflict("The reservation hold has expired.");
        if (row.Status == ReservationStatus.Confirmed && row.StartsAtUtc < now.AddMinutes(_options.MinimumCancellationNoticeMinutes))
            return Conflict("The cancellation cutoff has passed.");
        row.Status = ReservationStatus.Cancelled; row.CancelledAtUtc = now; row.UpdatedAtUtc = now;
        row.CancellationReason = string.IsNullOrWhiteSpace(request?.Reason) ? null : request.Reason.Trim(); row.HoldExpiresAtUtc = null;
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { return Conflict("The reservation changed concurrently."); }
        logger.LogInformation("Reservation {ReservationId} cancelled", row.Id);
        return new(new ReservationResult.Success(Map(row)));
    }

    public async Task<ReservationOperation> RescheduleAsync(Guid id, RescheduleReservationRequest request, CancellationToken ct)
    {
        if (currentUser.UserId is not Guid userId) return new(new ReservationResult.NotFound());
        if (request.StartsAtUtc.Offset != TimeSpan.Zero) return Invalid("StartsAtUtc must be expressed in UTC.");
        var row = await db.Reservations.SingleOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);
        if (row is null) return new(new ReservationResult.NotFound());
        if (row.Status != ReservationStatus.Confirmed) return Conflict("Only confirmed reservations may be rescheduled.");
        if (row.StartsAtUtc == request.StartsAtUtc) return Invalid("The new start must differ from the current start.");
        var end = request.StartsAtUtc.AddMinutes(row.ServiceDurationMinutesSnapshot);
        var validation = await ValidateScheduleAsync(row, request.StartsAtUtc, end, ct);
        if (validation is not null) return Invalid(validation);

        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
        if (transaction is not null)
            await db.Database.SqlQuery<Guid>($"SELECT Id AS Value FROM Resources WITH (UPDLOCK, HOLDLOCK) WHERE Id = {row.ResourceId}").SingleAsync(ct);
        await db.Entry(row).ReloadAsync(ct);
        if (row.UserId != userId || row.Status != ReservationStatus.Confirmed)
        { if (transaction is not null) await transaction.RollbackAsync(ct); return Conflict("The reservation changed concurrently."); }
        var now = timeProvider.GetUtcNow();
        if (request.StartsAtUtc < now.AddMinutes(_options.MinimumAdvanceMinutes) || request.StartsAtUtc > now.AddDays(_options.MaximumBookingHorizonDays))
        { if (transaction is not null) await transaction.RollbackAsync(ct); return Invalid("The requested start is outside the booking window."); }
        var overlap = await db.Reservations.AsNoTracking().AnyAsync(x => x.Id != row.Id && x.ResourceId == row.ResourceId &&
            x.StartsAtUtc < end && x.EndsAtUtc > request.StartsAtUtc && (x.Status == ReservationStatus.Confirmed ||
            (x.Status == ReservationStatus.Held && x.HoldExpiresAtUtc > now)), ct);
        if (overlap)
        { if (transaction is not null) await transaction.RollbackAsync(ct); logger.LogInformation("Reschedule conflict for reservation {ReservationId}", id); return Conflict("The requested slot is unavailable."); }
        row.StartsAtUtc = request.StartsAtUtc; row.EndsAtUtc = end; row.UpdatedAtUtc = now;
        try { await db.SaveChangesAsync(ct); if (transaction is not null) await transaction.CommitAsync(ct); }
        catch (DbUpdateConcurrencyException) { if (transaction is not null) await transaction.RollbackAsync(ct); return Conflict("The reservation changed concurrently."); }
        logger.LogInformation("Reservation {ReservationId} rescheduled", id);
        return new(new ReservationResult.Success(Map(row)));
    }

    public async Task<PagedResponse<AdminReservationResponse>> AdminListAsync(AdminReservationQuery query, CancellationToken ct)
    {
        var source = ApplyFilters(db.Reservations.AsNoTracking().Include(x => x.User), query);
        if (!string.IsNullOrWhiteSpace(query.CustomerEmail)) { var email = EmailNormalizer.Normalize(query.CustomerEmail); source = source.Where(x => x.User.NormalizedEmail == email); }
        if (query.ResourceId.HasValue) source = source.Where(x => x.ResourceId == query.ResourceId);
        if (query.ServiceId.HasValue) source = source.Where(x => x.ServiceId == query.ServiceId);
        var total = await source.CountAsync(ct);
        var rows = await source.OrderByDescending(x => x.StartsAtUtc).ThenByDescending(x => x.Id).Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return new(rows.Select(MapAdmin).ToList(), query.PageNumber, query.PageSize, total, (int)Math.Ceiling(total / (double)query.PageSize));
    }
    public async Task<AdminReservationResponse?> AdminGetAsync(Guid id, CancellationToken ct) =>
        await db.Reservations.AsNoTracking().Include(x => x.User).SingleOrDefaultAsync(x => x.Id == id, ct) is { } row ? MapAdmin(row) : null;
    public async Task<AdminOperation> AdminStatusAsync(Guid id, UpdateReservationStatusRequest request, CancellationToken ct)
    {
        var target = request.Status == "Completed" ? ReservationStatus.Completed : ReservationStatus.NoShow;
        var row = await db.Reservations.Include(x => x.User).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return new(new AdminResult.NotFound());
        if (!ReservationStatusRules.CanTransition(row.Status, target)) return new(new AdminResult.Conflict("The status transition is not allowed."));
        row.Status = target; row.UpdatedAtUtc = timeProvider.GetUtcNow();
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { return new(new AdminResult.Conflict("The reservation changed concurrently.")); }
        logger.LogInformation("Admin changed reservation {ReservationId} status to {Status}", id, target);
        return new(new AdminResult.Success(MapAdmin(row)));
    }

    private async Task<string?> ValidateScheduleAsync(Reservation row, DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        var active = await db.Resources.AsNoTracking().AnyAsync(x => x.Id == row.ResourceId && x.IsActive, ct) && await db.Services.AsNoTracking().AnyAsync(x => x.Id == row.ServiceId && x.IsActive, ct);
        if (!active || !await db.ResourceServices.AsNoTracking().AnyAsync(x => x.ResourceId == row.ResourceId && x.ServiceId == row.ServiceId, ct)) return "The resource/service is inactive or unsupported.";
        var zone = TimeZoneInfo.FindSystemTimeZoneById(_options.BusinessTimeZoneId); var localStart = TimeZoneInfo.ConvertTime(start, zone).DateTime; var localEnd = TimeZoneInfo.ConvertTime(end, zone).DateTime;
        if (!AvailabilityService.TryConvertToUtc(localStart, zone, out var rs) || !AvailabilityService.TryConvertToUtc(localEnd, zone, out var re) || rs != start || re != end || localStart.TimeOfDay.TotalMinutes % _options.SlotIntervalMinutes != 0) return "The requested start is not a valid aligned slot in the business time zone.";
        if (!await db.AvailabilityRules.AsNoTracking().AnyAsync(x => x.ResourceId == row.ResourceId && x.IsActive && x.DayOfWeek == localStart.DayOfWeek && localStart.Date == localEnd.Date && x.StartTime <= TimeOnly.FromDateTime(localStart) && x.EndTime >= TimeOnly.FromDateTime(localEnd), ct)) return "The requested interval is outside weekly availability.";
        if (await db.BlockedPeriods.AsNoTracking().AnyAsync(x => x.ResourceId == row.ResourceId && x.StartsAtUtc < end && x.EndsAtUtc > start, ct)) return "The requested interval overlaps a blocked period.";
        return null;
    }
    private static IQueryable<Reservation> ApplyFilters(IQueryable<Reservation> q, ReservationQuery f)
    { if (f.Status.HasValue) q = q.Where(x => x.Status == f.Status); if (f.FromDate.HasValue) q = q.Where(x => x.StartsAtUtc >= f.FromDate); if (f.ToDate.HasValue) q = q.Where(x => x.StartsAtUtc <= f.ToDate); return q; }
    private static ReservationResponse Map(Reservation x) => new(x.Id, x.ReferenceNumber, x.ServiceId, x.ServiceNameSnapshot, x.ResourceId, x.ResourceNameSnapshot, x.Status.ToString(), x.StartsAtUtc, x.EndsAtUtc, x.HoldExpiresAtUtc, x.CreatedAtUtc, x.UpdatedAtUtc, x.ConfirmedAtUtc, x.CancelledAtUtc, x.CancellationReason);
    private static AdminReservationResponse MapAdmin(Reservation x) => new(x.Id, x.ReferenceNumber, x.ServiceId, x.ServiceNameSnapshot, x.ResourceId, x.ResourceNameSnapshot, x.Status.ToString(), x.StartsAtUtc, x.EndsAtUtc, x.HoldExpiresAtUtc, x.CreatedAtUtc, x.UpdatedAtUtc, x.ConfirmedAtUtc, x.CancelledAtUtc, x.CancellationReason, x.UserId, x.User.Email, x.User.FirstName, x.User.LastName);
    private static ReservationOperation Conflict(string detail) => new(new ReservationResult.Conflict(detail));
    private static ReservationOperation Invalid(string detail) => new(new ReservationResult.Invalid(detail));
}

public abstract record ReservationResult { public sealed record Success(ReservationResponse Response) : ReservationResult; public sealed record NotFound : ReservationResult; public sealed record Invalid(string Detail) : ReservationResult; public sealed record Conflict(string Detail) : ReservationResult; }
public sealed record ReservationOperation(ReservationResult Result);
public abstract record AdminResult { public sealed record Success(AdminReservationResponse Response) : AdminResult; public sealed record NotFound : AdminResult; public sealed record Conflict(string Detail) : AdminResult; }
public sealed record AdminOperation(AdminResult Result);
