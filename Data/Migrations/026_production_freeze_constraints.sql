-- Production-freeze invariants. This migration never rewrites historical rows.
-- It aborts before adding constraints when existing data needs operator review.

DO $$
BEGIN
    IF EXISTS
    (
        SELECT 1
        FROM stock_movements
        WHERE movement_kind NOT IN
        (
            'MaterialOutSource',
            'MaterialOutDestination',
            'MaterialOutCancellationSource',
            'MaterialOutCancellationDestination',
            'MaterialInConsumption',
            'MaterialInCancellationConsumption',
            'MaterialInFinishedGoods',
            'MaterialInCancellationFinishedGoods'
        )
    ) THEN
        RAISE EXCEPTION 'Migration 026 blocked: stock_movements contains an unknown movement_kind.';
    END IF;

    IF EXISTS
    (
        SELECT 1
        FROM bill_of_materials bom
        JOIN bill_of_material_revisions revision ON revision.id = bom.current_revision_id
        WHERE revision.bom_id <> bom.id
    ) THEN
        RAISE EXCEPTION 'Migration 026 blocked: a BOM current revision belongs to another BOM.';
    END IF;
END $$;

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
            'MaterialInCancellationFinishedGoods'
        )
    );

ALTER TABLE bill_of_materials
    DROP CONSTRAINT IF EXISTS fk_bill_of_material_current_revision,
    ADD CONSTRAINT fk_bill_of_material_current_revision_pair
        FOREIGN KEY (current_revision_id, id)
        REFERENCES bill_of_material_revisions(id, bom_id)
        ON DELETE RESTRICT;
