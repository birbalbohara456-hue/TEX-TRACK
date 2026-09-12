CREATE TABLE tally_company_links
(
    id bigserial PRIMARY KEY,
    company_id bigint NOT NULL REFERENCES companies(id) ON DELETE RESTRICT,
    tally_company_name varchar(300) NOT NULL,
    tally_company_identity varchar(100) NOT NULL,
    is_confirmed boolean NOT NULL DEFAULT false,
    created_at_utc timestamptz NOT NULL,
    modified_at_utc timestamptz NOT NULL,
    created_by varchar(100) NOT NULL,
    modified_by varchar(100) NOT NULL,
    concurrency_token varchar(64) NOT NULL,
    CONSTRAINT uq_tally_company_identity UNIQUE (company_id, tally_company_identity)
);

CREATE TABLE tally_exchange_batches
(
    id bigserial PRIMARY KEY,
    company_id bigint NOT NULL REFERENCES companies(id) ON DELETE RESTRICT,
    tally_company_link_id bigint NULL REFERENCES tally_company_links(id) ON DELETE RESTRICT,
    direction varchar(20) NOT NULL,
    file_name varchar(300) NOT NULL,
    file_hash varchar(64) NOT NULL,
    status varchar(30) NOT NULL,
    new_master_count integer NOT NULL DEFAULT 0,
    new_voucher_count integer NOT NULL DEFAULT 0,
    updated_voucher_count integer NOT NULL DEFAULT 0,
    unchanged_voucher_count integer NOT NULL DEFAULT 0,
    cancelled_voucher_count integer NOT NULL DEFAULT 0,
    exception_count integer NOT NULL DEFAULT 0,
    summary text NOT NULL DEFAULT '',
    created_at_utc timestamptz NOT NULL,
    modified_at_utc timestamptz NOT NULL,
    created_by varchar(100) NOT NULL,
    modified_by varchar(100) NOT NULL,
    concurrency_token varchar(64) NOT NULL
);
CREATE INDEX ix_tally_exchange_batches_company_created ON tally_exchange_batches(company_id, created_at_utc);

CREATE TABLE tally_sync_records
(
    id bigserial PRIMARY KEY,
    company_id bigint NOT NULL REFERENCES companies(id) ON DELETE RESTRICT,
    tally_company_link_id bigint NOT NULL REFERENCES tally_company_links(id) ON DELETE RESTRICT,
    voucher_id bigint NULL REFERENCES vouchers(id) ON DELETE SET NULL,
    tally_guid varchar(160) NOT NULL,
    tally_remote_id varchar(200) NOT NULL DEFAULT '',
    voucher_type_name varchar(100) NOT NULL,
    voucher_number varchar(100) NOT NULL,
    source_hash varchar(64) NOT NULL,
    sync_state varchar(40) NOT NULL,
    last_imported_at_utc timestamptz NULL,
    last_exported_at_utc timestamptz NULL,
    created_at_utc timestamptz NOT NULL,
    modified_at_utc timestamptz NOT NULL,
    created_by varchar(100) NOT NULL,
    modified_by varchar(100) NOT NULL,
    concurrency_token varchar(64) NOT NULL,
    CONSTRAINT uq_tally_sync_company_guid UNIQUE (tally_company_link_id, tally_guid)
);
CREATE INDEX ix_tally_sync_records_company_voucher ON tally_sync_records(company_id, voucher_id);

CREATE TABLE tally_import_exceptions
(
    id bigserial PRIMARY KEY,
    company_id bigint NOT NULL REFERENCES companies(id) ON DELETE RESTRICT,
    exchange_batch_id bigint NOT NULL REFERENCES tally_exchange_batches(id) ON DELETE CASCADE,
    tally_guid varchar(160) NOT NULL DEFAULT '',
    voucher_type_name varchar(100) NOT NULL DEFAULT '',
    voucher_number varchar(100) NOT NULL DEFAULT '',
    order_number varchar(100) NOT NULL DEFAULT '',
    reason_code varchar(60) NOT NULL,
    message varchar(1000) NOT NULL,
    payload_xml text NOT NULL DEFAULT '',
    status varchar(30) NOT NULL DEFAULT 'Open',
    created_at_utc timestamptz NOT NULL,
    modified_at_utc timestamptz NOT NULL,
    created_by varchar(100) NOT NULL,
    modified_by varchar(100) NOT NULL,
    concurrency_token varchar(64) NOT NULL
);
CREATE INDEX ix_tally_import_exceptions_company_status ON tally_import_exceptions(company_id, status, created_at_utc);

CREATE TABLE tally_variant_allocation_tasks
(
    id bigserial PRIMARY KEY,
    company_id bigint NOT NULL REFERENCES companies(id) ON DELETE RESTRICT,
    exchange_batch_id bigint NOT NULL REFERENCES tally_exchange_batches(id) ON DELETE CASCADE,
    tally_guid varchar(160) NOT NULL,
    voucher_type_name varchar(100) NOT NULL,
    voucher_number varchar(100) NOT NULL,
    stock_item_name varchar(300) NOT NULL,
    quantity numeric(19,4) NOT NULL,
    uqc_name varchar(40) NOT NULL,
    payload_xml text NOT NULL,
    status varchar(30) NOT NULL DEFAULT 'Pending',
    created_at_utc timestamptz NOT NULL,
    modified_at_utc timestamptz NOT NULL,
    created_by varchar(100) NOT NULL,
    modified_by varchar(100) NOT NULL,
    concurrency_token varchar(64) NOT NULL
);
CREATE INDEX ix_tally_variant_tasks_company_status ON tally_variant_allocation_tasks(company_id, status, created_at_utc);
