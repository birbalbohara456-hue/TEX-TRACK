-- Fixed "Design Photo" field, present on every Stock Item regardless of company
-- Attribute Master configuration. Deliberately separate from
-- stock_item_field_definitions / stock_item_photos (the user-configurable
-- Photo-type attribute mechanism) so a company's own Photo attributes (Front
-- Photo, Back Photo, Additional Photos, etc.) never compete with or hide the
-- one canonical product photo shown on the Item form.
CREATE TABLE stock_item_design_photos
(
    id bigserial PRIMARY KEY,
    company_id bigint NOT NULL REFERENCES companies(id) ON DELETE RESTRICT,
    stock_item_id bigint NOT NULL UNIQUE REFERENCES stock_items(id) ON DELETE CASCADE,
    file_name varchar(255) NOT NULL,
    content_type varchar(80) NOT NULL,
    content bytea NOT NULL,
    created_at_utc timestamptz NOT NULL,
    modified_at_utc timestamptz NOT NULL,
    created_by varchar(100) NOT NULL,
    modified_by varchar(100) NOT NULL,
    concurrency_token varchar(64) NOT NULL,
    CONSTRAINT ck_stock_item_design_photo_content CHECK (octet_length(content) > 0 AND octet_length(content) <= 5242880)
);
CREATE INDEX ix_stock_item_design_photo_company ON stock_item_design_photos(company_id, stock_item_id);
