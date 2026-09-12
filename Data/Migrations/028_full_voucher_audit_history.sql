-- Full voucher audit history. Additive and safe for existing vouchers.
-- Existing rows receive an immutable identity; the application records an
-- honestly-labelled current-state baseline after the migration is applied.
ALTER TABLE vouchers
    ADD COLUMN audit_identity uuid;

UPDATE vouchers
SET audit_identity = gen_random_uuid()
WHERE audit_identity IS NULL;

ALTER TABLE vouchers
    ALTER COLUMN audit_identity SET DEFAULT gen_random_uuid(),
    ALTER COLUMN audit_identity SET NOT NULL;

CREATE UNIQUE INDEX ux_vouchers_audit_identity
    ON vouchers(audit_identity);

CREATE TABLE voucher_audit_revisions
(
    id bigserial PRIMARY KEY,
    voucher_audit_identity uuid NOT NULL,
    voucher_id bigint NOT NULL,
    company_id bigint NOT NULL,
    company_name varchar(300) NOT NULL,
    financial_year_id bigint NOT NULL,
    financial_year_name varchar(100) NOT NULL,
    voucher_type_id bigint NOT NULL,
    voucher_type_name varchar(200) NOT NULL,
    voucher_type_code varchar(100) NOT NULL,
    voucher_number varchar(100) NOT NULL,
    revision_number integer NOT NULL CHECK (revision_number > 0),
    snapshot_schema_version integer NOT NULL CHECK (snapshot_schema_version > 0),
    action varchar(50) NOT NULL,
    reason varchar(1000) NOT NULL DEFAULT '',
    -- Stored as exact text so the bytes covered by content_hash remain
    -- reproducible after a database round-trip. The application parses both
    -- values as JSON before display.
    snapshot_json text NOT NULL,
    changes_json text NOT NULL,
    content_hash varchar(64) NOT NULL CHECK (length(content_hash) = 64),
    previous_chain_hash varchar(64) NOT NULL DEFAULT '',
    chain_hash varchar(64) NOT NULL CHECK (length(chain_hash) = 64),
    recorded_at_utc timestamp with time zone NOT NULL,
    recorded_by varchar(200) NOT NULL,
    CONSTRAINT ux_voucher_audit_revision_number
        UNIQUE (voucher_audit_identity, revision_number),
    CONSTRAINT ux_voucher_audit_chain_hash UNIQUE (chain_hash)
);

-- Deliberately no foreign keys: audit evidence must survive deletion of the
-- live voucher, company-period changes and later master maintenance.
CREATE INDEX ix_voucher_audit_revisions_company_time
    ON voucher_audit_revisions(company_id, recorded_at_utc DESC);
CREATE INDEX ix_voucher_audit_revisions_company_voucher
    ON voucher_audit_revisions(company_id, voucher_id);
CREATE INDEX ix_voucher_audit_revisions_identity_time
    ON voucher_audit_revisions(voucher_audit_identity, recorded_at_utc);
