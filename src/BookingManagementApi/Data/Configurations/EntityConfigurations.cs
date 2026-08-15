using BookingManagementApi.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookingManagementApi.Data.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.FirstName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.LastName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Email).HasMaxLength(320).IsRequired();
        builder.Property(x => x.NormalizedEmail).HasMaxLength(320).IsRequired();
        builder.Property(x => x.PasswordHash).HasMaxLength(512).IsRequired();
        builder.Property(x => x.Role).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.NormalizedEmail).IsUnique();
    }
}

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
        builder.HasIndex(x => x.TokenHash).IsUnique();
        builder.HasOne(x => x.User).WithMany(x => x.RefreshTokens).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.ReplacedByToken).WithMany().HasForeignKey(x => x.ReplacedByTokenId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class ServiceConfiguration : IEntityTypeConfiguration<Service>
{
    public void Configure(EntityTypeBuilder<Service> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.ToTable(t => t.HasCheckConstraint("CK_Services_DurationMinutes_Positive", "[DurationMinutes] > 0"));
    }
}

public sealed class ResourceConfiguration : IEntityTypeConfiguration<Resource>
{
    public void Configure(EntityTypeBuilder<Resource> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(1000);
    }
}

public sealed class ResourceServiceConfiguration : IEntityTypeConfiguration<ResourceService>
{
    public void Configure(EntityTypeBuilder<ResourceService> builder)
    {
        builder.HasKey(x => new { x.ResourceId, x.ServiceId });
        builder.HasOne(x => x.Resource).WithMany(x => x.ResourceServices).HasForeignKey(x => x.ResourceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Service).WithMany(x => x.ResourceServices).HasForeignKey(x => x.ServiceId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class AvailabilityRuleConfiguration : IEntityTypeConfiguration<AvailabilityRule>
{
    public void Configure(EntityTypeBuilder<AvailabilityRule> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.StartTime).HasColumnType("time");
        builder.Property(x => x.EndTime).HasColumnType("time");
        builder.HasIndex(x => new { x.ResourceId, x.DayOfWeek });
        builder.HasOne(x => x.Resource).WithMany(x => x.AvailabilityRules).HasForeignKey(x => x.ResourceId).OnDelete(DeleteBehavior.Cascade);
        builder.ToTable(t => t.HasCheckConstraint("CK_AvailabilityRules_TimeRange", "[StartTime] < [EndTime]"));
    }
}

public sealed class BlockedPeriodConfiguration : IEntityTypeConfiguration<BlockedPeriod>
{
    public void Configure(EntityTypeBuilder<BlockedPeriod> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Reason).HasMaxLength(500);
        builder.HasIndex(x => new { x.ResourceId, x.StartsAtUtc, x.EndsAtUtc });
        builder.HasOne(x => x.Resource).WithMany(x => x.BlockedPeriods).HasForeignKey(x => x.ResourceId).OnDelete(DeleteBehavior.Cascade);
        builder.ToTable(t => t.HasCheckConstraint("CK_BlockedPeriods_TimeRange", "[StartsAtUtc] < [EndsAtUtc]"));
    }
}

public sealed class ReservationConfiguration : IEntityTypeConfiguration<Reservation>
{
    public void Configure(EntityTypeBuilder<Reservation> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ReferenceNumber).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.CancellationReason).HasMaxLength(500);
        builder.Property(x => x.ServiceNameSnapshot).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ResourceNameSnapshot).HasMaxLength(200).IsRequired();
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => x.ReferenceNumber).IsUnique();
        builder.HasIndex(x => new { x.UserId, x.StartsAtUtc });
        builder.HasIndex(x => new { x.ResourceId, x.StartsAtUtc, x.EndsAtUtc });
        builder.HasIndex(x => new { x.Status, x.HoldExpiresAtUtc });
        builder.HasOne(x => x.User).WithMany(x => x.Reservations).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.Resource).WithMany(x => x.Reservations).HasForeignKey(x => x.ResourceId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.Service).WithMany(x => x.Reservations).HasForeignKey(x => x.ServiceId).OnDelete(DeleteBehavior.NoAction);
        builder.ToTable(t => t.HasCheckConstraint("CK_Reservations_TimeRange", "[StartsAtUtc] < [EndsAtUtc]"));
    }
}
