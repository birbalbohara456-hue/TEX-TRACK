-- Preserve component colour/size identity through JWO -> MO -> MI stock movements.
-- Historical null component variants are filled only when the Stock Item has exactly one
-- possible variant. Multi-variant nulls remain untouched because their identity is ambiguous.

UPDATE job_work_order_components component
SET component_variant_id = candidate.variant_id
FROM
(
    SELECT stock_item_id, MIN(id) AS variant_id
    FROM stock_item_variants
    GROUP BY stock_item_id
    HAVING COUNT(*) = 1
) candidate
WHERE component.component_variant_id IS NULL
  AND candidate.stock_item_id = component.stock_item_id;

-- Abort instead of silently rewriting an inconsistent historical link.
DO $$
BEGIN
    IF EXISTS
    (
        SELECT 1
        FROM stock_movements movement
        JOIN material_out_lines line ON line.id = movement.material_out_line_id
        JOIN job_work_order_components component ON component.id = line.jwo_component_id
        JOIN stock_item_variants variant ON variant.id = component.component_variant_id
        WHERE component.component_variant_id IS NOT NULL
          AND
          (
              line.stock_item_id <> component.stock_item_id
              OR movement.stock_item_id <> line.stock_item_id
              OR variant.stock_item_id <> component.stock_item_id
              OR
              (
                  movement.stock_item_variant_id IS NOT NULL
                  AND movement.stock_item_variant_id <> component.component_variant_id
              )
          )
    ) THEN
        RAISE EXCEPTION
            'Migration 030 blocked: a Material Out stock movement has an inconsistent component/variant identity.';
    END IF;

    IF EXISTS
    (
        SELECT 1
        FROM stock_movements movement
        JOIN material_in_consumptions consumption ON consumption.id = movement.material_in_consumption_id
        JOIN job_work_order_components component ON component.id = consumption.jwo_component_id
        JOIN stock_item_variants variant ON variant.id = component.component_variant_id
        WHERE component.component_variant_id IS NOT NULL
          AND
          (
              consumption.stock_item_id <> component.stock_item_id
              OR movement.stock_item_id <> consumption.stock_item_id
              OR variant.stock_item_id <> component.stock_item_id
              OR
              (
                  movement.stock_item_variant_id IS NOT NULL
                  AND movement.stock_item_variant_id <> component.component_variant_id
              )
          )
    ) THEN
        RAISE EXCEPTION
            'Migration 030 blocked: a Material In consumption movement has an inconsistent component/variant identity.';
    END IF;
END $$;

UPDATE stock_movements movement
SET stock_item_variant_id = component.component_variant_id
FROM material_out_lines line
JOIN job_work_order_components component ON component.id = line.jwo_component_id
WHERE movement.material_out_line_id = line.id
  AND movement.stock_item_variant_id IS NULL
  AND component.component_variant_id IS NOT NULL;

UPDATE stock_movements movement
SET stock_item_variant_id = component.component_variant_id
FROM material_in_consumptions consumption
JOIN job_work_order_components component ON component.id = consumption.jwo_component_id
WHERE movement.material_in_consumption_id = consumption.id
  AND movement.stock_item_variant_id IS NULL
  AND component.component_variant_id IS NOT NULL;

CREATE INDEX ix_stock_movements_exact_position
    ON stock_movements
       (company_id, stock_item_id, stock_item_variant_id, uqc_id, godown_id, movement_date, id);

