-- Company-level inclusive stock freeze boundary.
-- NULL preserves the pre-migration behaviour: no stock period is frozen.
ALTER TABLE companies
    ADD COLUMN stock_frozen_through date;

COMMENT ON COLUMN companies.stock_frozen_through IS
    'Inclusive last date on which stock-affecting voucher mutations are prohibited.';
