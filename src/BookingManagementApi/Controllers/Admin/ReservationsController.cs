using BookingManagementApi.Common.Security;
using BookingManagementApi.Contracts.Reservations;
using BookingManagementApi.Services.Reservations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookingManagementApi.Controllers.Admin;

[ApiController, Route("api/admin/reservations"), Authorize(Roles = AppRoles.Admin)]
public sealed class ReservationsController(ReservationService reservations) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResponse<AdminReservationResponse>>> List([FromQuery] AdminReservationQuery query, CancellationToken ct) => Ok(await reservations.AdminListAsync(query, ct));
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AdminReservationResponse>> Get(Guid id, CancellationToken ct) => await reservations.AdminGetAsync(id, ct) is { } x ? Ok(x) : NotFound();
    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<AdminReservationResponse>> Status(Guid id, UpdateReservationStatusRequest request, CancellationToken ct)
    {
        var operation = await reservations.AdminStatusAsync(id, request, ct);
        return operation.Result switch
        {
            AdminResult.Success x => Ok(x.Response),
            AdminResult.NotFound => NotFound(),
            AdminResult.Conflict x => Conflict(new ProblemDetails { Title = "Reservation conflict.", Detail = x.Detail, Status = 409 }),
            _ => throw new InvalidOperationException("Unknown reservation result.")
        };
    }
}
