-- Expected completion belongs to the versioned JWO stage/jobber assignment.
-- The voucher due_date remains a compatibility summary of the final stage date.

ALTER TABLE job_work_order_stage_assignments
    ADD COLUMN expected_completion_date date NULL;

UPDATE job_work_order_stage_assignments assignment
SET expected_completion_date = voucher.due_date
FROM job_work_order_bom_stages stage
JOIN vouchers voucher ON voucher.id = stage.voucher_id
WHERE assignment.bom_stage_id = stage.id
  AND assignment.expected_completion_date IS NULL;

CREATE INDEX ix_jwo_stage_assignment_expected_completion
    ON job_work_order_stage_assignments(status, expected_completion_date);
