using System.Text.Json;
using Dorksmith.Api.Contracts;

namespace Dorksmith.Api.Http;

/// <summary>Reads a JSON request body explicitly so malformed or empty payloads yield the stable <see cref="ApiError"/> envelope in every environment.</summary>
public static class JsonBody
{
    public static async Task<(T? Value, IResult? Error)> ReadAsync<T>(HttpRequest request, CancellationToken ct) where T : class
    {
        if (request.ContentLength is 0) return (null, ApiErrors.InvalidInput("A JSON body is required.", "body"));
        if (request.ContentType is { } ctype && !ctype.Contains("json", StringComparison.OrdinalIgnoreCase))
            return (null, ApiErrors.InvalidInput("Content-Type must be application/json.", "body"));
        try
        {
            var value = await JsonSerializer.DeserializeAsync<T>(request.Body, AppJson.Options, ct);
            return value is null ? (null, ApiErrors.InvalidInput("A JSON body is required.", "body")) : (value, null);
        }
        catch (JsonException)
        {
            return (null, ApiErrors.InvalidInput("Request body is not valid JSON.", "body"));
        }
    }
}
