using BookingManagementApi.Common.Security;
using BookingManagementApi.Common.Errors;
using BookingManagementApi.Contracts.Catalog;
using BookingManagementApi.Services.Catalog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookingManagementApi.Controllers.Admin;

[ApiController, Route("api/admin/resources"), Authorize(Roles = AppRoles.Admin)]
public sealed class ResourcesController(ResourceCatalogService catalog) : ControllerBase
{
    [HttpGet]
    [EndpointSummary("List resources for administration")]
    public async Task<ActionResult<List<ResourceResponse>>> List([FromQuery] bool? isActive, CancellationToken ct) =>
        Ok(await catalog.ListAsync(false, isActive, ct));

    [HttpGet("{id:guid}")]
    [EndpointSummary("Get a resource for administration")]
    public async Task<ActionResult<ResourceResponse>> Get(Guid id, CancellationToken ct)
    {
        var result = await catalog.GetAsync(id, false, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost]
    [EndpointSummary("Create a resource")]
    public async Task<ActionResult<ResourceResponse>> Create(CreateResourceRequest request, CancellationToken ct)
    {
        var result = await catalog.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    [EndpointSummary("Update a resource")]
    public async Task<ActionResult<ResourceResponse>> Update(Guid id, UpdateResourceRequest request, CancellationToken ct)
    {
        var result = await catalog.UpdateAsync(id, request, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPatch("{id:guid}/activation")]
    [EndpointSummary("Activate or deactivate a resource")]
    public async Task<IActionResult> SetActivation(Guid id, SetActivationRequest request, CancellationToken ct) =>
        await catalog.SetActivationAsync(id, request.IsActive, ct) ? NoContent() : NotFound();

    [HttpGet("{resourceId:guid}/services")]
    [EndpointSummary("List services assigned to a resource")]
    public async Task<ActionResult<List<ServiceResponse>>> GetServices(Guid resourceId, CancellationToken ct)
    {
        var result = await catalog.GetServicesAsync(resourceId, ct);
        return result.Result == AssignmentResult.ResourceNotFound ? NotFound() : Ok(result.Services);
    }

    [HttpPost("{resourceId:guid}/services/{serviceId:guid}")]
    [EndpointSummary("Assign a service to a resource")]
    public async Task<IActionResult> Assign(Guid resourceId, Guid serviceId, CancellationToken ct) =>
        ToAssignmentResult(await catalog.AssignAsync(resourceId, serviceId, ct));

    [HttpDelete("{resourceId:guid}/services/{serviceId:guid}")]
    [EndpointSummary("Remove a service assignment")]
    public async Task<IActionResult> RemoveAssignment(Guid resourceId, Guid serviceId, CancellationToken ct) =>
        ToAssignmentResult(await catalog.RemoveAssignmentAsync(resourceId, serviceId, ct));

    private IActionResult ToAssignmentResult(AssignmentResult result) => result switch
    {
        AssignmentResult.Success => NoContent(),
        AssignmentResult.Conflict => Conflict(ApiProblems.Create(409, "Resource conflict.", "The resource-service assignment already exists or conflicts with current state.")),
        _ => NotFound()
    };
}
