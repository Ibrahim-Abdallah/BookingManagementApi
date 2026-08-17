using BookingManagementApi.Contracts.Reservations;
using FluentValidation;

namespace BookingManagementApi.Validation;

public sealed class ReservationQueryValidator : AbstractValidator<ReservationQuery>
{
    public ReservationQueryValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x).Must(x => !x.FromDate.HasValue || !x.ToDate.HasValue || x.FromDate <= x.ToDate)
            .WithMessage("FromDate must be earlier than or equal to ToDate.");
    }
}
public sealed class AdminReservationQueryValidator : AbstractValidator<AdminReservationQuery>
{
    public AdminReservationQueryValidator()
    {
        Include(new ReservationQueryValidator());
        RuleFor(x => x.ResourceId).NotEmpty().When(x => x.ResourceId.HasValue);
        RuleFor(x => x.ServiceId).NotEmpty().When(x => x.ServiceId.HasValue);
    }
}
public sealed class CancelReservationRequestValidator : AbstractValidator<CancelReservationRequest>
{ public CancelReservationRequestValidator() => RuleFor(x => x.Reason).MaximumLength(500); }
public sealed class RescheduleReservationRequestValidator : AbstractValidator<RescheduleReservationRequest>
{ public RescheduleReservationRequestValidator() => RuleFor(x => x.StartsAtUtc).NotEqual(default(DateTimeOffset)); }
public sealed class UpdateReservationStatusRequestValidator : AbstractValidator<UpdateReservationStatusRequest>
{
    public UpdateReservationStatusRequestValidator() => RuleFor(x => x.Status).NotEmpty()
        .Must(x => x is "Completed" or "NoShow").WithMessage("Status must be Completed or NoShow.");
}
