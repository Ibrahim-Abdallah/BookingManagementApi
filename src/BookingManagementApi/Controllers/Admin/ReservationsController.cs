using BookingManagementApi.Common.Security;
using BookingManagementApi.Common.Errors;
using BookingManagementApi.Contracts.Reservations;
using BookingManagementApi.Services.Reservations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookingManagementApi.Controllers.Admin;

[ApiController, Route("api/admin/reservations"), Authorize(Roles = AppRoles.Admin)]
public sealed class ReservationsController(ReservationService reservations) : ControllerBase
{
    [HttpGet]
    [EndpointSummary("List reservations for administration")]
    public async Task<ActionResult<PagedResponse<AdminReservationResponse>>> List([FromQuery] AdminReservationQuery query, CancellationToken ct) => Ok(await reservations.AdminListAsync(query, ct));
    [HttpGet("{id:guid}")]
    [EndpointSummary("Get a reservation for administration")]
    public async Task<ActionResult<AdminReservationResponse>> Get(Guid id, CancellationToken ct) => await reservations.AdminGetAsync(id, ct) is { } x ? Ok(x) : NotFound();
    [HttpPatch("{id:guid}/status")]
    [EndpointSummary("Set an operational reservation status")]
    [EndpointDescription("Marks a confirmed reservation Completed or NoShow; arbitrary status edits are not supported.")]
    public async Task<ActionResult<AdminReservationResponse>> Status(Guid id, UpdateReservationStatusRequest request, CancellationToken ct)
    {
        var operation = await reservations.AdminStatusAsync(id, request, ct);
        return operation.Result switch
        {
            AdminResult.Success x => Ok(x.Response),
            AdminResult.NotFound => NotFound(),
            AdminResult.Conflict x => Conflict(ApiProblems.Create(409, "Reservation conflict.", x.Detail)),
            _ => throw new InvalidOperationException("Unknown reservation result.")
        };
    }
}
