-- TexTrack ERP v0.5 Build 1
-- Universal voucher numbering and Material Out selection/data foundation.

ALTER TABLE voucher_types
    ADD COLUMN IF NOT EXISTS numbering_mode varchar(30) NOT NULL DEFAULT 'Auto',
    ADD COLUMN IF NOT EXISTS suffix varchar(30) NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS number_width integer NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS reset_period varchar(30) NOT NULL DEFAULT 'FinancialYear';

UPDATE voucher_types
SET numbering_mode = CASE
        WHEN allow_manual_numbering THEN 'Manual'
        WHEN prefix <> '' THEN 'AutoPrefixSuffix'
        ELSE 'Auto'
    END
WHERE numbering_mode = 'Auto';

ALTER TABLE voucher_types DROP CONSTRAINT IF EXISTS ck_voucher_types_numbering_mode;
ALTER TABLE voucher_types ADD CONSTRAINT ck_voucher_types_numbering_mode
    CHECK (numbering_mode IN ('Auto', 'AutoPrefixSuffix', 'Manual'));
ALTER TABLE voucher_types DROP CONSTRAINT IF EXISTS ck_voucher_types_number_width;
ALTER TABLE voucher_types ADD CONSTRAINT ck_voucher_types_number_width
    CHECK (number_width BETWEEN 0 AND 12);
ALTER TABLE voucher_types DROP CONSTRAINT IF EXISTS ck_voucher_types_reset_period;
ALTER TABLE voucher_types ADD CONSTRAINT ck_voucher_types_reset_period
    CHECK (reset_period IN ('Never', 'FinancialYear'));

CREATE TABLE IF NOT EXISTS voucher_sequences
(
    company_id bigint NOT NULL REFERENCES companies(id) ON DELETE RESTRICT,
    financial_year_id bigint NOT NULL REFERENCES financial_years(id) ON DELETE RESTRICT,
    voucher_type_id bigint NOT NULL REFERENCES voucher_types(id) ON DELETE RESTRICT,
    last_number integer NOT NULL,
    modified_at_utc timestamp with time zone NOT NULL,
    modified_by varchar(100) NOT NULL,
    PRIMARY KEY (company_id, financial_year_id, voucher_type_id),
    CONSTRAINT ck_voucher_sequences_positive CHECK (last_number >= 0)
);

INSERT INTO voucher_sequences(company_id, financial_year_id, voucher_type_id, last_number, modified_at_utc, modified_by)
SELECT company_id, financial_year_id, voucher_type_id, MAX(sequence_number), now(), 'Migration007'
FROM vouchers
GROUP BY company_id, financial_year_id, voucher_type_id
ON CONFLICT (company_id, financial_year_id, voucher_type_id)
DO UPDATE SET last_number = GREATEST(voucher_sequences.last_number, EXCLUDED.last_number),
              modified_at_utc = EXCLUDED.modified_at_utc,
              modified_by = EXCLUDED.modified_by;

CREATE TABLE IF NOT EXISTS material_out_lines
(
    id bigserial PRIMARY KEY,
    voucher_id bigint NOT NULL REFERENCES vouchers(id) ON DELETE CASCADE,
    jwo_voucher_id bigint NOT NULL REFERENCES vouchers(id) ON DELETE RESTRICT,
    jwo_finished_good_id bigint NOT NULL REFERENCES job_work_order_finished_goods(id) ON DELETE RESTRICT,
    jwo_component_id bigint NOT NULL REFERENCES job_work_order_components(id) ON DELETE RESTRICT,
    line_number integer NOT NULL,
    stock_item_id bigint NOT NULL REFERENCES stock_items(id) ON DELETE RESTRICT,
    uqc_id bigint NOT NULL REFERENCES uqcs(id) ON DELETE RESTRICT,
    source_godown_id bigint NOT NULL REFERENCES godowns(id) ON DELETE RESTRICT,
    destination_godown_id bigint NOT NULL REFERENCES godowns(id) ON DELETE RESTRICT,
    required_quantity numeric(19,4) NOT NULL,
    previously_issued_quantity numeric(19,4) NOT NULL DEFAULT 0,
    issued_quantity numeric(19,4) NOT NULL,
    rate numeric(19,4) NOT NULL DEFAULT 0,
    amount numeric(19,4) NOT NULL DEFAULT 0,
    CONSTRAINT ck_material_out_line_positive CHECK (line_number > 0),
    CONSTRAINT ck_material_out_required_nonnegative CHECK (required_quantity >= 0),
    CONSTRAINT ck_material_out_previous_nonnegative CHECK (previously_issued_quantity >= 0),
    CONSTRAINT ck_material_out_issued_positive CHECK (issued_quantity > 0),
    CONSTRAINT uq_material_out_line UNIQUE (voucher_id, line_number)
);
CREATE INDEX IF NOT EXISTS ix_material_out_jwo_component
    ON material_out_lines(jwo_voucher_id, jwo_component_id, voucher_id);
CREATE INDEX IF NOT EXISTS ix_material_out_stock_godown
    ON material_out_lines(stock_item_id, source_godown_id, voucher_id);
