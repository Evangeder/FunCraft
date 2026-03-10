CREATE TABLE IF NOT EXISTS inventory (
                                         uuid      UUID    NOT NULL REFERENCES players(uuid) ON DELETE CASCADE,
                                         slot      SMALLINT NOT NULL,
                                         item_data JSONB   NOT NULL,
                                         PRIMARY KEY (uuid, slot)
);