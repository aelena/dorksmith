using System.Reflection;
using Dorksmith.Api.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Dorksmith.Api.Logging;

/// <summary>
/// Default store for Docker/VPS deployments. Applies embedded SQL migrations on first use, runs in WAL mode
/// for file databases, and uses parameterised statements throughout. <c>:memory:</c> is supported for tests.
/// </summary>
public sealed class SqliteSearchLogStore : ISearchLogStore, ISearchLogReader, IDisposable
{
    private readonly string _connectionString;
    private readonly SqliteConnection? _keepAlive;
    private readonly ILogger<SqliteSearchLogStore> _logger;
    private readonly SemaphoreSlim _init = new(1, 1);
    private bool _initialized;

    public SqliteSearchLogStore(IOptions<SearchLogOptions> options, IHostEnvironment env, ILogger<SqliteSearchLogStore> logger)
    {
        _logger = logger;
        var path = options.Value.SqlitePath;
        if (path == ":memory:")
        {
            _connectionString = $"Data Source=dorksmith-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
            _keepAlive = new SqliteConnection(_connectionString);
            _keepAlive.Open();
        }
        else
        {
            var full = Path.IsPathRooted(path) ? path : Path.Combine(env.ContentRootPath, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            _connectionString = new SqliteConnectionStringBuilder { DataSource = full, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = true }.ToString();
        }
        DatabasePath = path;
    }

    public string DatabasePath { get; }

    public async Task AppendAsync(SearchLogEntry e, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO search_log (id, occurred_at_utc, client_key, ip_mode, input_type, intent, engine, normalized_input,
                                    options_json, variant_count, http_status, request_duration_ms, catalog_version, user_agent_family)
            VALUES ($id, $at, $key, $mode, $type, $intent, $engine, $input, $options, $variants, $status, $duration, $catalog, $ua)
            """;
        cmd.Parameters.AddWithValue("$id", e.Id);
        cmd.Parameters.AddWithValue("$at", e.OccurredAtUtc.UtcDateTime.ToString("O"));
        cmd.Parameters.AddWithValue("$key", (object?)e.ClientKey ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$mode", e.IpMode);
        cmd.Parameters.AddWithValue("$type", e.InputType);
        cmd.Parameters.AddWithValue("$intent", e.Intent);
        cmd.Parameters.AddWithValue("$engine", e.Engine);
        cmd.Parameters.AddWithValue("$input", e.NormalizedInput);
        cmd.Parameters.AddWithValue("$options", (object?)e.OptionsJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$variants", e.VariantCount);
        cmd.Parameters.AddWithValue("$status", e.HttpStatus);
        cmd.Parameters.AddWithValue("$duration", e.RequestDurationMs);
        cmd.Parameters.AddWithValue("$catalog", e.CatalogVersion);
        cmd.Parameters.AddWithValue("$ua", (object?)e.UserAgentFamily ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<int> PurgeOlderThanAsync(DateTimeOffset cutoffUtc, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM search_log WHERE occurred_at_utc < $cutoff";
        cmd.Parameters.AddWithValue("$cutoff", cutoffUtc.UtcDateTime.ToString("O"));
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<SearchLogEntry>> ReadRecentAsync(int take, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, occurred_at_utc, client_key, ip_mode, input_type, intent, engine, normalized_input, options_json,
                   variant_count, http_status, request_duration_ms, catalog_version, user_agent_family
            FROM search_log ORDER BY occurred_at_utc DESC, id DESC LIMIT $take
            """;
        cmd.Parameters.AddWithValue("$take", take);
        var list = new List<SearchLogEntry>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new SearchLogEntry(
                r.GetString(0), DateTimeOffset.Parse(r.GetString(1), null, System.Globalization.DateTimeStyles.RoundtripKind),
                r.IsDBNull(2) ? null : r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5), r.GetString(6), r.GetString(7),
                r.IsDBNull(8) ? null : r.GetString(8), r.GetInt32(9), r.GetInt32(10), r.GetInt64(11), r.GetString(12),
                r.IsDBNull(13) ? null : r.GetString(13)));
        }
        return list;
    }

    public async Task<long> CountAsync(CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM search_log";
        return (long)(await cmd.ExecuteScalarAsync(ct) ?? 0L);
    }

    public async Task<bool> PingAsync(CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1";
        return (long)(await cmd.ExecuteScalarAsync(ct) ?? 0L) == 1;
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken ct)
    {
        var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        if (!_initialized) await InitializeAsync(conn, ct);
        return conn;
    }

    private async Task InitializeAsync(SqliteConnection conn, CancellationToken ct)
    {
        await _init.WaitAsync(ct);
        try
        {
            if (_initialized) return;
            await Exec(conn, "PRAGMA busy_timeout = 5000;", ct);
            if (_keepAlive is null) await Exec(conn, "PRAGMA journal_mode = WAL;", ct);
            await Exec(conn, "CREATE TABLE IF NOT EXISTS schema_migrations (name TEXT PRIMARY KEY, applied_at_utc TEXT NOT NULL);", ct);

            var applied = new HashSet<string>(StringComparer.Ordinal);
            await using (var read = conn.CreateCommand())
            {
                read.CommandText = "SELECT name FROM schema_migrations";
                await using var r = await read.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct)) applied.Add(r.GetString(0));
            }

            var asm = Assembly.GetExecutingAssembly();
            var resources = asm.GetManifestResourceNames()
                .Where(n => n.Contains(".Migrations.", StringComparison.Ordinal) && n.EndsWith(".sql", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal);
            foreach (var res in resources)
            {
                var name = res[(res.IndexOf(".Migrations.", StringComparison.Ordinal) + ".Migrations.".Length)..];
                if (applied.Contains(name)) continue;
                await using var stream = asm.GetManifestResourceStream(res)!;
                using var reader = new StreamReader(stream);
                var sql = await reader.ReadToEndAsync(ct);
                await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct);
                await Exec(conn, sql, ct, tx);
                await using (var mark = conn.CreateCommand())
                {
                    mark.Transaction = tx;
                    mark.CommandText = "INSERT INTO schema_migrations (name, applied_at_utc) VALUES ($n, $at)";
                    mark.Parameters.AddWithValue("$n", name);
                    mark.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
                    await mark.ExecuteNonQueryAsync(ct);
                }
                await tx.CommitAsync(ct);
                _logger.LogInformation("Applied search-log migration {Migration}", name);
            }
            _initialized = true;
        }
        finally { _init.Release(); }
    }

    private static async Task Exec(SqliteConnection conn, string sql, CancellationToken ct, SqliteTransaction? tx = null)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public void Dispose()
    {
        _keepAlive?.Dispose();
        _init.Dispose();
    }
}

public sealed class SearchLogHealthCheck(ISearchLogStore store, IOptions<SearchLogOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!options.Value.Enabled) return HealthCheckResult.Healthy("search log disabled");
        if (store is not SqliteSearchLogStore sqlite) return HealthCheckResult.Healthy(store.GetType().Name);
        try
        {
            return await sqlite.PingAsync(cancellationToken)
                ? HealthCheckResult.Healthy($"sqlite {sqlite.DatabasePath}")
                : HealthCheckResult.Unhealthy("sqlite ping failed");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("sqlite unavailable: " + ex.Message);
        }
    }
}
