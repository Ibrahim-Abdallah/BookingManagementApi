using BookingManagementApi.Contracts.Availability;
using BookingManagementApi.Services.Availability;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookingManagementApi.Controllers;

[ApiController, Route("api/availability"), Authorize]
public sealed class AvailabilityController(AvailabilityService availability) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<AvailabilityResponse>> Search(
        [FromQuery] AvailabilityQueryParameters query,
        CancellationToken ct)
    {
        var result = await availability.SearchAsync(query, ct);
        return result.Result switch
        {
            AvailabilityResult.Success => Ok(result.Response),
            AvailabilityResult.ServiceNotFound or AvailabilityResult.ResourceNotFound => NotFound(),
            AvailabilityResult.InvalidDate => BadRequest(new ProblemDetails
            {
                Title = "The requested date is outside the booking window.",
                Status = StatusCodes.Status400BadRequest
            }),
            _ => throw new InvalidOperationException("Unknown availability result.")
        };
    }
}
