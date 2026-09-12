ALTER TABLE ledgers
    ADD COLUMN IF NOT EXISTS is_job_worker boolean NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS tally_ledger_name varchar(200) NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS default_material_out_destination_godown_id bigint NULL,
    ADD COLUMN IF NOT EXISTS default_material_in_consumption_godown_id bigint NULL;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_ledgers_job_worker_mo_godown' AND conrelid = 'ledgers'::regclass) THEN
        ALTER TABLE ledgers ADD CONSTRAINT fk_ledgers_job_worker_mo_godown
            FOREIGN KEY (default_material_out_destination_godown_id) REFERENCES godowns(id) ON DELETE RESTRICT;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_ledgers_job_worker_mi_godown' AND conrelid = 'ledgers'::regclass) THEN
        ALTER TABLE ledgers ADD CONSTRAINT fk_ledgers_job_worker_mi_godown
            FOREIGN KEY (default_material_in_consumption_godown_id) REFERENCES godowns(id) ON DELETE RESTRICT;
    END IF;
END $$;

UPDATE ledgers l
SET is_job_worker = true,
    tally_ledger_name = CASE WHEN btrim(l.tally_ledger_name) = '' THEN l.name ELSE l.tally_ledger_name END
WHERE EXISTS
(
    SELECT 1
    FROM vouchers v
    JOIN voucher_types vt ON vt.id = v.voucher_type_id
    LEFT JOIN voucher_types parent_vt ON parent_vt.id = vt.parent_voucher_type_id
    WHERE v.party_ledger_id = l.id
      AND COALESCE(NULLIF(vt.system_type_code, ''), parent_vt.system_type_code) IN ('JOB_WORK_OUT_ORDER', 'MATERIAL_OUT')
);

CREATE INDEX IF NOT EXISTS ix_ledgers_company_job_worker_name
    ON ledgers(company_id, name_normalized)
    WHERE is_job_worker = true;
CREATE INDEX IF NOT EXISTS ix_ledgers_job_worker_mo_godown
    ON ledgers(default_material_out_destination_godown_id)
    WHERE default_material_out_destination_godown_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_ledgers_job_worker_mi_godown
    ON ledgers(default_material_in_consumption_godown_id)
    WHERE default_material_in_consumption_godown_id IS NOT NULL;