using BookingManagementApi.Contracts.Reservations;
using FluentValidation;

namespace BookingManagementApi.Validation;

public sealed class CreateReservationHoldRequestValidator : AbstractValidator<CreateReservationHoldRequest>
{
    public CreateReservationHoldRequestValidator()
    {
        RuleFor(x => x.ServiceId).NotEmpty();
        RuleFor(x => x.ResourceId).NotEmpty();
        RuleFor(x => x.StartsAtUtc).NotEqual(default(DateTimeOffset));
    }
}
