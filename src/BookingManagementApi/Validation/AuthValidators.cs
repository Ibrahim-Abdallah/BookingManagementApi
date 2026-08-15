using BookingManagementApi.Common.Security;
using BookingManagementApi.Contracts.Auth;
using FluentValidation;

namespace BookingManagementApi.Validation;

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(UserConstraints.NameMaxLength)
            .Must(value => value == value.Trim()).WithMessage("First name must not contain surrounding whitespace.");
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(UserConstraints.NameMaxLength)
            .Must(value => value == value.Trim()).WithMessage("Last name must not contain surrounding whitespace.");
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(UserConstraints.EmailMaxLength);
        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(UserConstraints.PasswordMinLength)
            .MaximumLength(UserConstraints.PasswordMaxLength)
            .Matches("[A-Z]").WithMessage("Password must contain an uppercase letter.")
            .Matches("[a-z]").WithMessage("Password must contain a lowercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain a digit.")
            .Matches("[^a-zA-Z0-9]").WithMessage("Password must contain a non-alphanumeric character.");
        RuleFor(x => x.ConfirmPassword).Equal(x => x.Password).WithMessage("Passwords must match.");
    }
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(UserConstraints.EmailMaxLength);
        RuleFor(x => x.Password).NotEmpty();
    }
}

public sealed class RefreshTokenRequestValidator : AbstractValidator<RefreshTokenRequest>
{
    public RefreshTokenRequestValidator()
    {
        RuleFor(x => x.RefreshToken).NotEmpty().Must(value => !string.IsNullOrWhiteSpace(value))
            .WithMessage("Refresh token must not be whitespace.").MaximumLength(512);
    }
}
