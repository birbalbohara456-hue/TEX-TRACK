-- Stock Item master opening-inventory workflow.
-- The visible entry belongs to the Stock Item master; the linked Opening Stock
-- voucher remains the immutable posting/audit source underneath it.

ALTER TABLE vouchers
    ADD COLUMN opening_stock_item_id bigint NULL
        REFERENCES stock_items(id) ON DELETE RESTRICT;

CREATE UNIQUE INDEX ux_vouchers_company_year_opening_stock_item
    ON vouchers(company_id, financial_year_id, opening_stock_item_id)
    WHERE opening_stock_item_id IS NOT NULL;

-- When Value is the user's input, Rate is derived at the application's supported
-- precision. Preserve the accepted Value exactly instead of manufacturing a
-- rounding difference merely to force Value = Quantity x rounded Rate.
ALTER TABLE inventory_inward_lines
    DROP CONSTRAINT IF EXISTS ck_inventory_inward_amount_math;

CREATE OR REPLACE FUNCTION validate_opening_stock_voucher_origin()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    effective_type varchar(80);
    item_company_id bigint;
    year_company_id bigint;
    books_beginning date;
BEGIN
    IF NEW.opening_stock_item_id IS NULL THEN
        RETURN NEW;
    END IF;

    SELECT CASE WHEN vt.parent_voucher_type_id IS NOT NULL
                THEN parent.system_type_code ELSE vt.system_type_code END
      INTO effective_type
      FROM voucher_types vt
      LEFT JOIN voucher_types parent ON parent.id = vt.parent_voucher_type_id
     WHERE vt.id = NEW.voucher_type_id;

    SELECT company_id INTO item_company_id
      FROM stock_items WHERE id = NEW.opening_stock_item_id;
    SELECT company_id, start_date INTO year_company_id, books_beginning
      FROM financial_years WHERE id = NEW.financial_year_id;

    IF effective_type <> 'OPENING_STOCK' THEN
        RAISE EXCEPTION 'Only an Opening Stock voucher may be linked to a Stock Item opening balance.';
    END IF;
    IF item_company_id IS NULL
       OR item_company_id <> NEW.company_id
       OR year_company_id <> NEW.company_id THEN
        RAISE EXCEPTION 'Opening Stock item, voucher and financial year must belong to the same company.';
    END IF;
    IF NEW.voucher_date <> books_beginning THEN
        RAISE EXCEPTION 'Stock Item opening inventory must be dated on the Books Beginning Date.';
    END IF;
    IF NEW.party_ledger_id IS NOT NULL THEN
        RAISE EXCEPTION 'Stock Item opening inventory cannot have a party ledger.';
    END IF;
    RETURN NEW;
END $$;

CREATE TRIGGER trg_opening_stock_voucher_origin
BEFORE INSERT OR UPDATE ON vouchers
FOR EACH ROW EXECUTE FUNCTION validate_opening_stock_voucher_origin();

CREATE OR REPLACE FUNCTION validate_inventory_inward_line_integrity()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    voucher_company_id bigint;
    voucher_type_code varchar(80);
    opening_item_id bigint;
    item_company_id bigint;
    item_uqc_id bigint;
    variant_company_id bigint;
    variant_item_id bigint;
    uqc_company_id bigint;
    godown_company_id bigint;
BEGIN
    SELECT v.company_id,
           CASE WHEN vt.parent_voucher_type_id IS NOT NULL
                THEN parent.system_type_code ELSE vt.system_type_code END,
           v.opening_stock_item_id
      INTO voucher_company_id, voucher_type_code, opening_item_id
      FROM vouchers v
      JOIN voucher_types vt ON vt.id = v.voucher_type_id
      LEFT JOIN voucher_types parent ON parent.id = vt.parent_voucher_type_id
     WHERE v.id = NEW.voucher_id;

    SELECT company_id, uqc_id INTO item_company_id, item_uqc_id
      FROM stock_items WHERE id = NEW.stock_item_id;
    SELECT company_id, stock_item_id INTO variant_company_id, variant_item_id
      FROM stock_item_variants WHERE id = NEW.stock_item_variant_id;
    SELECT company_id INTO uqc_company_id FROM uqcs WHERE id = NEW.uqc_id;
    SELECT company_id INTO godown_company_id FROM godowns WHERE id = NEW.godown_id;

    IF voucher_type_code NOT IN ('PURCHASE', 'OPENING_STOCK') THEN
        RAISE EXCEPTION 'Inventory inward lines require a Purchase or Opening Stock voucher.';
    END IF;
    IF voucher_type_code = 'OPENING_STOCK'
       AND (opening_item_id IS NULL OR opening_item_id <> NEW.stock_item_id) THEN
        RAISE EXCEPTION 'Opening Stock lines must belong to the Stock Item master that owns the opening balance.';
    END IF;
    IF voucher_type_code = 'PURCHASE' AND opening_item_id IS NOT NULL THEN
        RAISE EXCEPTION 'Purchase inward cannot be linked as a Stock Item opening balance.';
    END IF;
    IF item_company_id <> voucher_company_id
       OR variant_company_id <> voucher_company_id
       OR uqc_company_id <> voucher_company_id
       OR godown_company_id <> voucher_company_id THEN
        RAISE EXCEPTION 'Inventory inward line references must belong to the voucher company.';
    END IF;
    IF variant_item_id <> NEW.stock_item_id THEN
        RAISE EXCEPTION 'Inventory inward variant does not belong to the selected Stock Item.';
    END IF;
    IF item_uqc_id <> NEW.uqc_id THEN
        RAISE EXCEPTION 'Inventory inward UQC must match the Stock Item UQC.';
    END IF;
    RETURN NEW;
END $$;
