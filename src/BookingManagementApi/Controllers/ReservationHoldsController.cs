using BookingManagementApi.Common.Security;
using BookingManagementApi.Common.Errors;
using BookingManagementApi.Contracts.Reservations;
using BookingManagementApi.Services.Reservations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookingManagementApi.Controllers;

[ApiController, Route("api/reservation-holds"), Authorize(Roles = AppRoles.Customer)]
public sealed class ReservationHoldsController(ReservationHoldService holds) : ControllerBase
{
    [HttpPost]
    [EndpointSummary("Create a reservation hold")]
    [EndpointDescription("Creates a temporary concurrency-safe hold. The server derives the end time and expiry.")]
    public async Task<ActionResult<ReservationHoldResponse>> Create(
        CreateReservationHoldRequest request, CancellationToken ct)
    {
        var operation = await holds.CreateAsync(request, ct);
        return operation.Result switch
        {
            ReservationHoldResult.Success success => Created($"/api/reservation-holds/{success.Response.ReservationId}", success.Response),
            ReservationHoldResult.Invalid invalid => BadRequest(ApiProblems.Create(400, "Reservation hold validation failed.", invalid.Detail)),
            ReservationHoldResult.ServiceNotFound => NotFound(ApiProblems.Create(404, "Service not found.", "The requested service was not found.")),
            ReservationHoldResult.ResourceNotFound => NotFound(ApiProblems.Create(404, "Resource not found.", "The requested resource was not found.")),
            ReservationHoldResult.Conflict => Conflict(ApiProblems.Create(409, "Reservation conflict.", "The requested slot is no longer available.")),
            ReservationHoldResult.Unauthorized => Unauthorized(),
            _ => throw new InvalidOperationException("Unknown reservation hold result.")
        };
    }
}
