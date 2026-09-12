-- Additive: legacy receipts retain their amounts and unknown rate snapshots remain NULL.
ALTER TABLE job_work_order_stage_assignments
    ADD COLUMN expected_process_rate numeric(19,4) NULL CHECK (expected_process_rate >= 0);
ALTER TABLE material_in_finished_goods
    ADD COLUMN expected_process_rate numeric(19,4) NULL CHECK (expected_process_rate >= 0),
    ADD COLUMN actual_process_rate numeric(19,4) NULL CHECK (actual_process_rate >= 0),
    ADD COLUMN charge_mode varchar(16) NOT NULL DEFAULT 'Total'
        CHECK (charge_mode IN ('Total', 'PerUnit'));
