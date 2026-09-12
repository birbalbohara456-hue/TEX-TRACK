-- Configurable Stock Item fields and transaction-safe named photo slots.

CREATE TABLE stock_item_field_definitions
(
    id bigserial PRIMARY KEY,
    company_id bigint NOT NULL REFERENCES companies(id) ON DELETE RESTRICT,
    name varchar(120) NOT NULL,
    name_normalized varchar(120) NOT NULL,
    field_type varchar(20) NOT NULL DEFAULT 'Text',
    display_order integer NOT NULL DEFAULT 100,
    is_required boolean NOT NULL DEFAULT false,
    allow_multiple boolean NOT NULL DEFAULT false,
    options_text varchar(2000) NOT NULL DEFAULT '',
    is_active boolean NOT NULL DEFAULT true,
    created_at_utc timestamptz NOT NULL,
    modified_at_utc timestamptz NOT NULL,
    created_by varchar(100) NOT NULL,
    modified_by varchar(100) NOT NULL,
    concurrency_token varchar(64) NOT NULL,
    CONSTRAINT ck_stock_item_field_type CHECK (field_type IN ('Text','Number','Date','YesNo','Choice','Photo')),
    CONSTRAINT ck_stock_item_field_order CHECK (display_order BETWEEN 1 AND 9999),
    CONSTRAINT uq_stock_item_field_name UNIQUE (company_id, name_normalized)
);
CREATE INDEX ix_stock_item_field_active_order ON stock_item_field_definitions(company_id, is_active, display_order);

CREATE TABLE stock_item_field_values
(
    id bigserial PRIMARY KEY,
    company_id bigint NOT NULL REFERENCES companies(id) ON DELETE RESTRICT,
    stock_item_id bigint NOT NULL REFERENCES stock_items(id) ON DELETE CASCADE,
    field_definition_id bigint NOT NULL REFERENCES stock_item_field_definitions(id) ON DELETE RESTRICT,
    value varchar(4000) NOT NULL DEFAULT '',
    created_at_utc timestamptz NOT NULL,
    modified_at_utc timestamptz NOT NULL,
    created_by varchar(100) NOT NULL,
    modified_by varchar(100) NOT NULL,
    concurrency_token varchar(64) NOT NULL,
    CONSTRAINT uq_stock_item_field_value UNIQUE (stock_item_id, field_definition_id)
);
CREATE INDEX ix_stock_item_field_value_search ON stock_item_field_values(company_id, field_definition_id, value);

CREATE TABLE stock_item_photos
(
    id bigserial PRIMARY KEY,
    company_id bigint NOT NULL REFERENCES companies(id) ON DELETE RESTRICT,
    stock_item_id bigint NOT NULL REFERENCES stock_items(id) ON DELETE CASCADE,
    field_definition_id bigint NOT NULL REFERENCES stock_item_field_definitions(id) ON DELETE RESTRICT,
    file_name varchar(255) NOT NULL,
    content_type varchar(80) NOT NULL,
    content bytea NOT NULL,
    display_order integer NOT NULL DEFAULT 1,
    created_at_utc timestamptz NOT NULL,
    modified_at_utc timestamptz NOT NULL,
    created_by varchar(100) NOT NULL,
    modified_by varchar(100) NOT NULL,
    concurrency_token varchar(64) NOT NULL,
    CONSTRAINT ck_stock_item_photo_order CHECK (display_order > 0),
    CONSTRAINT ck_stock_item_photo_content CHECK (octet_length(content) > 0 AND octet_length(content) <= 5242880),
    CONSTRAINT uq_stock_item_photo_order UNIQUE (stock_item_id, field_definition_id, display_order)
);
CREATE INDEX ix_stock_item_photo_field ON stock_item_photos(company_id, field_definition_id, stock_item_id);

INSERT INTO stock_item_field_definitions
    (company_id, name, name_normalized, field_type, display_order, is_required, allow_multiple, options_text,
     is_active, created_at_utc, modified_at_utc, created_by, modified_by, concurrency_token)
SELECT c.id, seed.name, upper(seed.name), seed.field_type, seed.display_order, false, seed.allow_multiple, '',
       true, now(), now(), 'System', 'System', md5(random()::text || clock_timestamp()::text)
FROM companies c
CROSS JOIN (VALUES
    ('Fabric', 'Text', 100, false),
    ('Pattern', 'Text', 110, false),
    ('Collection', 'Text', 120, false),
    ('Front Photo', 'Photo', 200, false),
    ('Back Photo', 'Photo', 210, false),
    ('Additional Photos', 'Photo', 220, true)
) AS seed(name, field_type, display_order, allow_multiple)
ON CONFLICT (company_id, name_normalized) DO NOTHING;
