using BookingManagementApi.Common.Security;
using BookingManagementApi.Common.Errors;
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
    [EndpointSummary("List my reservations")]
    public async Task<ActionResult<PagedResponse<ReservationResponse>>> List([FromQuery] ReservationQuery query, CancellationToken ct) => Ok(await reservations.ListAsync(query, ct));
    [HttpGet("{id:guid}")]
    [EndpointSummary("Get my reservation")]
    public async Task<ActionResult<ReservationResponse>> Get(Guid id, CancellationToken ct) => await reservations.GetAsync(id, ct) is { } x ? Ok(x) : NotFound();
    [HttpPost("{id:guid}/confirm")]
    [EndpointSummary("Confirm a reservation hold")]
    [EndpointDescription("Confirms an unexpired hold owned by the authenticated customer.")]
    public async Task<ActionResult<ReservationResponse>> Confirm(Guid id, CancellationToken ct) => ToAction(await reservations.ConfirmAsync(id, ct));
    [HttpPost("{id:guid}/cancel")]
    [EndpointSummary("Cancel a reservation")]
    [EndpointDescription("Cancels an owned hold or a confirmed reservation before the configured cutoff.")]
    public async Task<ActionResult<ReservationResponse>> Cancel(Guid id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CancelReservationRequest? request, CancellationToken ct) => ToAction(await reservations.CancelAsync(id, request, ct));
    [HttpPost("{id:guid}/reschedule")]
    [EndpointSummary("Reschedule a reservation")]
    [EndpointDescription("Moves a confirmed reservation on the same service/resource using transactional conflict checking.")]
    public async Task<ActionResult<ReservationResponse>> Reschedule(Guid id, RescheduleReservationRequest request, CancellationToken ct) => ToAction(await reservations.RescheduleAsync(id, request, ct));

    private ActionResult<ReservationResponse> ToAction(ReservationOperation operation) => operation.Result switch
    {
        ReservationResult.Success x => Ok(x.Response),
        ReservationResult.NotFound => NotFound(),
        ReservationResult.Invalid x => BadRequest(ApiProblems.Create(400, "Reservation validation failed.", x.Detail)),
        ReservationResult.Conflict x => Conflict(ApiProblems.Create(409, "Reservation conflict.", x.Detail)),
        _ => throw new InvalidOperationException("Unknown reservation result.")
    };
}
