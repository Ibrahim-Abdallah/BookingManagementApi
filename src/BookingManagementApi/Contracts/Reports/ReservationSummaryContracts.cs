namespace BookingManagementApi.Contracts.Reports;

public sealed record ReservationSummaryQuery(DateTimeOffset? FromDate, DateTimeOffset? ToDate);

public sealed record ReservationSummaryResponse(
    int TotalReservations,
    int Confirmed,
    int Completed,
    int Cancelled,
    int NoShow,
    IReadOnlyList<TopServiceResponse> TopServices,
    IReadOnlyList<TopResourceResponse> TopResources);

public sealed record TopServiceResponse(Guid ServiceId, string ServiceName, int ReservationCount);

public sealed record TopResourceResponse(Guid ResourceId, string ResourceName, int ReservationCount);
