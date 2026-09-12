-- Used Colour/Size values are historical stock identity. Their identity fields
-- are immutable after operational use, but is_active remains editable so they
-- can be hidden from new entry without rewriting old vouchers or reports.

CREATE OR REPLACE FUNCTION textrack_stock_item_variant_is_used(p_variant_id bigint)
RETURNS boolean
LANGUAGE sql
STABLE
AS $$
    SELECT
        EXISTS (SELECT 1 FROM job_work_order_size_allocations WHERE stock_item_variant_id = p_variant_id)
        OR EXISTS (SELECT 1 FROM master_job_order_allocations WHERE stock_item_variant_id = p_variant_id)
        OR EXISTS (SELECT 1 FROM job_work_order_components WHERE component_variant_id = p_variant_id)
        OR EXISTS (SELECT 1 FROM job_work_order_bom_stages WHERE output_variant_id = p_variant_id)
        OR EXISTS (SELECT 1 FROM material_in_fg_allocations WHERE stock_item_variant_id = p_variant_id)
        OR EXISTS (SELECT 1 FROM inventory_inward_lines WHERE stock_item_variant_id = p_variant_id)
        OR EXISTS (SELECT 1 FROM stock_movements WHERE stock_item_variant_id = p_variant_id)
        OR EXISTS (SELECT 1 FROM bill_of_material_lines WHERE component_variant_id = p_variant_id)
        OR EXISTS (SELECT 1 FROM bill_of_material_revision_lines WHERE component_variant_id = p_variant_id);
$$;

CREATE OR REPLACE FUNCTION textrack_colour_is_used(p_colour_id bigint)
RETURNS boolean
LANGUAGE sql
STABLE
AS $$
    SELECT
        EXISTS (SELECT 1 FROM job_work_order_finished_goods WHERE colour_id = p_colour_id)
        OR EXISTS (SELECT 1 FROM master_job_order_allocations WHERE colour_id = p_colour_id)
        OR EXISTS
        (
            SELECT 1
            FROM stock_item_variants variant
            WHERE variant.colour_id = p_colour_id
              AND textrack_stock_item_variant_is_used(variant.id)
        );
$$;

CREATE OR REPLACE FUNCTION textrack_size_is_used(p_size_id bigint)
RETURNS boolean
LANGUAGE sql
STABLE
AS $$
    SELECT
        EXISTS (SELECT 1 FROM job_work_order_size_allocations WHERE size_id = p_size_id)
        OR EXISTS (SELECT 1 FROM master_job_order_allocations WHERE size_id = p_size_id)
        OR EXISTS
        (
            SELECT 1
            FROM stock_item_variants variant
            WHERE variant.size_id = p_size_id
              AND textrack_stock_item_variant_is_used(variant.id)
        );
$$;

CREATE OR REPLACE FUNCTION textrack_guard_used_variant_attribute()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF TG_TABLE_NAME = 'colours'
       AND (NEW.name IS DISTINCT FROM OLD.name
            OR NEW.name_normalized IS DISTINCT FROM OLD.name_normalized
            OR NEW.colour_code IS DISTINCT FROM OLD.colour_code)
       AND textrack_colour_is_used(OLD.id)
    THEN
        RAISE EXCEPTION
            'Used Colour identity is immutable. Deactivate it for new vouchers or create a new Colour.';
    END IF;

    IF TG_TABLE_NAME = 'sizes'
       AND (NEW.name IS DISTINCT FROM OLD.name
            OR NEW.name_normalized IS DISTINCT FROM OLD.name_normalized)
       AND textrack_size_is_used(OLD.id)
    THEN
        RAISE EXCEPTION
            'Used Size identity is immutable. Deactivate it for new vouchers or create a new Size.';
    END IF;

    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_colour_used_identity_immutable ON colours;
CREATE TRIGGER trg_colour_used_identity_immutable
BEFORE UPDATE ON colours
FOR EACH ROW
EXECUTE FUNCTION textrack_guard_used_variant_attribute();

DROP TRIGGER IF EXISTS trg_size_used_identity_immutable ON sizes;
CREATE TRIGGER trg_size_used_identity_immutable
BEFORE UPDATE ON sizes
FOR EACH ROW
EXECUTE FUNCTION textrack_guard_used_variant_attribute();

CREATE OR REPLACE FUNCTION textrack_guard_used_stock_item_variant()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF TG_OP = 'DELETE' AND textrack_stock_item_variant_is_used(OLD.id)
    THEN
        RAISE EXCEPTION
            'Used Stock Item Variant identity is immutable. Deactivate it for new entry instead.';
    END IF;

    IF TG_OP = 'DELETE' THEN
        RETURN OLD;
    END IF;

    IF textrack_stock_item_variant_is_used(OLD.id)
       AND
       (NEW.stock_item_id IS DISTINCT FROM OLD.stock_item_id
        OR NEW.colour_id IS DISTINCT FROM OLD.colour_id
        OR NEW.size_id IS DISTINCT FROM OLD.size_id
        OR NEW.variant_key IS DISTINCT FROM OLD.variant_key)
    THEN
        RAISE EXCEPTION
            'Used Stock Item Variant identity is immutable. Deactivate it for new entry instead.';
    END IF;

    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_stock_item_variant_used_identity_immutable ON stock_item_variants;
CREATE TRIGGER trg_stock_item_variant_used_identity_immutable
BEFORE UPDATE OR DELETE ON stock_item_variants
FOR EACH ROW
EXECUTE FUNCTION textrack_guard_used_stock_item_variant();
