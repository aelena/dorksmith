CREATE TABLE IF NOT EXISTS search_log (
    id TEXT PRIMARY KEY,
    occurred_at_utc TEXT NOT NULL,
    client_key TEXT NULL,
    ip_mode TEXT NOT NULL,
    input_type TEXT NOT NULL,
    intent TEXT NOT NULL,
    engine TEXT NOT NULL,
    normalized_input TEXT NOT NULL,
    options_json TEXT NULL,
    variant_count INTEGER NOT NULL,
    http_status INTEGER NOT NULL,
    request_duration_ms INTEGER NOT NULL,
    catalog_version TEXT NOT NULL,
    user_agent_family TEXT NULL
);

CREATE INDEX IF NOT EXISTS ix_search_log_occurred_at
    ON search_log (occurred_at_utc);

CREATE INDEX IF NOT EXISTS ix_search_log_client_key_occurred_at
    ON search_log (client_key, occurred_at_utc);
