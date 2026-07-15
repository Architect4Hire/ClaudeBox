using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using RecipeBox.ApiService.Managers.Infrastructure;
using RecipeBox.ApiService.Managers.Models.Domain;
using Xunit;

namespace RecipeBox.Tests.Recipes;

/// <summary>
/// Unit tests for <see cref="DomainExceptionHandler"/> in isolation: the routing from exception type
/// to ProblemDetails shape/status, and — importantly — that an unmapped exception falls through
/// (returns <c>false</c>) so the default pipeline produces a 500. The endpoint tests only reach this
/// indirectly and never exercise the fall-through branch.
/// </summary>
public class DomainExceptionHandlerTests
{
    private readonly IProblemDetailsService _problemDetails = Substitute.For<IProblemDetailsService>();
    private readonly DomainExceptionHandler _sut;
    private ProblemDetailsContext? _written;

    public DomainExceptionHandlerTests()
    {
        _problemDetails
            .TryWriteAsync(Arg.Do<ProblemDetailsContext>(c => _written = c))
            .Returns(true);
        _sut = new DomainExceptionHandler(_problemDetails);
    }

    private async Task<(bool handled, HttpContext ctx)> HandleAsync(Exception exception)
    {
        var ctx = new DefaultHttpContext();
        var handled = await _sut.TryHandleAsync(ctx, exception, CancellationToken.None);
        return (handled, ctx);
    }

    [Fact]
    public async Task ValidationException_maps_to_400_with_per_field_errors()
    {
        var exception = new ValidationException(new[]
        {
            new ValidationFailure("Name", "Name is required"),
            new ValidationFailure("Servings", "Servings must be at least 1"),
        });

        var (handled, ctx) = await HandleAsync(exception);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status400BadRequest, ctx.Response.StatusCode);
        var problem = Assert.IsType<ValidationProblemDetails>(_written!.ProblemDetails);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
        Assert.Equal("One or more validation errors occurred.", problem.Title);
        Assert.True(problem.Errors.ContainsKey("Name"));
        Assert.True(problem.Errors.ContainsKey("Servings"));
    }

    [Fact]
    public async Task ValidationException_groups_multiple_messages_per_field()
    {
        var exception = new ValidationException(new[]
        {
            new ValidationFailure("Name", "Name is required"),
            new ValidationFailure("Name", "Name is too long"),
        });

        await HandleAsync(exception);

        var problem = Assert.IsType<ValidationProblemDetails>(_written!.ProblemDetails);
        Assert.Equal(2, problem.Errors["Name"].Length);
    }

    [Fact]
    public async Task RecipeNameConflictException_maps_to_409_with_message_in_detail()
    {
        var exception = new RecipeNameConflictException("Pancakes");

        var (handled, ctx) = await HandleAsync(exception);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status409Conflict, ctx.Response.StatusCode);
        Assert.Equal(StatusCodes.Status409Conflict, _written!.ProblemDetails.Status);
        Assert.Equal("Recipe name already exists.", _written.ProblemDetails.Title);
        Assert.Equal(exception.Message, _written.ProblemDetails.Detail);
    }

    [Fact]
    public async Task Unmapped_exception_is_not_handled_and_nothing_is_written()
    {
        var (handled, _) = await HandleAsync(new InvalidOperationException("boom"));

        Assert.False(handled);
        _ = _problemDetails.DidNotReceive().TryWriteAsync(Arg.Any<ProblemDetailsContext>());
    }
}
