using BookingManagementApi.Contracts.Availability;
using FluentValidation;

namespace BookingManagementApi.Validation;

public sealed class AvailabilityQueryParametersValidator : AbstractValidator<AvailabilityQueryParameters>
{
    public AvailabilityQueryParametersValidator()
    {
        RuleFor(x => x.ServiceId).NotEmpty();
        RuleFor(x => x.Date).NotEqual(default(DateOnly));
        RuleFor(x => x.ResourceId).Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("ResourceId must not be empty when supplied.");
    }
}
