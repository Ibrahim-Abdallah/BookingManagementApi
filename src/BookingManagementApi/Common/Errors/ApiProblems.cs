using Microsoft.AspNetCore.Mvc;

namespace BookingManagementApi.Common.Errors;

public static class ApiProblems
{
    public static ProblemDetails Create(int status, string title, string detail) => new()
    {
        Status = status,
        Title = title,
        Detail = detail
    };
}
