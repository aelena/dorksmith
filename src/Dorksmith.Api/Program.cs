using System.Text.Json;
using System.Text.Json.Serialization;
using Dorksmith.Api.Catalogs;
using Dorksmith.Api.Configuration;
using Dorksmith.Api.Endpoints;
using Dorksmith.Api.Generation;
using Dorksmith.Api.Http;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    EnvironmentName = Environment.GetEnvironmentVariable("APP_ENVIRONMENT"),
});

builder.Configuration.AddFlatEnvironmentVariables();

if (builder.Environment.IsDevelopment())
    builder.Logging.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; });
else
    builder.Logging.ClearProviders().AddJsonConsole(o => { o.UseUtcTimestamp = true; o.TimestampFormat = "O"; });

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
});

builder.Services.AddAppOptions(builder.Configuration);
builder.Services.AddSingleton<ICatalogProvider, JsonCatalogProvider>();
builder.Services.AddSingleton<RequestValidator>();
builder.Services.AddSingleton<PlaceholderResolver>();
builder.Services.AddSingleton<IDorkGenerator, DorkGenerator>();
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 16 * 1024);
builder.Services.AddHealthChecks()
    .AddCheck<CatalogHealthCheck>("catalogs", tags: [HealthEndpoints.ReadyTag]);

var app = builder.Build();

app.UseApiErrorHandling();
app.UseSecurityHeaders();
app.MapHealthEndpoints();
app.MapCatalogEndpoints();
app.MapConfigEndpoints();
app.MapDorkEndpoints();
app.MapStaticWeb();

app.Logger.LogInformation("Dorksmith API starting in {Environment}", app.Environment.EnvironmentName);
app.Run();

public partial class Program;
