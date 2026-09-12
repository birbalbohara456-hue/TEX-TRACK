-- Purchase Return foundation: a standalone outward-posting voucher. It does not
-- link back to any specific original Purchase (no FK, no quantity cap) - an
-- optional bill-number reference uses the existing free-text Voucher.ReferenceNumber
-- field instead. Mirrors PURCHASE's own native inward foundation (031), signs flipped.

UPDATE voucher_types
SET posting_mode = 'Inventory Outward',
    modified_at_utc = NOW(),
    modified_by = 'Migration039',
    concurrency_token = md5(random()::text)
WHERE system_type_code = 'PURCHASE_RETURN'
  AND posting_mode = 'Enabled later';

CREATE TABLE purchase_return_lines
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
    CONSTRAINT ck_purchase_return_line_number CHECK (line_number > 0),
    CONSTRAINT ck_purchase_return_quantity CHECK (quantity > 0),
    CONSTRAINT ck_purchase_return_rate CHECK (rate >= 0),
    CONSTRAINT ck_purchase_return_amount CHECK (amount >= 0),
    CONSTRAINT ck_purchase_return_amount_math CHECK (amount = round(quantity * rate, 4)),
    CONSTRAINT uq_purchase_return_line_number UNIQUE (voucher_id, line_number),
    CONSTRAINT uq_purchase_return_position UNIQUE
        (voucher_id, stock_item_id, stock_item_variant_id, uqc_id, godown_id)
);

CREATE INDEX ix_purchase_return_item_position
    ON purchase_return_lines(stock_item_id, stock_item_variant_id, uqc_id, godown_id, voucher_id);

ALTER TABLE stock_movements
    ADD COLUMN purchase_return_line_id bigint NULL
        REFERENCES purchase_return_lines(id) ON DELETE CASCADE;

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
            'OpeningStockInwardCancellation',
            'PurchaseReturnOutward',
            'PurchaseReturnOutwardCancellation'
        )
    );

CREATE INDEX ix_stock_movements_purchase_return_line
    ON stock_movements(voucher_id, purchase_return_line_id);

CREATE OR REPLACE FUNCTION validate_purchase_return_line_integrity()
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

    IF voucher_type_code <> 'PURCHASE_RETURN' THEN
        RAISE EXCEPTION 'Purchase Return lines require a Purchase Return voucher.';
    END IF;
    IF item_company_id <> voucher_company_id
       OR variant_company_id <> voucher_company_id
       OR uqc_company_id <> voucher_company_id
       OR godown_company_id <> voucher_company_id THEN
        RAISE EXCEPTION 'Purchase Return line references must belong to the voucher company.';
    END IF;
    IF variant_item_id <> NEW.stock_item_id THEN
        RAISE EXCEPTION 'Purchase Return variant does not belong to the selected Stock Item.';
    END IF;
    IF item_uqc_id <> NEW.uqc_id THEN
        RAISE EXCEPTION 'Purchase Return UQC must match the Stock Item UQC.';
    END IF;
    RETURN NEW;
END $$;

CREATE TRIGGER trg_purchase_return_line_integrity
BEFORE INSERT OR UPDATE ON purchase_return_lines
FOR EACH ROW EXECUTE FUNCTION validate_purchase_return_line_integrity();

CREATE OR REPLACE FUNCTION validate_purchase_return_movement_integrity()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    outward purchase_return_lines%ROWTYPE;
    outward_company_id bigint;
    outward_financial_year_id bigint;
    outward_date date;
BEGIN
    IF NEW.purchase_return_line_id IS NULL THEN
        RETURN NEW;
    END IF;

    SELECT * INTO outward
      FROM purchase_return_lines
     WHERE id = NEW.purchase_return_line_id;
    SELECT company_id, financial_year_id, voucher_date
      INTO outward_company_id, outward_financial_year_id, outward_date
      FROM vouchers
     WHERE id = outward.voucher_id;

    IF NOT FOUND
       OR NEW.voucher_id <> outward.voucher_id
       OR NEW.company_id <> outward_company_id
       OR NEW.financial_year_id <> outward_financial_year_id
       OR NEW.movement_date <> outward_date
       OR NEW.stock_item_id <> outward.stock_item_id
       OR NEW.stock_item_variant_id IS DISTINCT FROM outward.stock_item_variant_id
       OR NEW.uqc_id <> outward.uqc_id
       OR NEW.godown_id <> outward.godown_id
       OR NEW.rate <> outward.rate
       OR abs(NEW.quantity_change) <> outward.quantity
       OR abs(NEW.value_change) <> outward.amount
       OR NEW.movement_kind NOT IN
          ('PurchaseReturnOutward', 'PurchaseReturnOutwardCancellation') THEN
        RAISE EXCEPTION 'Purchase Return stock movement does not match its source line.';
    END IF;
    IF (NEW.movement_kind = 'PurchaseReturnOutward'
        AND (NEW.quantity_change >= 0 OR NEW.value_change > 0))
       OR (NEW.movement_kind = 'PurchaseReturnOutwardCancellation'
        AND (NEW.quantity_change <= 0 OR NEW.value_change < 0)) THEN
        RAISE EXCEPTION 'Purchase Return stock movement has the wrong posting direction.';
    END IF;
    RETURN NEW;
END $$;

CREATE TRIGGER trg_purchase_return_movement_integrity
BEFORE INSERT OR UPDATE ON stock_movements
FOR EACH ROW EXECUTE FUNCTION validate_purchase_return_movement_integrity();
