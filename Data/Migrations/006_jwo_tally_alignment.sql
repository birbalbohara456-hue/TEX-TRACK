-- TexTrack ERP v0.4 Build 2
-- Live Tally JWO alignment: per-FG and per-component godowns plus XML valuation fields.

ALTER TABLE job_work_order_finished_goods
    ADD COLUMN IF NOT EXISTS finished_goods_godown_id bigint NULL REFERENCES godowns(id) ON DELETE RESTRICT,
    ADD COLUMN IF NOT EXISTS destination_godown_id bigint NULL REFERENCES godowns(id) ON DELETE RESTRICT,
    ADD COLUMN IF NOT EXISTS xml_rate numeric(19,4) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS xml_amount numeric(19,4) NOT NULL DEFAULT 0;

CREATE INDEX IF NOT EXISTS ix_jwo_fg_finished_goods_godown
    ON job_work_order_finished_goods(finished_goods_godown_id, voucher_id);
CREATE INDEX IF NOT EXISTS ix_jwo_fg_destination_godown
    ON job_work_order_finished_goods(destination_godown_id, voucher_id);

ALTER TABLE job_work_order_components
    ADD COLUMN IF NOT EXISTS component_godown_id bigint NULL REFERENCES godowns(id) ON DELETE RESTRICT,
    ADD COLUMN IF NOT EXISTS xml_rate numeric(19,4) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS xml_amount numeric(19,4) NOT NULL DEFAULT 0;

CREATE INDEX IF NOT EXISTS ix_jwo_component_godown
    ON job_work_order_components(component_godown_id, finished_good_id);

-- Custom voucher types inherit behaviour from a protected permanent parent.
ALTER TABLE voucher_types
    ADD COLUMN IF NOT EXISTS parent_voucher_type_id bigint NULL REFERENCES voucher_types(id) ON DELETE RESTRICT;
CREATE INDEX IF NOT EXISTS ix_voucher_types_parent
    ON voucher_types(company_id, parent_voucher_type_id, name_normalized);
