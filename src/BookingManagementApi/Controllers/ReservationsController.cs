using BookingManagementApi.Common.Security;
using BookingManagementApi.Contracts.Reservations;
using BookingManagementApi.Services.Reservations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace BookingManagementApi.Controllers;

[ApiController, Route("api/reservations"), Authorize(Roles = AppRoles.Customer)]
public sealed class ReservationsController(ReservationService reservations) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResponse<ReservationResponse>>> List([FromQuery] ReservationQuery query, CancellationToken ct) => Ok(await reservations.ListAsync(query, ct));
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ReservationResponse>> Get(Guid id, CancellationToken ct) => await reservations.GetAsync(id, ct) is { } x ? Ok(x) : NotFound();
    [HttpPost("{id:guid}/confirm")]
    public async Task<ActionResult<ReservationResponse>> Confirm(Guid id, CancellationToken ct) => ToAction(await reservations.ConfirmAsync(id, ct));
    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<ReservationResponse>> Cancel(Guid id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CancelReservationRequest? request, CancellationToken ct) => ToAction(await reservations.CancelAsync(id, request, ct));
    [HttpPost("{id:guid}/reschedule")]
    public async Task<ActionResult<ReservationResponse>> Reschedule(Guid id, RescheduleReservationRequest request, CancellationToken ct) => ToAction(await reservations.RescheduleAsync(id, request, ct));

    private ActionResult<ReservationResponse> ToAction(ReservationOperation operation) => operation.Result switch
    {
        ReservationResult.Success x => Ok(x.Response),
        ReservationResult.NotFound => NotFound(),
        ReservationResult.Invalid x => BadRequest(new ProblemDetails { Title = "Reservation validation failed.", Detail = x.Detail, Status = 400 }),
        ReservationResult.Conflict x => Conflict(new ProblemDetails { Title = "Reservation conflict.", Detail = x.Detail, Status = 409 }),
        _ => throw new InvalidOperationException("Unknown reservation result.")
    };
}
