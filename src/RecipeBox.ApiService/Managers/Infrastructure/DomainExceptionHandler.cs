using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using RecipeBox.ApiService.Managers.Models.Domain;

namespace RecipeBox.ApiService.Managers.Infrastructure;

/// <summary>
/// Translates domain and validation exceptions into the shared ProblemDetails error shape, so no
/// layer has to hand-build error responses:
/// <list type="bullet">
///   <item><see cref="ValidationException"/> → 400 with per-field errors.</item>
///   <item><see cref="RecipeNameConflictException"/> → 409.</item>
/// </list>
/// Anything else is left for the default handling (500).
/// </summary>
public class DomainExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetails = problemDetails;

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ProblemDetails problem;

        switch (exception)
        {
            case ValidationException validation:
                var errors = validation.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
                problem = new ValidationProblemDetails(errors)
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "One or more validation errors occurred.",
                };
                break;

            case RecipeNameConflictException conflict:
                problem = new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = "Recipe name already exists.",
                    Detail = conflict.Message,
                };
                break;

            default:
                return false; // Not ours — let the default pipeline produce a 500.
        }

        httpContext.Response.StatusCode = problem.Status!.Value;
        return await _problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problem,
        });
    }
}
