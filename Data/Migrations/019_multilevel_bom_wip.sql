-- Build 3.1: optional multi-level BOM snapshots under one JWO.
-- WIP remains transaction-derived; this migration never moves a Stock Item to the WIP Stock Group.

CREATE TABLE bill_of_materials
(
    id bigserial PRIMARY KEY,
    company_id bigint NOT NULL REFERENCES companies(id) ON DELETE RESTRICT,
    stock_item_id bigint NOT NULL REFERENCES stock_items(id) ON DELETE RESTRICT,
    name varchar(200) NOT NULL,
    name_normalized varchar(200) NOT NULL,
    output_quantity numeric(19,4) NOT NULL DEFAULT 1,
    version_number integer NOT NULL DEFAULT 1,
    is_default boolean NOT NULL DEFAULT false,
    is_active boolean NOT NULL DEFAULT true,
    created_at_utc timestamptz NOT NULL,
    modified_at_utc timestamptz NOT NULL,
    created_by varchar(100) NOT NULL,
    modified_by varchar(100) NOT NULL,
    concurrency_token varchar(64) NOT NULL,
    CONSTRAINT ck_bom_output_quantity_positive CHECK (output_quantity > 0),
    CONSTRAINT ck_bom_version_positive CHECK (version_number > 0),
    CONSTRAINT uq_bom_company_item_name UNIQUE (company_id, stock_item_id, name_normalized)
);
CREATE INDEX ix_bom_company_item_active ON bill_of_materials(company_id, stock_item_id, is_active);
CREATE UNIQUE INDEX uq_bom_default_per_item ON bill_of_materials(company_id, stock_item_id)
    WHERE is_default AND is_active;

CREATE TABLE bill_of_material_lines
(
    id bigserial PRIMARY KEY,
    bom_id bigint NOT NULL REFERENCES bill_of_materials(id) ON DELETE CASCADE,
    line_number integer NOT NULL,
    component_stock_item_id bigint NOT NULL REFERENCES stock_items(id) ON DELETE RESTRICT,
    component_variant_id bigint NULL REFERENCES stock_item_variants(id) ON DELETE RESTRICT,
    uqc_id bigint NOT NULL REFERENCES uqcs(id) ON DELETE RESTRICT,
    required_quantity numeric(19,4) NOT NULL,
    child_bom_id bigint NULL REFERENCES bill_of_materials(id) ON DELETE RESTRICT,
    process_id bigint NULL REFERENCES processes(id) ON DELETE RESTRICT,
    notes varchar(500) NOT NULL DEFAULT '',
    CONSTRAINT ck_bom_line_number_positive CHECK (line_number > 0),
    CONSTRAINT ck_bom_line_quantity_positive CHECK (required_quantity > 0),
    CONSTRAINT uq_bom_line_number UNIQUE (bom_id, line_number)
);
CREATE INDEX ix_bom_line_component ON bill_of_material_lines(component_stock_item_id, bom_id);
CREATE INDEX ix_bom_line_child ON bill_of_material_lines(child_bom_id) WHERE child_bom_id IS NOT NULL;

-- A stage is a frozen production node copied from the selected BOM when the JWO is saved.
-- Root stages produce final goods; child stages produce intermediate components.
CREATE TABLE job_work_order_bom_stages
(
    id bigserial PRIMARY KEY,
    voucher_id bigint NOT NULL REFERENCES vouchers(id) ON DELETE CASCADE,
    finished_good_id bigint NOT NULL REFERENCES job_work_order_finished_goods(id) ON DELETE CASCADE,
    parent_stage_id bigint NULL REFERENCES job_work_order_bom_stages(id) ON DELETE CASCADE,
    source_bom_id bigint NULL REFERENCES bill_of_materials(id) ON DELETE RESTRICT,
    source_bom_version integer NULL,
    stage_number integer NOT NULL,
    stage_level integer NOT NULL DEFAULT 0,
    stage_path varchar(500) NOT NULL,
    stage_name varchar(200) NOT NULL,
    output_stock_item_id bigint NOT NULL REFERENCES stock_items(id) ON DELETE RESTRICT,
    output_variant_id bigint NULL REFERENCES stock_item_variants(id) ON DELETE RESTRICT,
    output_uqc_id bigint NOT NULL REFERENCES uqcs(id) ON DELETE RESTRICT,
    output_quantity numeric(19,4) NOT NULL,
    process_id bigint NULL REFERENCES processes(id) ON DELETE RESTRICT,
    assigned_job_worker_id bigint NULL REFERENCES ledgers(id) ON DELETE RESTRICT,
    output_godown_id bigint NULL REFERENCES godowns(id) ON DELETE RESTRICT,
    is_final_stage boolean NOT NULL DEFAULT false,
    CONSTRAINT ck_jwo_bom_stage_number_positive CHECK (stage_number > 0),
    CONSTRAINT ck_jwo_bom_stage_level_nonnegative CHECK (stage_level >= 0),
    CONSTRAINT ck_jwo_bom_stage_quantity_positive CHECK (output_quantity > 0),
    CONSTRAINT uq_jwo_bom_stage_path UNIQUE (finished_good_id, stage_path)
);
CREATE INDEX ix_jwo_bom_stage_voucher ON job_work_order_bom_stages(voucher_id, stage_number);
CREATE INDEX ix_jwo_bom_stage_worker ON job_work_order_bom_stages(assigned_job_worker_id, voucher_id)
    WHERE assigned_job_worker_id IS NOT NULL;

ALTER TABLE job_work_order_components
    ADD COLUMN bom_stage_id bigint NULL REFERENCES job_work_order_bom_stages(id) ON DELETE CASCADE,
    ADD COLUMN parent_component_id bigint NULL REFERENCES job_work_order_components(id) ON DELETE CASCADE,
    ADD COLUMN child_bom_stage_id bigint NULL REFERENCES job_work_order_bom_stages(id) ON DELETE RESTRICT,
    ADD COLUMN component_variant_id bigint NULL REFERENCES stock_item_variants(id) ON DELETE RESTRICT,
    ADD COLUMN bom_level integer NOT NULL DEFAULT 0,
    ADD COLUMN bom_path varchar(500) NOT NULL DEFAULT '',
    ADD COLUMN is_produced_component boolean NOT NULL DEFAULT false;

ALTER TABLE job_work_order_components DROP CONSTRAINT IF EXISTS uq_jwo_component_item;
CREATE UNIQUE INDEX uq_jwo_component_stage_line
    ON job_work_order_components(COALESCE(bom_stage_id, 0), finished_good_id, line_number);
CREATE INDEX ix_jwo_component_parent ON job_work_order_components(parent_component_id)
    WHERE parent_component_id IS NOT NULL;
CREATE INDEX ix_jwo_component_child_stage ON job_work_order_components(child_bom_stage_id)
    WHERE child_bom_stage_id IS NOT NULL;

ALTER TABLE material_in_finished_goods
    ADD COLUMN bom_stage_id bigint NULL REFERENCES job_work_order_bom_stages(id) ON DELETE RESTRICT;
CREATE INDEX ix_material_in_finished_goods_bom_stage ON material_in_finished_goods(bom_stage_id, voucher_id)
    WHERE bom_stage_id IS NOT NULL;

ALTER TABLE material_out_lines
    ADD COLUMN bom_stage_id bigint NULL REFERENCES job_work_order_bom_stages(id) ON DELETE RESTRICT;
CREATE INDEX ix_material_out_lines_bom_stage ON material_out_lines(bom_stage_id, voucher_id)
    WHERE bom_stage_id IS NOT NULL;

-- Reject recursive BOM graphs at the database boundary as well as in the repository.
CREATE OR REPLACE FUNCTION textrack_reject_bom_cycle() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
    owner_item_id bigint;
    child_item_id bigint;
    cycle_found boolean;
BEGIN
    IF NEW.child_bom_id IS NULL THEN
        RETURN NEW;
    END IF;

    SELECT stock_item_id INTO owner_item_id FROM bill_of_materials WHERE id = NEW.bom_id;
    SELECT stock_item_id INTO child_item_id FROM bill_of_materials WHERE id = NEW.child_bom_id;
    IF owner_item_id IS NULL OR child_item_id IS NULL OR child_item_id <> NEW.component_stock_item_id THEN
        RAISE EXCEPTION 'Child BOM output must match the component Stock Item.';
    END IF;

    WITH RECURSIVE descendants(id) AS
    (
        SELECT NEW.child_bom_id
        UNION
        SELECT l.child_bom_id
        FROM bill_of_material_lines l
        JOIN descendants d ON d.id = l.bom_id
        WHERE l.child_bom_id IS NOT NULL
    )
    SELECT EXISTS(SELECT 1 FROM descendants WHERE id = NEW.bom_id) INTO cycle_found;

    IF cycle_found THEN
        RAISE EXCEPTION 'A BOM cannot directly or indirectly contain itself.';
    END IF;
    RETURN NEW;
END $$;

CREATE TRIGGER trg_bill_of_material_line_no_cycle
BEFORE INSERT OR UPDATE OF bom_id, child_bom_id, component_stock_item_id
ON bill_of_material_lines
FOR EACH ROW EXECUTE FUNCTION textrack_reject_bom_cycle();
