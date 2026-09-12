-- Company-owned negative-stock policy.
--
-- New companies block negative stock by default. Existing companies that have
-- already posted stock movements retain the historical permissive behaviour
-- until an administrator reviews and explicitly enables blocking. Empty
-- companies can safely start in blocking mode immediately.
ALTER TABLE companies
    ADD COLUMN allow_negative_stock boolean NOT NULL DEFAULT false;

UPDATE companies AS company
SET allow_negative_stock = EXISTS
(
    SELECT 1
    FROM stock_movements AS movement
    WHERE movement.company_id = company.id
);

INSERT INTO audit_logs
    (company_id, entity_type, entity_id, action, success, description, performed_by, performed_at_utc)
SELECT
    id,
    'CompanyInventoryPolicy',
    id,
    'Migration',
    true,
    CASE
        WHEN allow_negative_stock THEN
            'Migration 041 preserved Allow Negative Stock for an existing company with stock history. Review reconciliation before enabling blocking.'
        ELSE
            'Migration 041 enabled Block Negative Stock for a company without stock history.'
    END,
    'Migration 041',
    NOW()
FROM companies;

COMMENT ON COLUMN companies.allow_negative_stock IS
    'When false, every committed stock mutation must remain non-negative at the exact company/item/variant/UQC/godown position throughout its effective-date history.';
