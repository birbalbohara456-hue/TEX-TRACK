-- Optional per-line linkage from a Purchase (inventory_inward_lines, Purchase kind)
-- back to the Purchase Order line it is fulfilling. Nullable: an open-market
-- Purchase line with no Purchase Order reference is equally valid. This link is
-- deliberately re-editable on every alteration (see InventoryInwardRepository) -
-- unlike Material In/Out's JwoVoucherId lock - so no immutability trigger is added
-- here beyond the existing item/variant/UQC integrity check, extended below to also
-- validate consistency against the referenced Purchase Order line when present.

ALTER TABLE inventory_inward_lines
    ADD COLUMN purchase_order_line_id bigint NULL
        REFERENCES purchase_order_lines(id) ON DELETE RESTRICT;

CREATE INDEX ix_inventory_inward_purchase_order_line
    ON inventory_inward_lines(purchase_order_line_id);

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
    po_item_id bigint;
    po_variant_id bigint;
    po_uqc_id bigint;
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

    IF NEW.purchase_order_line_id IS NOT NULL THEN
        IF voucher_type_code <> 'PURCHASE' THEN
            RAISE EXCEPTION 'Only Purchase lines may reference a Purchase Order line.';
        END IF;
        SELECT stock_item_id, stock_item_variant_id, uqc_id
          INTO po_item_id, po_variant_id, po_uqc_id
          FROM purchase_order_lines WHERE id = NEW.purchase_order_line_id;
        IF NOT FOUND THEN
            RAISE EXCEPTION 'Referenced Purchase Order line does not exist.';
        END IF;
        IF po_item_id <> NEW.stock_item_id
           OR po_variant_id <> NEW.stock_item_variant_id
           OR po_uqc_id <> NEW.uqc_id THEN
            RAISE EXCEPTION 'Purchase line item/variant/UQC must match the referenced Purchase Order line.';
        END IF;
    END IF;
    RETURN NEW;
END $$;
