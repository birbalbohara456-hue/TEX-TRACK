-- TexTrack ERP v0.3 Build 2.1 migration hotfix
-- Corrects the inventory foundation and activates the real Stock Item engine.
-- Approved rules: UQC and Godown are user-created flat masters; only the
-- Raw Material, Work In Progress and Finished Goods Stock Groups are system roots.

-- Remove unapproved demonstration UQCs introduced by the foundation seed.
DELETE FROM uqcs
WHERE is_system = true
  AND name_normalized IN ('PIECES', 'METRES', 'KILOGRAMS');

-- UQCs are never permanent system masters. Existing user-created UQCs are retained.
UPDATE uqcs SET is_system = false WHERE is_system = true;

-- Godowns are flat masters in the approved design.
-- Build 1 may have created user godowns below the automatic Main Godown.
-- Remove the self-reference first, preserve every user-created godown by
-- clearing its old parent, then remove only the unapproved system seed.
ALTER TABLE godowns DROP CONSTRAINT IF EXISTS fk_godowns_parent;
UPDATE godowns SET parent_id = NULL WHERE parent_id IS NOT NULL;
ALTER TABLE godowns DROP CONSTRAINT IF EXISTS ck_godowns_type;
DROP INDEX IF EXISTS ix_godowns_company_parent;
DROP INDEX IF EXISTS ix_godowns_company_type;
ALTER TABLE godowns DROP COLUMN IF EXISTS parent_id;
ALTER TABLE godowns DROP COLUMN IF EXISTS godown_type;

-- Remove the unapproved automatic Main Godown while keeping every user-created godown.
DELETE FROM godowns
WHERE is_system = true
  AND name_normalized = 'MAIN GODOWN';

UPDATE godowns SET is_system = false WHERE is_system = true;

CREATE TABLE IF NOT EXISTS stock_items
(
    id bigserial PRIMARY KEY,
    company_id bigint NOT NULL REFERENCES companies(id) ON DELETE RESTRICT,
    name varchar(200) NOT NULL,
    name_normalized varchar(200) NOT NULL,
    alias varchar(200) NOT NULL DEFAULT '',
    stock_group_id bigint NOT NULL REFERENCES stock_groups(id) ON DELETE RESTRICT,
    uqc_id bigint NOT NULL REFERENCES uqcs(id) ON DELETE RESTRICT,
    stock_category_id bigint NULL REFERENCES stock_categories(id) ON DELETE RESTRICT,
    tax_mode varchar(40) NOT NULL DEFAULT 'NotApplicable',
    tax_classification_id bigint NULL REFERENCES tax_classifications(id) ON DELETE RESTRICT,
    hsn_code varchar(20) NOT NULL,
    igst_rate numeric(7,4) NOT NULL DEFAULT 0,
    cgst_rate numeric(7,4) NOT NULL DEFAULT 0,
    sgst_rate numeric(7,4) NOT NULL DEFAULT 0,
    is_system boolean NOT NULL DEFAULT false,
    is_active boolean NOT NULL DEFAULT true,
    created_at_utc timestamp with time zone NOT NULL,
    modified_at_utc timestamp with time zone NOT NULL,
    created_by varchar(100) NOT NULL,
    modified_by varchar(100) NOT NULL,
    concurrency_token varchar(32) NOT NULL,
    CONSTRAINT ck_stock_items_tax_mode CHECK (tax_mode IN ('NotApplicable', 'InventoryTaxGroup', 'DirectRates')),
    CONSTRAINT ck_stock_items_rates CHECK
    (
        igst_rate BETWEEN 0 AND 100 AND
        cgst_rate BETWEEN 0 AND 100 AND
        sgst_rate BETWEEN 0 AND 100
    ),
    CONSTRAINT ck_stock_items_tax_source CHECK
    (
        (tax_mode = 'InventoryTaxGroup' AND tax_classification_id IS NOT NULL) OR
        (tax_mode <> 'InventoryTaxGroup' AND tax_classification_id IS NULL)
    )
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_stock_items_company_name
    ON stock_items(company_id, name_normalized);
CREATE INDEX IF NOT EXISTS ix_stock_items_company_group
    ON stock_items(company_id, stock_group_id, name_normalized);
CREATE INDEX IF NOT EXISTS ix_stock_items_company_uqc
    ON stock_items(company_id, uqc_id);
CREATE INDEX IF NOT EXISTS ix_stock_items_company_hsn
    ON stock_items(company_id, hsn_code);

CREATE TABLE IF NOT EXISTS stock_item_colours
(
    stock_item_id bigint NOT NULL REFERENCES stock_items(id) ON DELETE CASCADE,
    colour_id bigint NOT NULL REFERENCES colours(id) ON DELETE RESTRICT,
    PRIMARY KEY (stock_item_id, colour_id)
);
CREATE INDEX IF NOT EXISTS ix_stock_item_colours_colour
    ON stock_item_colours(colour_id, stock_item_id);

CREATE TABLE IF NOT EXISTS stock_item_sizes
(
    stock_item_id bigint NOT NULL REFERENCES stock_items(id) ON DELETE CASCADE,
    size_id bigint NOT NULL REFERENCES sizes(id) ON DELETE RESTRICT,
    PRIMARY KEY (stock_item_id, size_id)
);
CREATE INDEX IF NOT EXISTS ix_stock_item_sizes_size
    ON stock_item_sizes(size_id, stock_item_id);

CREATE TABLE IF NOT EXISTS stock_item_variants
(
    id bigserial PRIMARY KEY,
    company_id bigint NOT NULL REFERENCES companies(id) ON DELETE RESTRICT,
    stock_item_id bigint NOT NULL REFERENCES stock_items(id) ON DELETE CASCADE,
    colour_id bigint NULL REFERENCES colours(id) ON DELETE RESTRICT,
    size_id bigint NULL REFERENCES sizes(id) ON DELETE RESTRICT,
    variant_key varchar(100) NOT NULL,
    is_active boolean NOT NULL DEFAULT true,
    created_at_utc timestamp with time zone NOT NULL,
    modified_at_utc timestamp with time zone NOT NULL,
    created_by varchar(100) NOT NULL,
    modified_by varchar(100) NOT NULL,
    concurrency_token varchar(32) NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS uq_stock_item_variants_item_key
    ON stock_item_variants(stock_item_id, variant_key);
CREATE INDEX IF NOT EXISTS ix_stock_item_variants_lookup
    ON stock_item_variants(company_id, colour_id, size_id, stock_item_id);
