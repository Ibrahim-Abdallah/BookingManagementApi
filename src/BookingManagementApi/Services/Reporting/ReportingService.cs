using System.Data;
using BookingManagementApi.Contracts.Reports;
using BookingManagementApi.Data;
using Dapper;
using Microsoft.EntityFrameworkCore;

namespace BookingManagementApi.Services.Reporting;

public sealed class ReportingService(AppDbContext db)
{
    private const string Sql = """
        SELECT
            COUNT(*) AS TotalReservations,
            COALESCE(SUM(CASE WHEN r.Status = 'Confirmed' THEN 1 ELSE 0 END), 0) AS Confirmed,
            COALESCE(SUM(CASE WHEN r.Status = 'Completed' THEN 1 ELSE 0 END), 0) AS Completed,
            COALESCE(SUM(CASE WHEN r.Status = 'Cancelled' THEN 1 ELSE 0 END), 0) AS Cancelled,
            COALESCE(SUM(CASE WHEN r.Status = 'NoShow' THEN 1 ELSE 0 END), 0) AS NoShow
        FROM Reservations r
        WHERE r.Status IN ('Confirmed', 'Completed', 'Cancelled', 'NoShow')
          AND (@FromDate IS NULL OR r.StartsAtUtc >= @FromDate)
          AND (@ToDate IS NULL OR r.StartsAtUtc <= @ToDate);

        SELECT TOP (5)
            s.Id AS ServiceId,
            s.Name AS ServiceName,
            COUNT(*) AS ReservationCount
        FROM Reservations r
        INNER JOIN Services s ON s.Id = r.ServiceId
        WHERE r.Status IN ('Confirmed', 'Completed', 'Cancelled', 'NoShow')
          AND (@FromDate IS NULL OR r.StartsAtUtc >= @FromDate)
          AND (@ToDate IS NULL OR r.StartsAtUtc <= @ToDate)
        GROUP BY s.Id, s.Name
        ORDER BY ReservationCount DESC, s.Name ASC, s.Id ASC;

        SELECT TOP (5)
            resource.Id AS ResourceId,
            resource.Name AS ResourceName,
            COUNT(*) AS ReservationCount
        FROM Reservations r
        INNER JOIN Resources resource ON resource.Id = r.ResourceId
        WHERE r.Status IN ('Confirmed', 'Completed', 'Cancelled', 'NoShow')
          AND (@FromDate IS NULL OR r.StartsAtUtc >= @FromDate)
          AND (@ToDate IS NULL OR r.StartsAtUtc <= @ToDate)
        GROUP BY resource.Id, resource.Name
        ORDER BY ReservationCount DESC, resource.Name ASC, resource.Id ASC;
        """;

    public async Task<ReservationSummaryResponse> GetReservationSummaryAsync(
        ReservationSummaryQuery query,
        CancellationToken cancellationToken = default)
    {
        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State == ConnectionState.Closed;
        if (openedHere)
            await connection.OpenAsync(cancellationToken);

        try
        {
            var command = new CommandDefinition(Sql, new { query.FromDate, query.ToDate }, cancellationToken: cancellationToken);
            using var results = await connection.QueryMultipleAsync(command);
            var summary = await results.ReadSingleAsync<SummaryRow>();
            var topServices = (await results.ReadAsync<TopServiceResponse>()).AsList();
            var topResources = (await results.ReadAsync<TopResourceResponse>()).AsList();
            return new(summary.TotalReservations, summary.Confirmed, summary.Completed, summary.Cancelled,
                summary.NoShow, topServices, topResources);
        }
        finally
        {
            if (openedHere)
                await connection.CloseAsync();
        }
    }

    private sealed record SummaryRow(int TotalReservations, int Confirmed, int Completed, int Cancelled, int NoShow);
}
