using BookingManagementApi.Contracts.Reports;
using FluentValidation;

namespace BookingManagementApi.Validation;

public sealed class ReservationSummaryQueryValidator : AbstractValidator<ReservationSummaryQuery>
{
    public ReservationSummaryQueryValidator()
    {
        RuleFor(x => x.ToDate)
            .GreaterThanOrEqualTo(x => x.FromDate)
            .When(x => x.FromDate.HasValue && x.ToDate.HasValue)
            .WithMessage("toDate must be greater than or equal to fromDate.");
    }
}
