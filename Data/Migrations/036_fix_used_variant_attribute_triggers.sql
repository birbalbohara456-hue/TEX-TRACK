-- Migration 035 attached one generic trigger function to both colours and sizes.
-- PostgreSQL record fields are table-specific, so the colour_code reference in
-- that function is invalid when PostgreSQL evaluates it for a sizes row.
-- Keep migration 035 immutable and replace its triggers forward-only with
-- table-specific functions. No business or historical data is changed.

CREATE OR REPLACE FUNCTION textrack_guard_used_colour_identity()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF (NEW.name IS DISTINCT FROM OLD.name
        OR NEW.name_normalized IS DISTINCT FROM OLD.name_normalized
        OR NEW.colour_code IS DISTINCT FROM OLD.colour_code)
       AND textrack_colour_is_used(OLD.id)
    THEN
        RAISE EXCEPTION
            'Used Colour identity is immutable. Deactivate it for new vouchers or create a new Colour.';
    END IF;

    RETURN NEW;
END;
$$;

CREATE OR REPLACE FUNCTION textrack_guard_used_size_identity()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF (NEW.name IS DISTINCT FROM OLD.name
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
EXECUTE FUNCTION textrack_guard_used_colour_identity();

DROP TRIGGER IF EXISTS trg_size_used_identity_immutable ON sizes;
CREATE TRIGGER trg_size_used_identity_immutable
BEFORE UPDATE ON sizes
FOR EACH ROW
EXECUTE FUNCTION textrack_guard_used_size_identity();

DROP FUNCTION IF EXISTS textrack_guard_used_variant_attribute();
