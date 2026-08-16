using BookingManagementApi.Configuration;
using BookingManagementApi.Contracts.Catalog;
using FluentValidation;
using Microsoft.Extensions.Options;

namespace BookingManagementApi.Validation;

internal static class CatalogValidationRules
{
    public static void AddNameRules<T>(this IRuleBuilderInitial<T, string> rule) => rule
        .NotEmpty().Must(value => !string.IsNullOrWhiteSpace(value)).WithMessage("Name must not be whitespace.")
        .MaximumLength(CatalogConstraints.NameMaxLength);

    public static void AddDescriptionRules<T>(this IRuleBuilderInitial<T, string?> rule) => rule
        .MaximumLength(CatalogConstraints.DescriptionMaxLength);
}

public sealed class CreateServiceRequestValidator : AbstractValidator<CreateServiceRequest>
{
    public CreateServiceRequestValidator(IOptions<SchedulingOptions> options)
    {
        RuleFor(x => x.Name).AddNameRules();
        RuleFor(x => x.Description).AddDescriptionRules();
        RuleFor(x => x.DurationMinutes).GreaterThan(0)
            .Must(value => value > 0 && value % options.Value.SlotIntervalMinutes == 0)
            .WithMessage($"Duration must be a multiple of {options.Value.SlotIntervalMinutes} minutes.");
    }
}

public sealed class UpdateServiceRequestValidator : AbstractValidator<UpdateServiceRequest>
{
    public UpdateServiceRequestValidator(IOptions<SchedulingOptions> options)
    {
        RuleFor(x => x.Name).AddNameRules();
        RuleFor(x => x.Description).AddDescriptionRules();
        RuleFor(x => x.DurationMinutes).GreaterThan(0)
            .Must(value => value > 0 && value % options.Value.SlotIntervalMinutes == 0)
            .WithMessage($"Duration must be a multiple of {options.Value.SlotIntervalMinutes} minutes.");
    }
}

public sealed class CreateResourceRequestValidator : AbstractValidator<CreateResourceRequest>
{
    public CreateResourceRequestValidator()
    {
        RuleFor(x => x.Name).AddNameRules();
        RuleFor(x => x.Description).AddDescriptionRules();
    }
}

public sealed class UpdateResourceRequestValidator : AbstractValidator<UpdateResourceRequest>
{
    public UpdateResourceRequestValidator()
    {
        RuleFor(x => x.Name).AddNameRules();
        RuleFor(x => x.Description).AddDescriptionRules();
    }
}
