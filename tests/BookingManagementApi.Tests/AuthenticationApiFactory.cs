using BookingManagementApi.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace BookingManagementApi.Tests;

public sealed class AuthenticationApiFactory : WebApplicationFactory<Program>
{
    public const string JwtKey = "phase-02-test-only-signing-key-at-least-32-bytes-long";
    private readonly string _databaseName = $"auth-tests-{Guid.NewGuid()}";
    public TimeProvider Clock { get; init; } = TimeProvider.System;
    public string BusinessTimeZoneId { get; init; } = "UTC";
    public int SlotIntervalMinutes { get; init; } = 15;
    public int MinimumAdvanceMinutes { get; init; } = 30;
    public int MaximumBookingHorizonDays { get; init; } = 90;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "BookingManagementApi.Tests",
                ["Jwt:Audience"] = "BookingManagementApi.Tests.Client",
                ["Jwt:Key"] = JwtKey,
                ["Jwt:AccessTokenExpirationMinutes"] = "15",
                ["Jwt:RefreshTokenExpirationDays"] = "7",
                ["Scheduling:BusinessTimeZoneId"] = BusinessTimeZoneId,
                ["Scheduling:SlotIntervalMinutes"] = SlotIntervalMinutes.ToString(),
                ["Scheduling:HoldDurationMinutes"] = "5",
                ["Scheduling:MinimumAdvanceMinutes"] = MinimumAdvanceMinutes.ToString(),
                ["Scheduling:MaximumBookingHorizonDays"] = MaximumBookingHorizonDays.ToString(),
                ["Scheduling:MinimumCancellationNoticeMinutes"] = "60",
                ["Scheduling:HoldCleanupIntervalSeconds"] = "3600"
            }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase(_databaseName));
            services.RemoveAll<TimeProvider>();
            services.AddSingleton(Clock);
        });
    }
}
