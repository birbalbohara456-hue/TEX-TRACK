-- Audit hardening: enforce that paired identifiers describe the same immutable object.
-- This migration never rewrites historical vouchers. It stops with a precise exception
-- if existing rows violate an invariant, allowing the operator to inspect them first.

DO $$
BEGIN
    IF EXISTS
    (
        SELECT 1 FROM material_out_lines
        WHERE (bom_stage_id IS NULL) <> (stage_assignment_id IS NULL)
    ) OR EXISTS
    (
        SELECT 1 FROM material_in_finished_goods
        WHERE (bom_stage_id IS NULL) <> (stage_assignment_id IS NULL)
    ) OR EXISTS
    (
        SELECT 1 FROM material_in_consumptions
        WHERE (bom_stage_id IS NULL) <> (stage_assignment_id IS NULL)
    ) THEN
        RAISE EXCEPTION 'Migration 025 blocked: a production line has only one member of its stage/assignment pair.';
    END IF;

    IF EXISTS
    (
        SELECT 1
        FROM bill_of_material_revision_lines line
        JOIN bill_of_material_revisions revision ON revision.id = line.child_bom_revision_id
        WHERE line.child_bom_id IS NOT NULL
          AND revision.bom_id <> line.child_bom_id
    ) THEN
        RAISE EXCEPTION 'Migration 025 blocked: a BOM revision line references a child revision belonging to another BOM.';
    END IF;

    IF EXISTS
    (
        SELECT 1
        FROM material_out_lines line
        JOIN job_work_order_stage_assignments assignment ON assignment.id = line.stage_assignment_id
        WHERE line.bom_stage_id IS NOT NULL
          AND assignment.bom_stage_id <> line.bom_stage_id
    ) THEN
        RAISE EXCEPTION 'Migration 025 blocked: a Material Out line has a stage assignment belonging to another stage.';
    END IF;

    IF EXISTS
    (
        SELECT 1
        FROM material_in_finished_goods line
        JOIN job_work_order_stage_assignments assignment ON assignment.id = line.stage_assignment_id
        WHERE line.bom_stage_id IS NOT NULL
          AND assignment.bom_stage_id <> line.bom_stage_id
    ) THEN
        RAISE EXCEPTION 'Migration 025 blocked: a Material In output has a stage assignment belonging to another stage.';
    END IF;

    IF EXISTS
    (
        SELECT 1
        FROM material_in_consumptions line
        JOIN job_work_order_stage_assignments assignment ON assignment.id = line.stage_assignment_id
        WHERE line.bom_stage_id IS NOT NULL
          AND assignment.bom_stage_id <> line.bom_stage_id
    ) THEN
        RAISE EXCEPTION 'Migration 025 blocked: a Material In consumption has a stage assignment belonging to another stage.';
    END IF;
END $$;

CREATE UNIQUE INDEX IF NOT EXISTS ux_bom_revision_id_bom
    ON bill_of_material_revisions(id, bom_id);
CREATE UNIQUE INDEX IF NOT EXISTS ux_jwo_assignment_id_stage
    ON job_work_order_stage_assignments(id, bom_stage_id);

ALTER TABLE bill_of_material_revision_lines
    ADD CONSTRAINT fk_bom_revision_line_child_revision_pair
    FOREIGN KEY (child_bom_revision_id, child_bom_id)
    REFERENCES bill_of_material_revisions(id, bom_id)
    ON DELETE RESTRICT;

ALTER TABLE material_out_lines
    ADD CONSTRAINT ck_material_out_stage_assignment_pair
    CHECK ((bom_stage_id IS NULL) = (stage_assignment_id IS NULL)),
    ADD CONSTRAINT fk_material_out_stage_assignment_pair
    FOREIGN KEY (stage_assignment_id, bom_stage_id)
    REFERENCES job_work_order_stage_assignments(id, bom_stage_id)
    ON DELETE RESTRICT;

ALTER TABLE material_in_finished_goods
    ADD CONSTRAINT ck_material_in_fg_stage_assignment_pair
    CHECK ((bom_stage_id IS NULL) = (stage_assignment_id IS NULL)),
    ADD CONSTRAINT fk_material_in_fg_stage_assignment_pair
    FOREIGN KEY (stage_assignment_id, bom_stage_id)
    REFERENCES job_work_order_stage_assignments(id, bom_stage_id)
    ON DELETE RESTRICT;

ALTER TABLE material_in_consumptions
    ADD CONSTRAINT ck_material_in_consumption_stage_assignment_pair
    CHECK ((bom_stage_id IS NULL) = (stage_assignment_id IS NULL)),
    ADD CONSTRAINT fk_material_in_consumption_stage_assignment_pair
    FOREIGN KEY (stage_assignment_id, bom_stage_id)
    REFERENCES job_work_order_stage_assignments(id, bom_stage_id)
    ON DELETE RESTRICT;
