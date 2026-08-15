using System.ComponentModel.DataAnnotations;

namespace BookingManagementApi.Configuration;

public sealed class SchedulingOptions
{
    public const string SectionName = "Scheduling";

    [Required]
    public string BusinessTimeZoneId { get; init; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int SlotIntervalMinutes { get; init; }

    [Range(1, int.MaxValue)]
    public int HoldDurationMinutes { get; init; }

    [Range(0, int.MaxValue)]
    public int MinimumAdvanceMinutes { get; init; }

    [Range(1, int.MaxValue)]
    public int MaximumBookingHorizonDays { get; init; }

    [Range(0, int.MaxValue)]
    public int MinimumCancellationNoticeMinutes { get; init; }

    [Range(1, int.MaxValue)]
    public int HoldCleanupIntervalSeconds { get; init; }
}
