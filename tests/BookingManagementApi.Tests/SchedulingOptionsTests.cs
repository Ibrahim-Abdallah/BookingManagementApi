using System.ComponentModel.DataAnnotations;
using BookingManagementApi.Configuration;

namespace BookingManagementApi.Tests;

public sealed class SchedulingOptionsTests
{
    [Fact]
    public void Validation_rejects_non_positive_core_intervals()
    {
        var options = new SchedulingOptions
        {
            BusinessTimeZoneId = "Egypt Standard Time",
            SlotIntervalMinutes = 0,
            HoldDurationMinutes = 0,
            MaximumBookingHorizonDays = 0,
            HoldCleanupIntervalSeconds = 0
        };

        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(options, new ValidationContext(options), results, true);

        Assert.False(isValid);
        Assert.Equal(4, results.Count);
    }
}
