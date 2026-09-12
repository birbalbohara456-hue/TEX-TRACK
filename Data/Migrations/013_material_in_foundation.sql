-- TexTrack ERP v0.5 Build 2.6A
-- Material In database foundation: finished-goods receipt, actual MO-based consumption,
-- oldest-MO-first traceability allocations, and stock movement linkage.

CREATE TABLE IF NOT EXISTS material_in_details
(
    voucher_id bigint PRIMARY KEY REFERENCES vouchers(id) ON DELETE CASCADE,
    jwo_voucher_id bigint NOT NULL REFERENCES vouchers(id) ON DELETE RESTRICT,
    consumption_godown_id bigint NOT NULL REFERENCES godowns(id) ON DELETE RESTRICT,
    receiving_godown_id bigint NOT NULL REFERENCES godowns(id) ON DELETE RESTRICT,
    displayed_order_number varchar(100) NOT NULL DEFAULT '',
    total_process_charge numeric(19,4) NOT NULL DEFAULT 0,
    total_consumed_material_value numeric(19,4) NOT NULL DEFAULT 0,
    total_finished_goods_value numeric(19,4) NOT NULL DEFAULT 0,
    CONSTRAINT ck_material_in_totals_nonnegative CHECK
        (total_process_charge >= 0 AND total_consumed_material_value >= 0 AND total_finished_goods_value >= 0)
);
CREATE INDEX IF NOT EXISTS ix_material_in_details_jwo
    ON material_in_details(jwo_voucher_id, voucher_id);

CREATE TABLE IF NOT EXISTS material_in_finished_goods
(
    id bigserial PRIMARY KEY,
    voucher_id bigint NOT NULL REFERENCES vouchers(id) ON DELETE CASCADE,
    jwo_finished_good_id bigint NOT NULL REFERENCES job_work_order_finished_goods(id) ON DELETE RESTRICT,
    line_number integer NOT NULL,
    stock_item_id bigint NOT NULL REFERENCES stock_items(id) ON DELETE RESTRICT,
    uqc_id bigint NOT NULL REFERENCES uqcs(id) ON DELETE RESTRICT,
    receiving_godown_id bigint NOT NULL REFERENCES godowns(id) ON DELETE RESTRICT,
    ordered_quantity numeric(19,4) NOT NULL,
    previously_received_quantity numeric(19,4) NOT NULL DEFAULT 0,
    received_quantity numeric(19,4) NOT NULL,
    material_value numeric(19,4) NOT NULL DEFAULT 0,
    process_charge numeric(19,4) NOT NULL DEFAULT 0,
    finished_goods_value numeric(19,4) NOT NULL DEFAULT 0,
    rate numeric(19,4) NOT NULL DEFAULT 0,
    CONSTRAINT ck_material_in_fg_line_positive CHECK (line_number > 0),
    CONSTRAINT ck_material_in_fg_qty CHECK
        (ordered_quantity >= 0 AND previously_received_quantity >= 0 AND received_quantity > 0),
    CONSTRAINT ck_material_in_fg_values CHECK
        (material_value >= 0 AND process_charge >= 0 AND finished_goods_value >= 0 AND rate >= 0),
    CONSTRAINT uq_material_in_fg_line UNIQUE (voucher_id, line_number),
    CONSTRAINT uq_material_in_fg_source UNIQUE (voucher_id, jwo_finished_good_id)
);
CREATE INDEX IF NOT EXISTS ix_material_in_fg_jwo
    ON material_in_finished_goods(jwo_finished_good_id, voucher_id);

CREATE TABLE IF NOT EXISTS material_in_fg_allocations
(
    id bigserial PRIMARY KEY,
    finished_good_line_id bigint NOT NULL REFERENCES material_in_finished_goods(id) ON DELETE CASCADE,
    stock_item_variant_id bigint NOT NULL REFERENCES stock_item_variants(id) ON DELETE RESTRICT,
    quantity numeric(19,4) NOT NULL,
    CONSTRAINT ck_material_in_fg_allocation_positive CHECK (quantity > 0),
    CONSTRAINT uq_material_in_fg_allocation UNIQUE (finished_good_line_id, stock_item_variant_id)
);
CREATE INDEX IF NOT EXISTS ix_material_in_fg_alloc_variant
    ON material_in_fg_allocations(stock_item_variant_id, finished_good_line_id);

CREATE TABLE IF NOT EXISTS material_in_consumptions
(
    id bigserial PRIMARY KEY,
    voucher_id bigint NOT NULL REFERENCES vouchers(id) ON DELETE CASCADE,
    jwo_component_id bigint NOT NULL REFERENCES job_work_order_components(id) ON DELETE RESTRICT,
    line_number integer NOT NULL,
    stock_item_id bigint NOT NULL REFERENCES stock_items(id) ON DELETE RESTRICT,
    uqc_id bigint NOT NULL REFERENCES uqcs(id) ON DELETE RESTRICT,
    consumption_godown_id bigint NOT NULL REFERENCES godowns(id) ON DELETE RESTRICT,
    available_quantity numeric(19,4) NOT NULL,
    consumed_quantity numeric(19,4) NOT NULL,
    rate numeric(19,4) NOT NULL DEFAULT 0,
    value numeric(19,4) NOT NULL DEFAULT 0,
    CONSTRAINT ck_material_in_consumption_line_positive CHECK (line_number > 0),
    CONSTRAINT ck_material_in_consumption_qty CHECK (available_quantity >= 0 AND consumed_quantity > 0),
    CONSTRAINT ck_material_in_consumption_values CHECK (rate >= 0 AND value >= 0),
    CONSTRAINT uq_material_in_consumption_line UNIQUE (voucher_id, line_number),
    CONSTRAINT uq_material_in_consumption_source UNIQUE (voucher_id, jwo_component_id)
);
CREATE INDEX IF NOT EXISTS ix_material_in_consumption_component
    ON material_in_consumptions(jwo_component_id, voucher_id);

CREATE TABLE IF NOT EXISTS material_in_mo_allocations
(
    id bigserial PRIMARY KEY,
    consumption_line_id bigint NOT NULL REFERENCES material_in_consumptions(id) ON DELETE CASCADE,
    material_out_line_id bigint NOT NULL REFERENCES material_out_lines(id) ON DELETE RESTRICT,
    allocated_quantity numeric(19,4) NOT NULL,
    rate_snapshot numeric(19,4) NOT NULL DEFAULT 0,
    value_snapshot numeric(19,4) NOT NULL DEFAULT 0,
    CONSTRAINT ck_material_in_mo_alloc_qty CHECK (allocated_quantity > 0),
    CONSTRAINT ck_material_in_mo_alloc_values CHECK (rate_snapshot >= 0 AND value_snapshot >= 0),
    CONSTRAINT uq_material_in_mo_alloc UNIQUE (consumption_line_id, material_out_line_id)
);
CREATE INDEX IF NOT EXISTS ix_material_in_mo_alloc_source
    ON material_in_mo_allocations(material_out_line_id, consumption_line_id);

ALTER TABLE stock_movements
    ADD COLUMN IF NOT EXISTS material_in_finished_good_id bigint NULL,
    ADD COLUMN IF NOT EXISTS material_in_consumption_id bigint NULL,
    ADD COLUMN IF NOT EXISTS stock_item_variant_id bigint NULL;

DO $$ BEGIN
    ALTER TABLE stock_movements ADD CONSTRAINT fk_stock_movements_material_in_fg
        FOREIGN KEY (material_in_finished_good_id) REFERENCES material_in_finished_goods(id) ON DELETE CASCADE;
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    ALTER TABLE stock_movements ADD CONSTRAINT fk_stock_movements_material_in_consumption
        FOREIGN KEY (material_in_consumption_id) REFERENCES material_in_consumptions(id) ON DELETE CASCADE;
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    ALTER TABLE stock_movements ADD CONSTRAINT fk_stock_movements_variant
        FOREIGN KEY (stock_item_variant_id) REFERENCES stock_item_variants(id) ON DELETE RESTRICT;
EXCEPTION WHEN duplicate_object THEN NULL; END $$;

CREATE INDEX IF NOT EXISTS ix_stock_movements_material_in_fg
    ON stock_movements(voucher_id, material_in_finished_good_id);
CREATE INDEX IF NOT EXISTS ix_stock_movements_material_in_consumption
    ON stock_movements(voucher_id, material_in_consumption_id);
