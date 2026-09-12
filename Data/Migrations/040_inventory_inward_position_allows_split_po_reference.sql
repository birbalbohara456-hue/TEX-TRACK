-- A single supplier invoice can legitimately cover several Purchase Orders.
-- The original uq_inventory_inward_position constraint (031) rejected the same
-- item/variant/UQC/godown combination appearing twice in one Purchase voucher
-- unconditionally - blocking the case where two different Purchase Order lines
-- for the same colour/size are received together in one voucher. Replaced with
-- an expression index that folds purchase_order_line_id into the uniqueness
-- key, using COALESCE(..., -1) so multiple open-market (NULL-reference) lines
-- for the same position still collide exactly as before - only lines tagged
-- to genuinely different Purchase Order lines are now allowed to coexist.

ALTER TABLE inventory_inward_lines
    DROP CONSTRAINT uq_inventory_inward_position;

CREATE UNIQUE INDEX uq_inventory_inward_position
    ON inventory_inward_lines
    (voucher_id, stock_item_id, stock_item_variant_id, uqc_id, godown_id, COALESCE(purchase_order_line_id, -1));
