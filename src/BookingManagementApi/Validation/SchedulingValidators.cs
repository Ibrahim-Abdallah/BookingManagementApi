using BookingManagementApi.Contracts.Scheduling;
using FluentValidation;

namespace BookingManagementApi.Validation;

internal static class SchedulingValidationRules
{
    public static void AddAvailabilityRules<T>(this AbstractValidator<T> validator,
        Func<T, DayOfWeek> day, Func<T, TimeOnly> start, Func<T, TimeOnly> end)
    {
        validator.RuleFor(x => day(x)).IsInEnum();
        validator.RuleFor(x => end(x)).GreaterThan(x => start(x))
            .WithMessage("EndTime must be later than StartTime.");
    }

    public static void AddBlockedPeriodRules<T>(this AbstractValidator<T> validator,
        Func<T, DateTimeOffset> start, Func<T, DateTimeOffset> end, Func<T, string?> reason)
    {
        validator.RuleFor(x => end(x)).GreaterThan(x => start(x))
            .WithMessage("EndsAtUtc must be later than StartsAtUtc.");
        validator.RuleFor(x => reason(x)).MaximumLength(500);
    }
}

public sealed class CreateAvailabilityRuleRequestValidator : AbstractValidator<CreateAvailabilityRuleRequest>
{
    public CreateAvailabilityRuleRequestValidator() => this.AddAvailabilityRules(x => x.DayOfWeek, x => x.StartTime, x => x.EndTime);
}

public sealed class UpdateAvailabilityRuleRequestValidator : AbstractValidator<UpdateAvailabilityRuleRequest>
{
    public UpdateAvailabilityRuleRequestValidator() => this.AddAvailabilityRules(x => x.DayOfWeek, x => x.StartTime, x => x.EndTime);
}

public sealed class CreateBlockedPeriodRequestValidator : AbstractValidator<CreateBlockedPeriodRequest>
{
    public CreateBlockedPeriodRequestValidator() => this.AddBlockedPeriodRules(x => x.StartsAtUtc, x => x.EndsAtUtc, x => x.Reason);
}

public sealed class UpdateBlockedPeriodRequestValidator : AbstractValidator<UpdateBlockedPeriodRequest>
{
    public UpdateBlockedPeriodRequestValidator() => this.AddBlockedPeriodRules(x => x.StartsAtUtc, x => x.EndsAtUtc, x => x.Reason);
}
