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

    [Fact]
    public void Configured_business_timezone_is_resolvable() =>
        Assert.NotNull(TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time"));

    [Fact]
    public void Validation_rejects_unresolvable_business_timezone()
    {
        var options = new SchedulingOptions
        {
            BusinessTimeZoneId = "Definitely/Not-A-TimeZone",
            SlotIntervalMinutes = 15,
            HoldDurationMinutes = 5,
            MaximumBookingHorizonDays = 90,
            HoldCleanupIntervalSeconds = 60
        };
        var results = new List<ValidationResult>();

        Assert.False(Validator.TryValidateObject(options, new ValidationContext(options), results, true));
        Assert.Contains(results, result => result.MemberNames.Contains(nameof(SchedulingOptions.BusinessTimeZoneId)));
    }
}
