CREATE TABLE IF NOT EXISTS inventory (
                                         uuid       UUID     NOT NULL REFERENCES players(uuid) ON DELETE CASCADE,
                                         slot       SMALLINT NOT NULL,
                                         item_id    INTEGER  NOT NULL,
                                         item_count SMALLINT NOT NULL,
                                         PRIMARY KEY (uuid, slot)
);