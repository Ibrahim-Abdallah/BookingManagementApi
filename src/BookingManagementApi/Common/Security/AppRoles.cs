namespace BookingManagementApi.Common.Security;

public static class AppRoles
{
    public const string Customer = "Customer";
    public const string Admin = "Admin";
}

public static class UserConstraints
{
    public const int NameMaxLength = 100;
    public const int EmailMaxLength = 320;
    public const int PasswordHashMaxLength = 512;
    public const int RoleMaxLength = 50;
    public const int PasswordMinLength = 8;
    public const int PasswordMaxLength = 128;
}
