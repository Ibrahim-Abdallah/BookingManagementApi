namespace BookingManagementApi.Contracts.Catalog;

public static class CatalogConstraints
{
    public const int NameMaxLength = 200;
    public const int DescriptionMaxLength = 1000;
}

public sealed record CreateServiceRequest(string Name, string? Description, int DurationMinutes);
public sealed record UpdateServiceRequest(string Name, string? Description, int DurationMinutes);
public sealed record SetActivationRequest(bool IsActive);
public sealed record ServiceResponse(Guid Id, string Name, string? Description, int DurationMinutes,
    bool IsActive, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);

public sealed record CreateResourceRequest(string Name, string? Description);
public sealed record UpdateResourceRequest(string Name, string? Description);
public sealed record ResourceResponse(Guid Id, string Name, string? Description, bool IsActive,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);
