using BookingManagementApi.Common.Security;
using BookingManagementApi.Common.Errors;
using BookingManagementApi.Contracts.Scheduling;
using BookingManagementApi.Services.Scheduling;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookingManagementApi.Controllers.Admin;

[ApiController, Authorize(Roles = AppRoles.Admin)]
public sealed class BlockedPeriodsController(ScheduleManagementService schedules) : ControllerBase
{
    [HttpGet("api/admin/resources/{resourceId:guid}/blocked-periods")]
    [EndpointSummary("List blocked periods")]
    public async Task<ActionResult<List<BlockedPeriodResponse>>> List(Guid resourceId, CancellationToken ct)
    {
        var result = await schedules.ListBlockedPeriodsAsync(resourceId, ct);
        return result.Result == ScheduleResult.ResourceNotFound ? NotFound() : Ok(result.Value);
    }

    [HttpPost("api/admin/resources/{resourceId:guid}/blocked-periods")]
    [EndpointSummary("Create a blocked period")]
    public async Task<ActionResult<BlockedPeriodResponse>> Create(Guid resourceId, CreateBlockedPeriodRequest request, CancellationToken ct)
    {
        var result = await schedules.CreateBlockedPeriodAsync(resourceId, request, ct);
        return result.Result switch
        {
            ScheduleResult.ResourceNotFound => NotFound(),
            ScheduleResult.Conflict => Conflict(ApiProblems.Create(409, "Scheduling conflict.", "The blocked period conflicts with an existing confirmed reservation.")),
            _ => Created($"/api/admin/blocked-periods/{result.Value!.Id}", result.Value)
        };
    }

    [HttpPut("api/admin/blocked-periods/{id:guid}")]
    [EndpointSummary("Update a blocked period")]
    public async Task<ActionResult<BlockedPeriodResponse>> Update(Guid id, UpdateBlockedPeriodRequest request, CancellationToken ct)
    {
        var result = await schedules.UpdateBlockedPeriodAsync(id, request, ct);
        return result.Result switch
        {
            ScheduleResult.ItemNotFound => NotFound(),
            ScheduleResult.Conflict => Conflict(ApiProblems.Create(409, "Scheduling conflict.", "The blocked period conflicts with an existing confirmed reservation.")),
            _ => Ok(result.Value)
        };
    }

    [HttpDelete("api/admin/blocked-periods/{id:guid}")]
    [EndpointSummary("Delete a blocked period")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
        await schedules.DeleteBlockedPeriodAsync(id, ct) == ScheduleResult.Success ? NoContent() : NotFound();
}
