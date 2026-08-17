using BookingManagementApi.Common.Security;
using BookingManagementApi.Contracts.Reports;
using BookingManagementApi.Services.Reporting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookingManagementApi.Controllers.Admin;

[ApiController, Route("api/admin/reports"), Authorize(Roles = AppRoles.Admin)]
public sealed class ReportsController(ReportingService reporting) : ControllerBase
{
    [HttpGet("reservations-summary")]
    public async Task<ActionResult<ReservationSummaryResponse>> ReservationsSummary(
        [FromQuery] ReservationSummaryQuery query,
        CancellationToken cancellationToken) =>
        Ok(await reporting.GetReservationSummaryAsync(query, cancellationToken));
}
