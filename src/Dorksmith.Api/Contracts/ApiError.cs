namespace Dorksmith.Api.Contracts;

/// <summary>Stable error envelope for every non-2xx API response.</summary>
public sealed record ApiError(string Error, string Message, string? Field = null, int? RetryAfterSeconds = null);

public static class ApiErrors
{
    public static IResult InvalidInput(string message, string? field = null)
        => Results.Json(new ApiError("invalid_input", message, field), statusCode: StatusCodes.Status400BadRequest);

    public static IResult CannotGenerate(string message, string? field = null)
        => Results.Json(new ApiError("cannot_generate", message, field), statusCode: StatusCodes.Status422UnprocessableEntity);

    public static IResult NotFound(string message)
        => Results.Json(new ApiError("not_found", message), statusCode: StatusCodes.Status404NotFound);

    public static IResult NotReady(string message = "Catalogs are not loaded.")
        => Results.Json(new ApiError("not_ready", message), statusCode: StatusCodes.Status503ServiceUnavailable);

    public static IResult FeatureDisabled(string message)
        => Results.Json(new ApiError("feature_disabled", message), statusCode: StatusCodes.Status404NotFound);

    public static IResult PayloadTooLarge(string message = "Request payload too large.")
        => Results.Json(new ApiError("payload_too_large", message), statusCode: StatusCodes.Status413PayloadTooLarge);
}

/// <summary>Thrown by validators; converted to a 400/422 by the endpoint layer.</summary>
public sealed class InputValidationException(string message, string? field = null, bool unprocessable = false) : Exception(message)
{
    public string? Field { get; } = field;
    public bool Unprocessable { get; } = unprocessable;
    public IResult ToResult() => Unprocessable ? ApiErrors.CannotGenerate(Message, Field) : ApiErrors.InvalidInput(Message, Field);
}
