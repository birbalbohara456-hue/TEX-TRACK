-- TexTrack ERP v0.4 Build 1
-- Shared voucher header foundation + first working Job Work Out Order engine.
-- JWO is a planning/order voucher only: this migration creates no stock or ledger postings.

CREATE TABLE IF NOT EXISTS vouchers
(
    id bigserial PRIMARY KEY,
    company_id bigint NOT NULL REFERENCES companies(id) ON DELETE RESTRICT,
    financial_year_id bigint NOT NULL REFERENCES financial_years(id) ON DELETE RESTRICT,
    voucher_type_id bigint NOT NULL REFERENCES voucher_types(id) ON DELETE RESTRICT,
    sequence_number integer NOT NULL,
    voucher_number varchar(100) NOT NULL,
    voucher_number_normalized varchar(100) NOT NULL,
    voucher_date date NOT NULL,
    reference_number varchar(100) NOT NULL DEFAULT '',
    batch varchar(100) NOT NULL DEFAULT '',
    party_ledger_id bigint NULL REFERENCES ledgers(id) ON DELETE RESTRICT,
    due_date date NULL,
    narration varchar(1000) NOT NULL DEFAULT '',
    status varchar(30) NOT NULL DEFAULT 'Open',
    created_at_utc timestamp with time zone NOT NULL,
    modified_at_utc timestamp with time zone NOT NULL,
    created_by varchar(100) NOT NULL,
    modified_by varchar(100) NOT NULL,
    concurrency_token varchar(32) NOT NULL,
    CONSTRAINT ck_vouchers_sequence_positive CHECK (sequence_number > 0),
    CONSTRAINT ck_vouchers_status CHECK (status IN ('Open', 'PartiallyProcessed', 'Completed', 'Cancelled')),
    CONSTRAINT uq_vouchers_number UNIQUE
        (company_id, financial_year_id, voucher_type_id, voucher_number_normalized),
    CONSTRAINT uq_vouchers_sequence UNIQUE
        (company_id, financial_year_id, voucher_type_id, sequence_number)
);
CREATE INDEX IF NOT EXISTS ix_vouchers_company_date
    ON vouchers(company_id, voucher_date DESC, id DESC);
CREATE INDEX IF NOT EXISTS ix_vouchers_company_party_status
    ON vouchers(company_id, party_ledger_id, status, voucher_date DESC);
CREATE INDEX IF NOT EXISTS ix_vouchers_company_batch
    ON vouchers(company_id, batch, voucher_date DESC);

CREATE TABLE IF NOT EXISTS job_work_order_finished_goods
(
    id bigserial PRIMARY KEY,
    voucher_id bigint NOT NULL REFERENCES vouchers(id) ON DELETE CASCADE,
    line_number integer NOT NULL,
    stock_item_id bigint NOT NULL REFERENCES stock_items(id) ON DELETE RESTRICT,
    colour_id bigint NULL REFERENCES colours(id) ON DELETE RESTRICT,
    ordered_quantity numeric(19,4) NOT NULL,
    CONSTRAINT ck_jwo_fg_line_positive CHECK (line_number > 0),
    CONSTRAINT ck_jwo_fg_qty_positive CHECK (ordered_quantity > 0),
    CONSTRAINT uq_jwo_fg_line UNIQUE (voucher_id, line_number)
);
CREATE INDEX IF NOT EXISTS ix_jwo_fg_item_colour
    ON job_work_order_finished_goods(stock_item_id, colour_id, voucher_id);

CREATE TABLE IF NOT EXISTS job_work_order_size_allocations
(
    id bigserial PRIMARY KEY,
    finished_good_id bigint NOT NULL REFERENCES job_work_order_finished_goods(id) ON DELETE CASCADE,
    stock_item_variant_id bigint NOT NULL REFERENCES stock_item_variants(id) ON DELETE RESTRICT,
    size_id bigint NULL REFERENCES sizes(id) ON DELETE RESTRICT,
    quantity numeric(19,4) NOT NULL,
    CONSTRAINT ck_jwo_size_qty_positive CHECK (quantity > 0),
    CONSTRAINT uq_jwo_size_variant UNIQUE (finished_good_id, stock_item_variant_id)
);
CREATE INDEX IF NOT EXISTS ix_jwo_size_variant_lookup
    ON job_work_order_size_allocations(stock_item_variant_id, finished_good_id);

CREATE TABLE IF NOT EXISTS job_work_order_components
(
    id bigserial PRIMARY KEY,
    finished_good_id bigint NOT NULL REFERENCES job_work_order_finished_goods(id) ON DELETE CASCADE,
    line_number integer NOT NULL,
    stock_item_id bigint NOT NULL REFERENCES stock_items(id) ON DELETE RESTRICT,
    uqc_id bigint NOT NULL REFERENCES uqcs(id) ON DELETE RESTRICT,
    required_quantity numeric(19,4) NOT NULL,
    CONSTRAINT ck_jwo_component_line_positive CHECK (line_number > 0),
    CONSTRAINT ck_jwo_component_qty_positive CHECK (required_quantity > 0),
    CONSTRAINT uq_jwo_component_line UNIQUE (finished_good_id, line_number),
    CONSTRAINT uq_jwo_component_item UNIQUE (finished_good_id, stock_item_id)
);
CREATE INDEX IF NOT EXISTS ix_jwo_component_item
    ON job_work_order_components(stock_item_id, finished_good_id);

-- Generic immutable linkage foundation for Material Out, Material In and later vouchers.
CREATE TABLE IF NOT EXISTS voucher_links
(
    id bigserial PRIMARY KEY,
    company_id bigint NOT NULL REFERENCES companies(id) ON DELETE RESTRICT,
    source_voucher_id bigint NOT NULL REFERENCES vouchers(id) ON DELETE RESTRICT,
    target_voucher_id bigint NOT NULL REFERENCES vouchers(id) ON DELETE RESTRICT,
    link_type varchar(60) NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    created_by varchar(100) NOT NULL,
    CONSTRAINT ck_voucher_link_not_self CHECK (source_voucher_id <> target_voucher_id),
    CONSTRAINT uq_voucher_link UNIQUE (company_id, source_voucher_id, target_voucher_id, link_type)
);
CREATE INDEX IF NOT EXISTS ix_voucher_links_target
    ON voucher_links(company_id, target_voucher_id, link_type);
