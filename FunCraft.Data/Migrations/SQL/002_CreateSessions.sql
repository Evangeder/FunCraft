CREATE TABLE IF NOT EXISTS sessions (
                                        id               BIGSERIAL   PRIMARY KEY,
                                        uuid             UUID        NOT NULL,
                                        username         TEXT        NOT NULL,
                                        ip_address       TEXT        NOT NULL,
                                        connected_at     TIMESTAMPTZ NOT NULL,
                                        disconnected_at  TIMESTAMPTZ
);

CREATE INDEX IF NOT EXISTS idx_sessions_uuid ON sessions (uuid);