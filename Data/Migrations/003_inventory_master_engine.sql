-- TexTrack ERP v0.3 Build 1
-- Inventory master engine: hierarchical godowns, garment ordering fields,
-- stronger identifiers and database-level integrity controls.

ALTER TABLE godowns
    ADD COLUMN IF NOT EXISTS parent_id bigint NULL,
    ADD COLUMN IF NOT EXISTS godown_type varchar(30) NOT NULL DEFAULT 'Internal',
    ADD COLUMN IF NOT EXISTS address_line1 varchar(250) NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS address_line2 varchar(250) NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS city varchar(100) NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS state varchar(100) NOT NULL DEFAULT '';

DO $$
BEGIN
    IF NOT EXISTS
    (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'fk_godowns_parent'
    ) THEN
        ALTER TABLE godowns
            ADD CONSTRAINT fk_godowns_parent
            FOREIGN KEY (parent_id) REFERENCES godowns(id) ON DELETE RESTRICT;
    END IF;
END $$;

DO $$
BEGIN
    IF NOT EXISTS
    (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'ck_godowns_type'
    ) THEN
        ALTER TABLE godowns
            ADD CONSTRAINT ck_godowns_type
            CHECK (godown_type IN ('Internal', 'JobWorker', 'ThirdParty'));
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS ix_godowns_company_parent
    ON godowns(company_id, parent_id);

CREATE INDEX IF NOT EXISTS ix_godowns_company_type
    ON godowns(company_id, godown_type);

ALTER TABLE colours
    ADD COLUMN IF NOT EXISTS colour_code varchar(30) NOT NULL DEFAULT '';

CREATE UNIQUE INDEX IF NOT EXISTS uq_colours_company_code
    ON colours(company_id, upper(colour_code))
    WHERE btrim(colour_code) <> '';

ALTER TABLE sizes
    ADD COLUMN IF NOT EXISTS display_order integer NOT NULL DEFAULT 100;

ALTER TABLE processes
    ADD COLUMN IF NOT EXISTS display_order integer NOT NULL DEFAULT 100;

DO $$
BEGIN
    IF NOT EXISTS
    (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'ck_sizes_display_order'
    ) THEN
        ALTER TABLE sizes
            ADD CONSTRAINT ck_sizes_display_order
            CHECK (display_order BETWEEN 1 AND 9999);
    END IF;
END $$;

DO $$
BEGIN
    IF NOT EXISTS
    (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'ck_processes_display_order'
    ) THEN
        ALTER TABLE processes
            ADD CONSTRAINT ck_processes_display_order
            CHECK (display_order BETWEEN 1 AND 9999);
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS ix_sizes_company_display_order
    ON sizes(company_id, display_order, name_normalized);

CREATE INDEX IF NOT EXISTS ix_processes_company_display_order
    ON processes(company_id, display_order, name_normalized);

CREATE UNIQUE INDEX IF NOT EXISTS uq_uqcs_company_short_name
    ON uqcs(company_id, upper(short_name));

UPDATE godowns
SET godown_type = 'Internal'
WHERE godown_type IS NULL OR btrim(godown_type) = '';
