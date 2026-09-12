-- Purchase Order foundation: a pure planning/reference voucher with zero inventory
-- or financial posting. Mirrors Job Work Out Order's role (Nature 'Planning',
-- PostingMode 'No financial posting', already seeded in 001_initial_foundation.sql) -
-- no voucher_types changes needed here.

CREATE TABLE purchase_order_lines
(
    id bigserial PRIMARY KEY,
    voucher_id bigint NOT NULL REFERENCES vouchers(id) ON DELETE CASCADE,
    line_number integer NOT NULL,
    stock_item_id bigint NOT NULL REFERENCES stock_items(id) ON DELETE RESTRICT,
    stock_item_variant_id bigint NOT NULL REFERENCES stock_item_variants(id) ON DELETE RESTRICT,
    uqc_id bigint NOT NULL REFERENCES uqcs(id) ON DELETE RESTRICT,
    godown_id bigint NULL REFERENCES godowns(id) ON DELETE RESTRICT,
    ordered_quantity numeric(19,4) NOT NULL,
    rate numeric(19,4) NOT NULL,
    amount numeric(19,4) NOT NULL,
    expected_delivery_date date NULL,
    CONSTRAINT ck_purchase_order_line_number CHECK (line_number > 0),
    CONSTRAINT ck_purchase_order_ordered_quantity CHECK (ordered_quantity > 0),
    CONSTRAINT ck_purchase_order_rate CHECK (rate >= 0),
    CONSTRAINT ck_purchase_order_amount CHECK (amount >= 0),
    CONSTRAINT ck_purchase_order_amount_math CHECK (amount = round(ordered_quantity * rate, 4)),
    CONSTRAINT uq_purchase_order_line_number UNIQUE (voucher_id, line_number),
    CONSTRAINT uq_purchase_order_position UNIQUE
        (voucher_id, stock_item_id, stock_item_variant_id, uqc_id, godown_id)
);

CREATE INDEX ix_purchase_order_item_position
    ON purchase_order_lines(stock_item_id, stock_item_variant_id, uqc_id, voucher_id);

CREATE OR REPLACE FUNCTION validate_purchase_order_line_integrity()
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

    IF voucher_type_code <> 'PURCHASE_ORDER' THEN
        RAISE EXCEPTION 'Purchase Order lines require a Purchase Order voucher.';
    END IF;
    IF item_company_id <> voucher_company_id
       OR variant_company_id <> voucher_company_id
       OR uqc_company_id <> voucher_company_id THEN
        RAISE EXCEPTION 'Purchase Order line references must belong to the voucher company.';
    END IF;
    IF variant_item_id <> NEW.stock_item_id THEN
        RAISE EXCEPTION 'Purchase Order variant does not belong to the selected Stock Item.';
    END IF;
    IF item_uqc_id <> NEW.uqc_id THEN
        RAISE EXCEPTION 'Purchase Order UQC must match the Stock Item UQC.';
    END IF;
    IF NEW.godown_id IS NOT NULL THEN
        SELECT company_id INTO godown_company_id FROM godowns WHERE id = NEW.godown_id;
        IF godown_company_id <> voucher_company_id THEN
            RAISE EXCEPTION 'Purchase Order expected godown must belong to the voucher company.';
        END IF;
    END IF;
    RETURN NEW;
END $$;

CREATE TRIGGER trg_purchase_order_line_integrity
BEFORE INSERT OR UPDATE ON purchase_order_lines
FOR EACH ROW EXECUTE FUNCTION validate_purchase_order_line_integrity();
