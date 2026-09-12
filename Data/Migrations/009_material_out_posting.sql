-- TexTrack ERP v0.5 Build 1.7
-- Transaction-safe Material Out posting, statutory detail persistence and stock movement ledger.

CREATE TABLE IF NOT EXISTS material_out_details
(
    voucher_id bigint PRIMARY KEY REFERENCES vouchers(id) ON DELETE CASCADE,
    provide_gst_eway_details boolean NOT NULL DEFAULT false,
    destination_godown_id bigint NOT NULL REFERENCES godowns(id) ON DELETE RESTRICT,
    displayed_order_number varchar(100) NOT NULL DEFAULT '',
    eway_bill_number varchar(30) NOT NULL DEFAULT '',
    eway_bill_date date NULL,
    consolidated_eway_bill_number varchar(30) NOT NULL DEFAULT '',
    consolidated_eway_bill_date date NULL,
    eway_sub_type varchar(50) NOT NULL DEFAULT 'Others',
    eway_document_type varchar(50) NOT NULL DEFAULT 'Delivery Challan',
    consignor_mailing_name varchar(200) NOT NULL DEFAULT '',
    consignor_gstin varchar(20) NOT NULL DEFAULT '',
    consignor_state varchar(100) NOT NULL DEFAULT '',
    consignor_address1 varchar(250) NOT NULL DEFAULT '',
    consignor_address2 varchar(250) NOT NULL DEFAULT '',
    consignor_pincode varchar(10) NOT NULL DEFAULT '',
    consignor_place varchar(100) NOT NULL DEFAULT '',
    consignor_actual_state varchar(100) NOT NULL DEFAULT '',
    consignee_mailing_name varchar(200) NOT NULL DEFAULT '',
    consignee_gstin varchar(20) NOT NULL DEFAULT '',
    consignee_state varchar(100) NOT NULL DEFAULT '',
    consignee_address1 varchar(250) NOT NULL DEFAULT '',
    consignee_address2 varchar(250) NOT NULL DEFAULT '',
    consignee_pincode varchar(10) NOT NULL DEFAULT '',
    consignee_place varchar(100) NOT NULL DEFAULT '',
    consignee_actual_state varchar(100) NOT NULL DEFAULT '',
    pin_to_pin_distance varchar(20) NOT NULL DEFAULT '',
    transporter_name varchar(200) NOT NULL DEFAULT '',
    transporter_id varchar(30) NOT NULL DEFAULT '',
    transport_mode varchar(40) NOT NULL DEFAULT 'Not Applicable',
    transport_document_number varchar(50) NOT NULL DEFAULT '',
    transport_document_date date NULL,
    vehicle_number varchar(30) NOT NULL DEFAULT '',
    vehicle_type varchar(50) NOT NULL DEFAULT 'Not Applicable'
);

CREATE TABLE IF NOT EXISTS stock_movements
(
    id bigserial PRIMARY KEY,
    company_id bigint NOT NULL REFERENCES companies(id) ON DELETE RESTRICT,
    financial_year_id bigint NOT NULL REFERENCES financial_years(id) ON DELETE RESTRICT,
    voucher_id bigint NOT NULL REFERENCES vouchers(id) ON DELETE CASCADE,
    material_out_line_id bigint NULL REFERENCES material_out_lines(id) ON DELETE CASCADE,
    movement_date date NOT NULL,
    stock_item_id bigint NOT NULL REFERENCES stock_items(id) ON DELETE RESTRICT,
    uqc_id bigint NOT NULL REFERENCES uqcs(id) ON DELETE RESTRICT,
    godown_id bigint NOT NULL REFERENCES godowns(id) ON DELETE RESTRICT,
    quantity_change numeric(19,4) NOT NULL,
    rate numeric(19,4) NOT NULL DEFAULT 0,
    value_change numeric(19,4) NOT NULL DEFAULT 0,
    movement_kind varchar(30) NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    created_by varchar(100) NOT NULL,
    CONSTRAINT ck_stock_movement_nonzero CHECK (quantity_change <> 0),
    CONSTRAINT ck_stock_movement_kind CHECK (movement_kind IN ('MaterialOutSource', 'MaterialOutDestination'))
);
CREATE INDEX IF NOT EXISTS ix_stock_movements_balance
    ON stock_movements(company_id, stock_item_id, godown_id, movement_date, id);
CREATE INDEX IF NOT EXISTS ix_stock_movements_voucher
    ON stock_movements(voucher_id, material_out_line_id);

ALTER TABLE material_out_lines DROP CONSTRAINT IF EXISTS ck_material_out_issued_not_over_required;
ALTER TABLE material_out_lines ADD CONSTRAINT ck_material_out_issued_not_over_required
    CHECK (previously_issued_quantity + issued_quantity <= required_quantity);
