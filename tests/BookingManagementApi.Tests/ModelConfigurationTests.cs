using BookingManagementApi.Data;
using BookingManagementApi.Entities;
using Microsoft.EntityFrameworkCore;

namespace BookingManagementApi.Tests;

public sealed class ModelConfigurationTests
{
    private static AppDbContext CreateContext() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public void Reservation_has_expected_indexes_and_row_version()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(Reservation))!;

        Assert.Contains(entity.GetIndexes(), index => index.IsUnique && index.Properties.Single().Name == nameof(Reservation.ReferenceNumber));
        Assert.True(entity.FindProperty(nameof(Reservation.RowVersion))!.IsConcurrencyToken);
    }

    [Fact]
    public void Resource_service_uses_composite_primary_key()
    {
        using var context = CreateContext();
        var key = context.Model.FindEntityType(typeof(ResourceService))!.FindPrimaryKey()!;

        Assert.Equal([nameof(ResourceService.ResourceId), nameof(ResourceService.ServiceId)], key.Properties.Select(x => x.Name));
    }
}
