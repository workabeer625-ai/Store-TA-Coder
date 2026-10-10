using Marina.Application.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Marina.Api.Services;

public sealed class ApiExceptionHandler(
    ILogger<ApiExceptionHandler> log,
    IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        log.LogError(exception, "Request {TraceId} failed", context.TraceIdentifier);

        var (status, title, code) = exception switch
        {
            ValidationException => (400, "The request is invalid.", "validation_failed"),
            ArgumentException => (400, "The request is invalid.", "invalid_argument"),
            UnauthorizedAccessException => (401, "Authentication is required.", "unauthorized"),
            KeyNotFoundException => (404, "The requested resource was not found.", "not_found"),
            DbUpdateConcurrencyException => (409, "This record changed since you opened it. Refresh and try again.", "concurrency_conflict"),
            DbUpdateException db when IsConstraintViolation(db) => (409, "The operation conflicts with existing data.", "data_conflict"),
            InvalidOperationException => (409, "The operation cannot be completed in the current state.", "business_conflict"),
            _ => (500, "An unexpected error occurred.", "request_failed")
        };

        context.Response.StatusCode = status;
        var details = new ProblemDetails { Status = status, Title = title };
        details.Extensions["traceId"] = context.TraceIdentifier;
        details.Extensions["code"] = code;
        if (exception is ValidationException validation)
            details.Extensions["errors"] = validation.Errors;

        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = details,
            Exception = exception
        });
    }

    private static bool IsConstraintViolation(DbUpdateException exception)
    {
        for (var current = exception.InnerException; current is not null; current = current.InnerException)
            if (current is SqlException sql && sql.Errors.Cast<SqlError>().Any(error => error.Number is 2601 or 2627 or 547))
                return true;
        return false;
    }
}
