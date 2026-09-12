-- Native inventory-inward foundation for Purchase and Opening Stock.
-- Purchase currently posts inventory quantity/value only; accounting/tax/payable
-- entries remain outside this migration and must not be inferred from these rows.

-- RESET-SEED: native inward voucher types

DO $$
BEGIN
    IF EXISTS
    (
        SELECT 1
        FROM companies c
        JOIN voucher_types vt
          ON vt.company_id = c.id
         AND vt.name_normalized = 'OPENING STOCK'
         AND vt.system_type_code <> 'OPENING_STOCK'
    ) THEN
        RAISE EXCEPTION
            'Migration 031 blocked: rename the existing non-system Voucher Type named Opening Stock before retrying.';
    END IF;
END $$;

INSERT INTO voucher_types
(
    company_id, name, name_normalized, alias, system_type_code, nature,
    posting_mode, abbreviation, allow_manual_numbering, numbering_mode,
    prefix, suffix, starting_number, number_width, reset_period,
    tally_voucher_type_name, parent_voucher_type_id, is_system, is_active,
    created_at_utc, modified_at_utc, created_by, modified_by, concurrency_token
)
SELECT
    c.id, 'Opening Stock', 'OPENING STOCK', '', 'OPENING_STOCK', 'Inventory',
    'Inventory Inward', 'OPN', false, 'Auto',
    '', '', 1, 0, 'FinancialYear',
    'Stock Journal', NULL, true, true,
    NOW(), NOW(), 'Migration031', 'Migration031', md5(random()::text)
FROM companies c
WHERE NOT EXISTS
(
    SELECT 1 FROM voucher_types vt
    WHERE vt.company_id = c.id AND vt.system_type_code = 'OPENING_STOCK'
);

UPDATE voucher_types
SET posting_mode = 'Inventory Inward',
    modified_at_utc = NOW(),
    modified_by = 'Migration031',
    concurrency_token = md5(random()::text)
WHERE system_type_code = 'PURCHASE'
  AND posting_mode = 'Enabled later';

-- RESET-SEED-END

CREATE TABLE inventory_inward_lines
(
    id bigserial PRIMARY KEY,
    voucher_id bigint NOT NULL REFERENCES vouchers(id) ON DELETE CASCADE,
    line_number integer NOT NULL,
    stock_item_id bigint NOT NULL REFERENCES stock_items(id) ON DELETE RESTRICT,
    stock_item_variant_id bigint NOT NULL REFERENCES stock_item_variants(id) ON DELETE RESTRICT,
    uqc_id bigint NOT NULL REFERENCES uqcs(id) ON DELETE RESTRICT,
    godown_id bigint NOT NULL REFERENCES godowns(id) ON DELETE RESTRICT,
    quantity numeric(19,4) NOT NULL,
    rate numeric(19,4) NOT NULL,
    amount numeric(19,4) NOT NULL,
    CONSTRAINT ck_inventory_inward_line_number CHECK (line_number > 0),
    CONSTRAINT ck_inventory_inward_quantity CHECK (quantity > 0),
    CONSTRAINT ck_inventory_inward_rate CHECK (rate >= 0),
    CONSTRAINT ck_inventory_inward_amount CHECK (amount >= 0),
    CONSTRAINT ck_inventory_inward_amount_math CHECK (amount = round(quantity * rate, 4)),
    CONSTRAINT uq_inventory_inward_line_number UNIQUE (voucher_id, line_number),
    CONSTRAINT uq_inventory_inward_position UNIQUE
        (voucher_id, stock_item_id, stock_item_variant_id, uqc_id, godown_id)
);

CREATE INDEX ix_inventory_inward_item_position
    ON inventory_inward_lines(stock_item_id, stock_item_variant_id, uqc_id, godown_id, voucher_id);

ALTER TABLE stock_movements
    ADD COLUMN inventory_inward_line_id bigint NULL
        REFERENCES inventory_inward_lines(id) ON DELETE CASCADE;

ALTER TABLE stock_movements
    DROP CONSTRAINT IF EXISTS ck_stock_movement_kind,
    ADD CONSTRAINT ck_stock_movement_kind CHECK
    (
        movement_kind IN
        (
            'MaterialOutSource',
            'MaterialOutDestination',
            'MaterialOutCancellationSource',
            'MaterialOutCancellationDestination',
            'MaterialInConsumption',
            'MaterialInCancellationConsumption',
            'MaterialInFinishedGoods',
            'MaterialInCancellationFinishedGoods',
            'PurchaseInward',
            'PurchaseInwardCancellation',
            'OpeningStockInward',
            'OpeningStockInwardCancellation'
        )
    );

CREATE INDEX ix_stock_movements_inventory_inward_line
    ON stock_movements(voucher_id, inventory_inward_line_id);

CREATE OR REPLACE FUNCTION validate_inventory_inward_line_integrity()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    voucher_company_id bigint;
    voucher_type_code varchar(80);
    item_company_id bigint;
    item_uqc_id bigint;
    variant_company_id bigint;
    variant_item_id bigint;
    uqc_company_id bigint;
    godown_company_id bigint;
BEGIN
    SELECT v.company_id,
           CASE WHEN vt.parent_voucher_type_id IS NOT NULL
                THEN parent.system_type_code ELSE vt.system_type_code END
      INTO voucher_company_id, voucher_type_code
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

CREATE TRIGGER trg_inventory_inward_line_integrity
BEFORE INSERT OR UPDATE ON inventory_inward_lines
FOR EACH ROW EXECUTE FUNCTION validate_inventory_inward_line_integrity();

CREATE OR REPLACE FUNCTION validate_inventory_inward_movement_integrity()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    inward inventory_inward_lines%ROWTYPE;
    inward_company_id bigint;
    inward_financial_year_id bigint;
    inward_date date;
BEGIN
    IF NEW.inventory_inward_line_id IS NULL THEN
        RETURN NEW;
    END IF;

    SELECT * INTO inward
      FROM inventory_inward_lines
     WHERE id = NEW.inventory_inward_line_id;
    SELECT company_id, financial_year_id, voucher_date
      INTO inward_company_id, inward_financial_year_id, inward_date
      FROM vouchers
     WHERE id = inward.voucher_id;

    IF NOT FOUND
       OR NEW.voucher_id <> inward.voucher_id
       OR NEW.company_id <> inward_company_id
       OR NEW.financial_year_id <> inward_financial_year_id
       OR NEW.movement_date <> inward_date
       OR NEW.stock_item_id <> inward.stock_item_id
       OR NEW.stock_item_variant_id IS DISTINCT FROM inward.stock_item_variant_id
       OR NEW.uqc_id <> inward.uqc_id
       OR NEW.godown_id <> inward.godown_id
       OR NEW.rate <> inward.rate
       OR abs(NEW.quantity_change) <> inward.quantity
       OR abs(NEW.value_change) <> inward.amount
       OR NEW.movement_kind NOT IN
          ('PurchaseInward', 'PurchaseInwardCancellation',
           'OpeningStockInward', 'OpeningStockInwardCancellation') THEN
        RAISE EXCEPTION 'Inventory inward stock movement does not match its source line.';
    END IF;
    IF (NEW.movement_kind IN ('PurchaseInward', 'OpeningStockInward')
        AND (NEW.quantity_change <= 0 OR NEW.value_change < 0))
       OR (NEW.movement_kind IN ('PurchaseInwardCancellation', 'OpeningStockInwardCancellation')
        AND (NEW.quantity_change >= 0 OR NEW.value_change > 0)) THEN
        RAISE EXCEPTION 'Inventory inward stock movement has the wrong posting direction.';
    END IF;
    RETURN NEW;
END $$;

CREATE TRIGGER trg_inventory_inward_movement_integrity
BEFORE INSERT OR UPDATE ON stock_movements
FOR EACH ROW EXECUTE FUNCTION validate_inventory_inward_movement_integrity();
