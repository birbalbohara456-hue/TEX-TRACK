-- Perpetual FIFO dormant foundation.
--
-- This migration is additive. It does not create layers for historical stock,
-- change a persisted movement quantity/value, activate FIFO, or alter reports.

CREATE TABLE inventory_posting_sequences
(
    company_id bigint PRIMARY KEY REFERENCES companies(id) ON DELETE RESTRICT,
    last_posting_order bigint NOT NULL DEFAULT 0,
    modified_at_utc timestamp with time zone NOT NULL,
    modified_by varchar(100) NOT NULL,
    CONSTRAINT ck_inventory_posting_sequence_nonnegative CHECK (last_posting_order >= 0)
);

CREATE TABLE inventory_postings
(
    id bigserial PRIMARY KEY,
    operation_id uuid NOT NULL,
    company_id bigint NOT NULL REFERENCES companies(id) ON DELETE RESTRICT,
    financial_year_id bigint NOT NULL REFERENCES financial_years(id) ON DELETE RESTRICT,
    voucher_id bigint NOT NULL REFERENCES vouchers(id) ON DELETE RESTRICT,
    effective_date date NOT NULL,
    effective_time time without time zone NULL,
    posting_order bigint NOT NULL,
    event_kind varchar(20) NOT NULL,
    reversal_of_posting_id bigint NULL REFERENCES inventory_postings(id) ON DELETE RESTRICT,
    algorithm_version integer NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    created_by varchar(100) NOT NULL,
    CONSTRAINT uq_inventory_postings_operation UNIQUE (operation_id),
    CONSTRAINT uq_inventory_postings_company_order UNIQUE (company_id, posting_order),
    CONSTRAINT ck_inventory_posting_order_positive CHECK (posting_order > 0),
    CONSTRAINT ck_inventory_posting_algorithm_positive CHECK (algorithm_version > 0),
    CONSTRAINT ck_inventory_posting_event_kind CHECK (event_kind IN ('Original', 'Reversal', 'Cutover')),
    CONSTRAINT ck_inventory_posting_reversal_shape CHECK
    (
        (event_kind = 'Reversal' AND reversal_of_posting_id IS NOT NULL)
        OR (event_kind <> 'Reversal' AND reversal_of_posting_id IS NULL)
    )
);
CREATE UNIQUE INDEX uq_inventory_postings_reversal
    ON inventory_postings(reversal_of_posting_id)
    WHERE reversal_of_posting_id IS NOT NULL;
CREATE INDEX ix_inventory_postings_company_effective_order
    ON inventory_postings(company_id, effective_date, posting_order);
CREATE INDEX ix_inventory_postings_voucher
    ON inventory_postings(voucher_id, posting_order);

ALTER TABLE stock_movements
    ADD COLUMN inventory_posting_id bigint NULL REFERENCES inventory_postings(id) ON DELETE RESTRICT,
    ADD COLUMN movement_line_order integer NULL,
    ADD CONSTRAINT ck_stock_movement_layered_shape CHECK
    (
        (inventory_posting_id IS NULL AND movement_line_order IS NULL)
        OR (inventory_posting_id IS NOT NULL AND movement_line_order > 0)
    );
CREATE UNIQUE INDEX uq_stock_movements_posting_line
    ON stock_movements(inventory_posting_id, movement_line_order)
    WHERE inventory_posting_id IS NOT NULL;

ALTER TABLE audit_logs
    ADD COLUMN inventory_posting_id bigint NULL REFERENCES inventory_postings(id) ON DELETE RESTRICT,
    ADD COLUMN correlation_id uuid NULL;
CREATE INDEX ix_audit_logs_inventory_posting
    ON audit_logs(inventory_posting_id)
    WHERE inventory_posting_id IS NOT NULL;
CREATE INDEX ix_audit_logs_correlation
    ON audit_logs(correlation_id)
    WHERE correlation_id IS NOT NULL;

CREATE TABLE inventory_valuation_runs
(
    id uuid PRIMARY KEY,
    idempotency_key uuid NOT NULL UNIQUE,
    company_id bigint NOT NULL REFERENCES companies(id) ON DELETE RESTRICT,
    run_kind varchar(30) NOT NULL,
    algorithm_version integer NOT NULL,
    schema_version integer NOT NULL,
    requested_scope_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    cutoff_date date NULL,
    state varchar(20) NOT NULL,
    requested_at_utc timestamp with time zone NOT NULL,
    requested_by varchar(100) NOT NULL,
    started_at_utc timestamp with time zone NULL,
    started_by varchar(100) NOT NULL DEFAULT '',
    finished_at_utc timestamp with time zone NULL,
    finished_by varchar(100) NOT NULL DEFAULT '',
    attempt_count integer NOT NULL DEFAULT 0,
    lease_owner varchar(200) NOT NULL DEFAULT '',
    lease_expires_at_utc timestamp with time zone NULL,
    heartbeat_at_utc timestamp with time zone NULL,
    earliest_affected_date date NULL,
    earliest_affected_posting_order bigint NULL,
    affected_position_count bigint NOT NULL DEFAULT 0,
    affected_movement_count bigint NOT NULL DEFAULT 0,
    affected_layer_count bigint NOT NULL DEFAULT 0,
    quantity_before numeric(19,4) NOT NULL DEFAULT 0,
    quantity_after numeric(19,4) NOT NULL DEFAULT 0,
    value_before numeric(19,4) NOT NULL DEFAULT 0,
    value_after numeric(19,4) NOT NULL DEFAULT 0,
    failure_summary varchar(1000) NOT NULL DEFAULT '',
    diagnostic_reference varchar(500) NOT NULL DEFAULT '',
    audit_correlation_id uuid NOT NULL,
    CONSTRAINT ck_inventory_valuation_run_kind CHECK
        (run_kind IN ('InitialBuild', 'Cutover', 'Replay', 'Reconciliation')),
    CONSTRAINT ck_inventory_valuation_run_state CHECK
        (state IN ('Pending', 'Running', 'Succeeded', 'Failed', 'Cancelled')),
    CONSTRAINT ck_inventory_valuation_run_versions CHECK
        (algorithm_version > 0 AND schema_version > 0),
    CONSTRAINT ck_inventory_valuation_run_attempt CHECK (attempt_count >= 0),
    CONSTRAINT ck_inventory_valuation_run_counts CHECK
        (affected_position_count >= 0 AND affected_movement_count >= 0 AND affected_layer_count >= 0),
    CONSTRAINT ck_inventory_valuation_run_scope_object CHECK (jsonb_typeof(requested_scope_json) = 'object')
);
CREATE INDEX ix_inventory_valuation_runs_company_state
    ON inventory_valuation_runs(company_id, state, requested_at_utc);
CREATE INDEX ix_inventory_valuation_runs_lease
    ON inventory_valuation_runs(state, lease_expires_at_utc)
    WHERE state IN ('Pending', 'Running');

CREATE TABLE inventory_valuation_settings
(
    company_id bigint PRIMARY KEY REFERENCES companies(id) ON DELETE RESTRICT,
    book_method varchar(30) NOT NULL DEFAULT 'LegacyPersistedValue',
    lifecycle_state varchar(30) NOT NULL DEFAULT 'NotInitialized',
    cutover_date date NULL,
    cutover_run_id uuid NULL REFERENCES inventory_valuation_runs(id) ON DELETE RESTRICT,
    active_algorithm_version integer NOT NULL DEFAULT 1,
    activated_at_utc timestamp with time zone NULL,
    activated_by varchar(100) NOT NULL DEFAULT '',
    activation_reason varchar(1000) NOT NULL DEFAULT '',
    last_successful_reconciliation_run_id uuid NULL REFERENCES inventory_valuation_runs(id) ON DELETE RESTRICT,
    modified_at_utc timestamp with time zone NOT NULL,
    modified_by varchar(100) NOT NULL,
    concurrency_token varchar(32) NOT NULL,
    CONSTRAINT ck_inventory_valuation_book_method CHECK
        (book_method IN ('LegacyPersistedValue', 'FIFO')),
    CONSTRAINT ck_inventory_valuation_lifecycle CHECK
        (lifecycle_state IN ('NotInitialized', 'Building', 'Ready', 'PendingReplay', 'Failed')),
    CONSTRAINT ck_inventory_valuation_algorithm_positive CHECK (active_algorithm_version > 0),
    CONSTRAINT ck_inventory_valuation_activation_shape CHECK
    (
        (book_method = 'FIFO' AND lifecycle_state = 'Ready'
            AND activated_at_utc IS NOT NULL AND activated_by <> '' AND activation_reason <> '')
        OR book_method = 'LegacyPersistedValue'
    )
);

CREATE TABLE inventory_valuation_positions
(
    id bigserial PRIMARY KEY,
    company_id bigint NOT NULL REFERENCES companies(id) ON DELETE RESTRICT,
    stock_item_id bigint NOT NULL REFERENCES stock_items(id) ON DELETE RESTRICT,
    stock_item_variant_id bigint NULL REFERENCES stock_item_variants(id) ON DELETE RESTRICT,
    uqc_id bigint NOT NULL REFERENCES uqcs(id) ON DELETE RESTRICT,
    godown_id bigint NOT NULL REFERENCES godowns(id) ON DELETE RESTRICT,
    state varchar(30) NOT NULL DEFAULT 'Legacy',
    earliest_dirty_date date NULL,
    earliest_dirty_posting_order bigint NULL,
    current_successful_run_id uuid NULL REFERENCES inventory_valuation_runs(id) ON DELETE RESTRICT,
    last_error_summary varchar(1000) NOT NULL DEFAULT '',
    modified_at_utc timestamp with time zone NOT NULL,
    modified_by varchar(100) NOT NULL,
    concurrency_token varchar(32) NOT NULL,
    CONSTRAINT ck_inventory_valuation_position_state CHECK
        (state IN ('Ready', 'PendingReplay', 'Failed', 'Legacy')),
    CONSTRAINT ck_inventory_valuation_position_dirty_order CHECK
        (earliest_dirty_posting_order IS NULL OR earliest_dirty_posting_order > 0)
);
CREATE UNIQUE INDEX uq_inventory_valuation_position_exact
    ON inventory_valuation_positions
       (company_id, stock_item_id, COALESCE(stock_item_variant_id, 0), uqc_id, godown_id);
CREATE INDEX ix_inventory_valuation_positions_company_state
    ON inventory_valuation_positions(company_id, state);

CREATE TABLE inventory_cost_layers
(
    id bigserial PRIMARY KEY,
    company_id bigint NOT NULL REFERENCES companies(id) ON DELETE RESTRICT,
    receipt_movement_id bigint NOT NULL REFERENCES stock_movements(id) ON DELETE RESTRICT,
    layer_sequence integer NOT NULL,
    stock_item_id bigint NOT NULL REFERENCES stock_items(id) ON DELETE RESTRICT,
    stock_item_variant_id bigint NULL REFERENCES stock_item_variants(id) ON DELETE RESTRICT,
    uqc_id bigint NOT NULL REFERENCES uqcs(id) ON DELETE RESTRICT,
    godown_id bigint NOT NULL REFERENCES godowns(id) ON DELETE RESTRICT,
    origin_kind varchar(30) NOT NULL,
    source_allocation_id bigint NULL,
    original_quantity numeric(19,4) NOT NULL,
    original_value numeric(19,4) NOT NULL,
    unit_cost numeric(28,8) NOT NULL,
    remaining_quantity numeric(19,4) NOT NULL,
    remaining_value numeric(19,4) NOT NULL,
    effective_date date NOT NULL,
    posting_order bigint NOT NULL,
    movement_line_order integer NOT NULL,
    algorithm_version integer NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    created_by varchar(100) NOT NULL,
    CONSTRAINT uq_inventory_cost_layer_receipt_sequence UNIQUE (receipt_movement_id, layer_sequence),
    CONSTRAINT ck_inventory_cost_layer_sequence_positive CHECK (layer_sequence > 0),
    CONSTRAINT ck_inventory_cost_layer_origin CHECK
        (origin_kind IN ('Purchase', 'Opening', 'Manufactured', 'Transfer', 'Reversal', 'CutoverSynthetic')),
    CONSTRAINT ck_inventory_cost_layer_source_shape CHECK
    (
        (origin_kind IN ('Transfer', 'Reversal') AND source_allocation_id IS NOT NULL)
        OR (origin_kind NOT IN ('Transfer', 'Reversal') AND source_allocation_id IS NULL)
    ),
    CONSTRAINT ck_inventory_cost_layer_original CHECK
        (original_quantity > 0 AND original_value >= 0 AND unit_cost >= 0),
    CONSTRAINT ck_inventory_cost_layer_remaining CHECK
        (remaining_quantity >= 0 AND remaining_quantity <= original_quantity
         AND remaining_value >= 0 AND remaining_value <= original_value),
    CONSTRAINT ck_inventory_cost_layer_order CHECK
        (posting_order > 0 AND movement_line_order > 0 AND algorithm_version > 0)
);
CREATE UNIQUE INDEX uq_inventory_cost_layer_source_allocation
    ON inventory_cost_layers(source_allocation_id)
    WHERE source_allocation_id IS NOT NULL;
CREATE INDEX ix_inventory_cost_layers_fifo
    ON inventory_cost_layers
       (company_id, stock_item_id, COALESCE(stock_item_variant_id, 0), uqc_id, godown_id,
        effective_date, posting_order, movement_line_order, layer_sequence, id)
    WHERE remaining_quantity > 0;

CREATE TABLE inventory_cost_allocations
(
    id bigserial PRIMARY KEY,
    operation_id uuid NOT NULL REFERENCES inventory_postings(operation_id) ON DELETE RESTRICT,
    company_id bigint NOT NULL REFERENCES companies(id) ON DELETE RESTRICT,
    outward_movement_id bigint NOT NULL REFERENCES stock_movements(id) ON DELETE RESTRICT,
    source_layer_id bigint NOT NULL REFERENCES inventory_cost_layers(id) ON DELETE RESTRICT,
    allocation_sequence integer NOT NULL,
    allocated_quantity numeric(19,4) NOT NULL,
    allocated_value numeric(19,4) NOT NULL,
    unit_cost_snapshot numeric(28,8) NOT NULL,
    reversal_of_allocation_id bigint NULL REFERENCES inventory_cost_allocations(id) ON DELETE RESTRICT,
    created_at_utc timestamp with time zone NOT NULL,
    created_by varchar(100) NOT NULL,
    algorithm_version integer NOT NULL,
    CONSTRAINT uq_inventory_cost_allocation_sequence UNIQUE (outward_movement_id, allocation_sequence),
    CONSTRAINT uq_inventory_cost_allocation_layer UNIQUE (outward_movement_id, source_layer_id),
    CONSTRAINT ck_inventory_cost_allocation_positive CHECK
        (allocation_sequence > 0 AND allocated_quantity > 0
         AND allocated_value >= 0 AND unit_cost_snapshot >= 0 AND algorithm_version > 0)
);
CREATE UNIQUE INDEX uq_inventory_cost_allocation_reversal
    ON inventory_cost_allocations(reversal_of_allocation_id)
    WHERE reversal_of_allocation_id IS NOT NULL;
CREATE INDEX ix_inventory_cost_allocations_source_layer
    ON inventory_cost_allocations(source_layer_id, id);

ALTER TABLE inventory_cost_layers
    ADD CONSTRAINT fk_inventory_cost_layer_source_allocation
    FOREIGN KEY (source_allocation_id) REFERENCES inventory_cost_allocations(id) ON DELETE RESTRICT;

CREATE OR REPLACE FUNCTION textrack_validate_inventory_posting()
RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
    voucher_row vouchers%ROWTYPE;
    original_row inventory_postings%ROWTYPE;
BEGIN
    IF TG_OP = 'DELETE' THEN
        RAISE EXCEPTION 'Layered inventory postings are immutable and cannot be deleted.';
    END IF;
    IF TG_OP = 'UPDATE' THEN
        RAISE EXCEPTION 'Layered inventory posting identity is immutable.';
    END IF;

    SELECT * INTO voucher_row FROM vouchers WHERE id = NEW.voucher_id;
    IF NOT FOUND OR voucher_row.company_id <> NEW.company_id
       OR voucher_row.financial_year_id <> NEW.financial_year_id
       OR voucher_row.voucher_date <> NEW.effective_date THEN
        RAISE EXCEPTION 'Inventory posting company, financial year and effective date must match its voucher.';
    END IF;

    IF NEW.reversal_of_posting_id IS NOT NULL THEN
        SELECT * INTO original_row FROM inventory_postings WHERE id = NEW.reversal_of_posting_id;
        IF NOT FOUND OR original_row.company_id <> NEW.company_id THEN
            RAISE EXCEPTION 'Inventory posting reversal must reference a posting in the same company.';
        END IF;
    END IF;
    RETURN NEW;
END $$;

CREATE TRIGGER tr_inventory_postings_validate
BEFORE INSERT OR UPDATE OR DELETE ON inventory_postings
FOR EACH ROW EXECUTE FUNCTION textrack_validate_inventory_posting();

CREATE OR REPLACE FUNCTION textrack_validate_layered_stock_movement()
RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
    posting_row inventory_postings%ROWTYPE;
    active_method varchar(30);
    active_state varchar(30);
BEGIN
    IF TG_OP = 'DELETE' THEN
        IF OLD.inventory_posting_id IS NOT NULL THEN
            RAISE EXCEPTION 'A layered stock movement cannot be deleted.';
        END IF;
        RETURN OLD;
    END IF;

    IF TG_OP = 'UPDATE' AND OLD.inventory_posting_id IS NOT NULL AND
       (OLD.inventory_posting_id, OLD.movement_line_order, OLD.company_id, OLD.financial_year_id,
        OLD.voucher_id, OLD.movement_date, OLD.stock_item_id, OLD.stock_item_variant_id,
        OLD.uqc_id, OLD.godown_id, OLD.quantity_change, OLD.rate, OLD.value_change, OLD.movement_kind)
       IS DISTINCT FROM
       (NEW.inventory_posting_id, NEW.movement_line_order, NEW.company_id, NEW.financial_year_id,
        NEW.voucher_id, NEW.movement_date, NEW.stock_item_id, NEW.stock_item_variant_id,
        NEW.uqc_id, NEW.godown_id, NEW.quantity_change, NEW.rate, NEW.value_change, NEW.movement_kind) THEN
        RAISE EXCEPTION 'Layered stock movement identity and value are immutable.';
    END IF;

    SELECT book_method, lifecycle_state INTO active_method, active_state
    FROM inventory_valuation_settings WHERE company_id = NEW.company_id;
    IF active_method = 'FIFO' AND active_state = 'Ready' AND NEW.inventory_posting_id IS NULL THEN
        RAISE EXCEPTION 'FIFO-active company stock movements require valuation posting evidence.';
    END IF;

    IF NEW.inventory_posting_id IS NULL THEN
        RETURN NEW;
    END IF;

    SELECT * INTO posting_row FROM inventory_postings WHERE id = NEW.inventory_posting_id;
    IF NOT FOUND OR posting_row.company_id <> NEW.company_id
       OR posting_row.financial_year_id <> NEW.financial_year_id
       OR posting_row.voucher_id <> NEW.voucher_id
       OR posting_row.effective_date <> NEW.movement_date THEN
        RAISE EXCEPTION 'Layered stock movement must match its posting company, year, voucher and date.';
    END IF;
    IF (NEW.quantity_change > 0 AND NEW.value_change < 0)
       OR (NEW.quantity_change < 0 AND NEW.value_change > 0) THEN
        RAISE EXCEPTION 'Stock movement value sign must agree with its quantity direction.';
    END IF;
    RETURN NEW;
END $$;

CREATE TRIGGER tr_stock_movements_layered_validate
BEFORE INSERT OR UPDATE OR DELETE ON stock_movements
FOR EACH ROW EXECUTE FUNCTION textrack_validate_layered_stock_movement();

CREATE OR REPLACE FUNCTION textrack_validate_inventory_cost_layer()
RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
    receipt_row stock_movements%ROWTYPE;
    source_allocation_row inventory_cost_allocations%ROWTYPE;
    source_layer_row inventory_cost_layers%ROWTYPE;
BEGIN
    IF TG_OP = 'DELETE' THEN
        RAISE EXCEPTION 'Inventory cost layers cannot be deleted.';
    END IF;
    IF TG_OP = 'UPDATE' THEN
        IF (OLD.id, OLD.company_id, OLD.receipt_movement_id, OLD.layer_sequence,
            OLD.stock_item_id, OLD.stock_item_variant_id, OLD.uqc_id, OLD.godown_id,
            OLD.origin_kind, OLD.source_allocation_id, OLD.original_quantity, OLD.original_value,
            OLD.unit_cost, OLD.effective_date, OLD.posting_order, OLD.movement_line_order,
            OLD.algorithm_version, OLD.created_at_utc, OLD.created_by)
           IS DISTINCT FROM
           (NEW.id, NEW.company_id, NEW.receipt_movement_id, NEW.layer_sequence,
            NEW.stock_item_id, NEW.stock_item_variant_id, NEW.uqc_id, NEW.godown_id,
            NEW.origin_kind, NEW.source_allocation_id, NEW.original_quantity, NEW.original_value,
            NEW.unit_cost, NEW.effective_date, NEW.posting_order, NEW.movement_line_order,
            NEW.algorithm_version, NEW.created_at_utc, NEW.created_by) THEN
            RAISE EXCEPTION 'Inventory cost layer origin and identity are immutable.';
        END IF;
    END IF;

    SELECT * INTO receipt_row FROM stock_movements WHERE id = NEW.receipt_movement_id;
    IF NOT FOUND OR receipt_row.quantity_change <= 0 OR receipt_row.inventory_posting_id IS NULL
       OR receipt_row.company_id <> NEW.company_id OR receipt_row.stock_item_id <> NEW.stock_item_id
       OR receipt_row.stock_item_variant_id IS DISTINCT FROM NEW.stock_item_variant_id
       OR receipt_row.uqc_id <> NEW.uqc_id OR receipt_row.godown_id <> NEW.godown_id
       OR receipt_row.movement_date <> NEW.effective_date
       OR receipt_row.movement_line_order <> NEW.movement_line_order THEN
        RAISE EXCEPTION 'Inventory cost layer must exactly match a layered inward stock movement.';
    END IF;
    IF NOT EXISTS
       (SELECT 1 FROM inventory_postings p WHERE p.id = receipt_row.inventory_posting_id
        AND p.posting_order = NEW.posting_order) THEN
        RAISE EXCEPTION 'Inventory cost layer posting order must match its receipt posting.';
    END IF;

    IF NEW.source_allocation_id IS NOT NULL THEN
        SELECT * INTO source_allocation_row FROM inventory_cost_allocations WHERE id = NEW.source_allocation_id;
        SELECT * INTO source_layer_row FROM inventory_cost_layers WHERE id = source_allocation_row.source_layer_id;
        IF NOT FOUND OR source_allocation_row.company_id <> NEW.company_id
           OR source_allocation_row.allocated_quantity <> NEW.original_quantity
           OR source_allocation_row.allocated_value <> NEW.original_value
           OR source_allocation_row.unit_cost_snapshot <> NEW.unit_cost
           OR source_layer_row.stock_item_id <> NEW.stock_item_id
           OR source_layer_row.stock_item_variant_id IS DISTINCT FROM NEW.stock_item_variant_id
           OR source_layer_row.uqc_id <> NEW.uqc_id THEN
            RAISE EXCEPTION 'Transfer or reversal child layer must preserve its source allocation quantity, value and stock identity.';
        END IF;
    END IF;
    RETURN NEW;
END $$;

CREATE TRIGGER tr_inventory_cost_layers_validate
BEFORE INSERT OR UPDATE OR DELETE ON inventory_cost_layers
FOR EACH ROW EXECUTE FUNCTION textrack_validate_inventory_cost_layer();

CREATE OR REPLACE FUNCTION textrack_validate_inventory_cost_allocation()
RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
    movement_row stock_movements%ROWTYPE;
    layer_row inventory_cost_layers%ROWTYPE;
    posting_operation uuid;
    allocated_quantity_total numeric(19,4);
    allocated_value_total numeric(19,4);
BEGIN
    IF TG_OP = 'DELETE' THEN
        RAISE EXCEPTION 'Inventory cost allocations cannot be deleted.';
    END IF;
    IF TG_OP = 'UPDATE' THEN
        RAISE EXCEPTION 'Inventory cost allocations are immutable.';
    END IF;

    SELECT * INTO movement_row FROM stock_movements WHERE id = NEW.outward_movement_id;
    IF NOT FOUND OR movement_row.quantity_change >= 0 OR movement_row.inventory_posting_id IS NULL THEN
        RAISE EXCEPTION 'Inventory cost allocation requires a layered outward stock movement.';
    END IF;
    SELECT operation_id INTO posting_operation FROM inventory_postings
    WHERE id = movement_row.inventory_posting_id;
    IF posting_operation IS DISTINCT FROM NEW.operation_id THEN
        RAISE EXCEPTION 'Inventory cost allocation operation must match its outward posting.';
    END IF;

    SELECT * INTO layer_row FROM inventory_cost_layers WHERE id = NEW.source_layer_id FOR UPDATE;
    IF NOT FOUND OR layer_row.company_id <> NEW.company_id OR movement_row.company_id <> NEW.company_id
       OR layer_row.stock_item_id <> movement_row.stock_item_id
       OR layer_row.stock_item_variant_id IS DISTINCT FROM movement_row.stock_item_variant_id
       OR layer_row.uqc_id <> movement_row.uqc_id OR layer_row.godown_id <> movement_row.godown_id
       OR NEW.unit_cost_snapshot <> layer_row.unit_cost THEN
        RAISE EXCEPTION 'FIFO allocation source layer must match the outward movement exact stock position and company.';
    END IF;

    SELECT COALESCE(SUM(allocated_quantity), 0), COALESCE(SUM(allocated_value), 0)
      INTO allocated_quantity_total, allocated_value_total
      FROM inventory_cost_allocations WHERE source_layer_id = NEW.source_layer_id;
    IF allocated_quantity_total + NEW.allocated_quantity > layer_row.original_quantity
       OR allocated_value_total + NEW.allocated_value > layer_row.original_value THEN
        RAISE EXCEPTION 'FIFO allocation exceeds the source layer original quantity or value.';
    END IF;
    RETURN NEW;
END $$;

CREATE TRIGGER tr_inventory_cost_allocations_validate
BEFORE INSERT OR UPDATE OR DELETE ON inventory_cost_allocations
FOR EACH ROW EXECUTE FUNCTION textrack_validate_inventory_cost_allocation();

CREATE OR REPLACE FUNCTION textrack_validate_valuation_run_reference()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF NEW.cutover_run_id IS NOT NULL AND NOT EXISTS
       (SELECT 1 FROM inventory_valuation_runs r WHERE r.id = NEW.cutover_run_id AND r.company_id = NEW.company_id) THEN
        RAISE EXCEPTION 'Valuation cutover run must belong to the same company.';
    END IF;
    IF NEW.last_successful_reconciliation_run_id IS NOT NULL AND NOT EXISTS
       (SELECT 1 FROM inventory_valuation_runs r
        WHERE r.id = NEW.last_successful_reconciliation_run_id AND r.company_id = NEW.company_id
          AND r.run_kind = 'Reconciliation' AND r.state = 'Succeeded') THEN
        RAISE EXCEPTION 'Successful reconciliation run must belong to the same company and be succeeded.';
    END IF;
    RETURN NEW;
END $$;

CREATE TRIGGER tr_inventory_valuation_settings_run_company
BEFORE INSERT OR UPDATE ON inventory_valuation_settings
FOR EACH ROW EXECUTE FUNCTION textrack_validate_valuation_run_reference();

CREATE OR REPLACE FUNCTION textrack_validate_valuation_position()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM stock_items WHERE id = NEW.stock_item_id AND company_id = NEW.company_id)
       OR NOT EXISTS (SELECT 1 FROM uqcs WHERE id = NEW.uqc_id AND company_id = NEW.company_id)
       OR NOT EXISTS (SELECT 1 FROM godowns WHERE id = NEW.godown_id AND company_id = NEW.company_id)
       OR (NEW.stock_item_variant_id IS NOT NULL AND NOT EXISTS
           (SELECT 1 FROM stock_item_variants
            WHERE id = NEW.stock_item_variant_id AND company_id = NEW.company_id
              AND stock_item_id = NEW.stock_item_id)) THEN
        RAISE EXCEPTION 'Valuation position stock identity must belong to the same company.';
    END IF;
    IF NEW.current_successful_run_id IS NOT NULL AND NOT EXISTS
       (SELECT 1 FROM inventory_valuation_runs r
        WHERE r.id = NEW.current_successful_run_id AND r.company_id = NEW.company_id
          AND r.state = 'Succeeded') THEN
        RAISE EXCEPTION 'Valuation position successful run must belong to the same company and be succeeded.';
    END IF;
    RETURN NEW;
END $$;

CREATE TRIGGER tr_inventory_valuation_positions_validate
BEFORE INSERT OR UPDATE ON inventory_valuation_positions
FOR EACH ROW EXECUTE FUNCTION textrack_validate_valuation_position();

CREATE OR REPLACE FUNCTION textrack_seed_company_valuation_foundation()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    INSERT INTO inventory_posting_sequences(company_id, last_posting_order, modified_at_utc, modified_by)
    VALUES (NEW.id, 0, NOW(), 'System')
    ON CONFLICT (company_id) DO NOTHING;

    INSERT INTO inventory_valuation_settings
        (company_id, book_method, lifecycle_state, active_algorithm_version,
         modified_at_utc, modified_by, concurrency_token)
    VALUES
        (NEW.id, 'LegacyPersistedValue', 'NotInitialized', 1,
         NOW(), 'System', md5(random()::text || clock_timestamp()::text))
    ON CONFLICT (company_id) DO NOTHING;
    RETURN NEW;
END $$;

CREATE TRIGGER tr_companies_seed_valuation_foundation
AFTER INSERT ON companies
FOR EACH ROW EXECUTE FUNCTION textrack_seed_company_valuation_foundation();

INSERT INTO inventory_posting_sequences(company_id, last_posting_order, modified_at_utc, modified_by)
SELECT id, 0, NOW(), 'Migration 042' FROM companies
ON CONFLICT (company_id) DO NOTHING;

INSERT INTO inventory_valuation_settings
    (company_id, book_method, lifecycle_state, active_algorithm_version,
     modified_at_utc, modified_by, concurrency_token)
SELECT id, 'LegacyPersistedValue', 'NotInitialized', 1,
       NOW(), 'Migration 042', md5(random()::text || clock_timestamp()::text)
FROM companies
ON CONFLICT (company_id) DO NOTHING;

INSERT INTO audit_logs
    (company_id, entity_type, entity_id, action, success, description,
     performed_by, performed_at_utc, correlation_id)
SELECT id, 'InventoryValuationFoundation', id, 'Migration', true,
       'Migration 042 installed the dormant FIFO foundation. Existing stock movements and reports remain in LegacyPersistedValue mode; no historical layers were created.',
       'Migration 042', NOW(), gen_random_uuid()
FROM companies;

COMMENT ON TABLE inventory_postings IS
    'Immutable posting headers for layered inventory. Dormant until a company passes FIFO activation preflight.';
COMMENT ON TABLE inventory_cost_layers IS
    'Immutable inward cost tranches with transactionally mutable remaining quantity/value.';
COMMENT ON TABLE inventory_cost_allocations IS
    'Immutable evidence connecting outward movement quantity/value to exact FIFO source layers.';
COMMENT ON TABLE inventory_valuation_settings IS
    'Per-company book method and lifecycle. Migration 042 seeds LegacyPersistedValue/NotInitialized only.';
