using BookingManagementApi.Common.Security;
using BookingManagementApi.Contracts.Reservations;
using BookingManagementApi.Services.Reservations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookingManagementApi.Controllers;

[ApiController, Route("api/reservation-holds"), Authorize(Roles = AppRoles.Customer)]
public sealed class ReservationHoldsController(ReservationHoldService holds) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<ReservationHoldResponse>> Create(
        CreateReservationHoldRequest request, CancellationToken ct)
    {
        var operation = await holds.CreateAsync(request, ct);
        return operation.Result switch
        {
            ReservationHoldResult.Success success => Created($"/api/reservation-holds/{success.Response.ReservationId}", success.Response),
            ReservationHoldResult.Invalid invalid => BadRequest(new ProblemDetails { Title = "Reservation hold validation failed.", Detail = invalid.Detail, Status = 400 }),
            ReservationHoldResult.ServiceNotFound => NotFound(new ProblemDetails { Title = "Service not found.", Status = 404 }),
            ReservationHoldResult.ResourceNotFound => NotFound(new ProblemDetails { Title = "Resource not found.", Status = 404 }),
            ReservationHoldResult.Conflict => Conflict(new ProblemDetails { Title = "The requested slot is no longer available.", Status = 409 }),
            ReservationHoldResult.Unauthorized => Unauthorized(),
            _ => throw new InvalidOperationException("Unknown reservation hold result.")
        };
    }
}
