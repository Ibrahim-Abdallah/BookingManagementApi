using BookingManagementApi.Common.Security;
using BookingManagementApi.Contracts.Scheduling;
using BookingManagementApi.Services.Scheduling;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookingManagementApi.Controllers.Admin;

[ApiController, Authorize(Roles = AppRoles.Admin)]
public sealed class AvailabilityRulesController(ScheduleManagementService schedules) : ControllerBase
{
    [HttpGet("api/admin/resources/{resourceId:guid}/availability-rules")]
    public async Task<ActionResult<List<AvailabilityRuleResponse>>> List(Guid resourceId, CancellationToken ct)
    {
        var result = await schedules.ListAvailabilityRulesAsync(resourceId, ct);
        return result.Result == ScheduleResult.ResourceNotFound ? NotFound() : Ok(result.Value);
    }

    [HttpPost("api/admin/resources/{resourceId:guid}/availability-rules")]
    public async Task<ActionResult<AvailabilityRuleResponse>> Create(Guid resourceId, CreateAvailabilityRuleRequest request, CancellationToken ct)
    {
        var result = await schedules.CreateAvailabilityRuleAsync(resourceId, request, ct);
        return result.Result switch
        {
            ScheduleResult.ResourceNotFound => NotFound(),
            ScheduleResult.Conflict => Conflict(new ProblemDetails { Title = "Availability rule overlaps an existing rule." }),
            _ => Created($"/api/admin/availability-rules/{result.Value!.Id}", result.Value)
        };
    }

    [HttpPut("api/admin/availability-rules/{id:guid}")]
    public async Task<ActionResult<AvailabilityRuleResponse>> Update(Guid id, UpdateAvailabilityRuleRequest request, CancellationToken ct)
    {
        var result = await schedules.UpdateAvailabilityRuleAsync(id, request, ct);
        return result.Result switch
        {
            ScheduleResult.ItemNotFound => NotFound(),
            ScheduleResult.Conflict => Conflict(new ProblemDetails { Title = "Availability rule overlaps an existing rule." }),
            _ => Ok(result.Value)
        };
    }

    [HttpDelete("api/admin/availability-rules/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
        await schedules.DeleteAvailabilityRuleAsync(id, ct) == ScheduleResult.Success ? NoContent() : NotFound();
}
