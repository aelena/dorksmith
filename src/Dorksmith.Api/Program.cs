using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dorksmith.Api.Catalogs;
using Dorksmith.Api.Configuration;
using Dorksmith.Api.Endpoints;
using Dorksmith.Api.Generation;
using Dorksmith.Api.Handles;
using Dorksmith.Api.Http;
using Dorksmith.Api.Logging;
using Dorksmith.Api.Privacy;
using Dorksmith.Api.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

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

builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 16 * 1024);

builder.Services.AddAppOptions(builder.Configuration);
builder.Services.AddSingleton(TimeProvider.System);

// Catalogs + generation
builder.Services.AddSingleton<ICatalogProvider, JsonCatalogProvider>();
builder.Services.AddSingleton<RequestValidator>();
builder.Services.AddSingleton<PlaceholderResolver>();
builder.Services.AddSingleton<IDorkGenerator, DorkGenerator>();
builder.Services.AddSingleton<IHandleExpander, HandleExpander>();

// Privacy, quota, search log
builder.Services.AddSingleton<ClientKeyProvider>();
builder.Services.AddSingleton<IRequestQuotaService, InMemoryRequestQuotaService>();
builder.Services.AddSingleton<ISearchLogStore>(sp =>
    sp.GetRequiredService<IOptions<SearchLogOptions>>().Value.Enabled
        ? ActivatorUtilities.CreateInstance<SqliteSearchLogStore>(sp)
        : new NullSearchLogStore());
builder.Services.AddSingleton<ISearchLogReader>(sp => (ISearchLogReader)sp.GetRequiredService<ISearchLogStore>());
builder.Services.AddSingleton<SearchLogWriter>();
builder.Services.AddHostedService<RetentionCleanupService>();

builder.Services.AddHealthChecks()
    .AddCheck<CatalogHealthCheck>("catalogs", tags: [HealthEndpoints.ReadyTag])
    .AddCheck<SearchLogHealthCheck>("search-log", tags: [HealthEndpoints.ReadyTag]);

// Reverse-proxy awareness: only trust X-Forwarded-* from configured proxies/networks.
var proxy = builder.Configuration.GetSection(ProxyOptions.Section).Get<ProxyOptions>() ?? new ProxyOptions();
var trustsProxies = proxy.KnownProxies.Length > 0 || proxy.KnownNetworks.Length > 0;
if (trustsProxies)
{
    builder.Services.Configure<ForwardedHeadersOptions>(o =>
    {
        o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        o.ForwardLimit = proxy.ForwardLimit;
        o.KnownProxies.Clear();
        o.KnownNetworks.Clear();
        foreach (var p in proxy.KnownProxies)
            if (IPAddress.TryParse(p, out var ip)) o.KnownProxies.Add(ip);
        foreach (var n in proxy.KnownNetworks)
        {
            var parts = n.Split('/');
            if (parts.Length == 2 && IPAddress.TryParse(parts[0], out var prefix) && int.TryParse(parts[1], out var len))
                o.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(prefix, len));
        }
    });
}

var web = builder.Configuration.GetSection(WebOptions.Section).Get<WebOptions>() ?? new WebOptions();
if (web.CorsAllowedOrigins.Length > 0)
{
    builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
        .WithOrigins(web.CorsAllowedOrigins)
        .WithMethods("GET", "POST")
        .WithHeaders("Content-Type", "If-None-Match")
        .WithExposedHeaders("ETag", "RateLimit-Limit", "RateLimit-Remaining", "RateLimit-Reset", "Retry-After", "X-Request-Id")));
}

var app = builder.Build();

if (trustsProxies) app.UseForwardedHeaders();
app.UseApiErrorHandling();
app.UseSecurityHeaders();
if (web.CorsAllowedOrigins.Length > 0) app.UseCors();

app.MapHealthEndpoints();
app.MapCatalogEndpoints();
app.MapConfigEndpoints();
app.MapDorkEndpoints();
app.MapHandleEndpoints();
app.MapStaticWeb();

var privacy = app.Services.GetRequiredService<IOptions<PrivacyOptions>>().Value;
var rate = app.Services.GetRequiredService<IOptions<RateLimitingOptions>>().Value;
var searchLog = app.Services.GetRequiredService<IOptions<SearchLogOptions>>().Value;
app.Logger.LogInformation(
    "Dorksmith API starting in {Environment}: rate limit {Enabled} {Permit}/{Window}min, search log {LogEnabled} ({Retention}d, {Path}), ip mode {IpMode}, proxies trusted {Proxies}",
    app.Environment.EnvironmentName, rate.Enabled, rate.PermitLimit, rate.WindowMinutes, searchLog.Enabled, searchLog.RetentionDays, searchLog.SqlitePath, privacy.IpLoggingMode, trustsProxies);

app.Run();

public partial class Program;
