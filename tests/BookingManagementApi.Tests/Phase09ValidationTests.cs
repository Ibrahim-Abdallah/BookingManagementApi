using BookingManagementApi.Contracts.Reports;
using BookingManagementApi.Validation;
using FluentValidation.TestHelper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BookingManagementApi.Tests;

public sealed class Phase09ValidationTests
{
    [Fact]
    public void Report_date_range_rejects_from_after_to()
    {
        var validator = new ReservationSummaryQueryValidator();
        var result = validator.TestValidate(new ReservationSummaryQuery(
            DateTimeOffset.Parse("2026-08-20T10:00:00Z"),
            DateTimeOffset.Parse("2026-08-20T09:00:00Z")));

        result.ShouldHaveValidationErrorFor(x => x.ToDate);
    }

    [Fact]
    public void Application_registers_exactly_one_expiration_hosted_service()
    {
        using var factory = new AuthenticationApiFactory();
        _ = factory.CreateClient();
        var services = factory.Services.GetServices<IHostedService>()
            .Count(x => x.GetType().Name == "ExpiredReservationHoldService");

        Assert.Equal(1, services);
    }
}
