-- Production-chain integrity: permanent stage assignment snapshots and stock-group rules.

ALTER TABLE material_out_lines
    ADD COLUMN stage_assignment_id bigint NULL REFERENCES job_work_order_stage_assignments(id) ON DELETE RESTRICT;
CREATE INDEX ix_material_out_stage_assignment ON material_out_lines(stage_assignment_id, voucher_id);

ALTER TABLE material_in_finished_goods
    ADD COLUMN stage_assignment_id bigint NULL REFERENCES job_work_order_stage_assignments(id) ON DELETE RESTRICT;
CREATE INDEX ix_material_in_fg_stage_assignment ON material_in_finished_goods(stage_assignment_id, voucher_id);

ALTER TABLE material_in_consumptions
    ADD COLUMN bom_stage_id bigint NULL REFERENCES job_work_order_bom_stages(id) ON DELETE RESTRICT,
    ADD COLUMN stage_assignment_id bigint NULL REFERENCES job_work_order_stage_assignments(id) ON DELETE RESTRICT;
CREATE INDEX ix_material_in_consumption_stage ON material_in_consumptions(bom_stage_id, voucher_id);

UPDATE material_out_lines line
SET stage_assignment_id = assignment.id
FROM job_work_order_stage_assignments assignment
WHERE assignment.bom_stage_id = line.bom_stage_id
  AND assignment.status = 'Active'
  AND line.stage_assignment_id IS NULL;

UPDATE material_in_finished_goods line
SET stage_assignment_id = assignment.id
FROM job_work_order_stage_assignments assignment
WHERE assignment.bom_stage_id = line.bom_stage_id
  AND assignment.status = 'Active'
  AND line.stage_assignment_id IS NULL;

UPDATE material_in_consumptions consumption
SET bom_stage_id = component.bom_stage_id
FROM job_work_order_components component
WHERE component.id = consumption.jwo_component_id
  AND consumption.bom_stage_id IS NULL;

UPDATE material_in_consumptions consumption
SET stage_assignment_id = assignment.id
FROM job_work_order_stage_assignments assignment
WHERE assignment.bom_stage_id = consumption.bom_stage_id
  AND assignment.status = 'Active'
  AND consumption.stage_assignment_id IS NULL;

INSERT INTO stock_groups
    (company_id, parent_id, name, name_normalized, alias, root_classification, is_system, is_active,
     created_at_utc, modified_at_utc, created_by, modified_by, concurrency_token)
SELECT company.id, NULL, 'Semi-Finished Goods', 'SEMI-FINISHED GOODS', 'SFG', 'SemiFinishedGoods', true, true,
       NOW(), NOW(), 'System', 'System', md5(random()::text)
FROM companies company
WHERE NOT EXISTS
(
    SELECT 1 FROM stock_groups existing
    WHERE existing.company_id = company.id AND existing.name_normalized = 'SEMI-FINISHED GOODS'
);
