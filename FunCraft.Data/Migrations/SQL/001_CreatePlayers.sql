CREATE TABLE IF NOT EXISTS players (
                                       uuid      UUID        PRIMARY KEY,
                                       username  TEXT        NOT NULL,
                                       x         DOUBLE PRECISION NOT NULL DEFAULT 0,
                                       y         DOUBLE PRECISION NOT NULL DEFAULT 64,
                                       z         DOUBLE PRECISION NOT NULL DEFAULT 0,
                                       yaw       REAL        NOT NULL DEFAULT 0,
                                       pitch     REAL        NOT NULL DEFAULT 0,
                                       last_seen TIMESTAMPTZ NOT NULL DEFAULT NOW()
);