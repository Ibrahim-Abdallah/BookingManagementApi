namespace BookingManagementApi.Common.Security;

public static class EmailNormalizer
{
    public static string Normalize(string email) => email.Trim().ToUpperInvariant();
    public static string ToDisplayEmail(string email) => email.Trim();
}
