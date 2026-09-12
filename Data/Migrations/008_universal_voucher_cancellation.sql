-- TexTrack ERP v0.5 Build 1.2
-- Universal voucher cancellation metadata. Cancellation preserves voucher number and original rows.

ALTER TABLE vouchers
    ADD COLUMN IF NOT EXISTS cancellation_reason varchar(500) NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS cancelled_at_utc timestamp with time zone NULL,
    ADD COLUMN IF NOT EXISTS cancelled_by varchar(100) NOT NULL DEFAULT '';

CREATE INDEX IF NOT EXISTS ix_vouchers_cancelled
    ON vouchers(company_id, financial_year_id, status, cancelled_at_utc);
