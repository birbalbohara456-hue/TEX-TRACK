-- Reference cost/sale price fields on Stock Items. Purely additive, nullable -
-- display and future Purchase/Sale rate-prefill groundwork only. Not a
-- valuation source: actual stock value continues to come from posted
-- inward/outward movements, never from this reference field.
ALTER TABLE stock_items
    ADD COLUMN cost_price numeric(19,4) NULL,
    ADD COLUMN sale_price numeric(19,4) NULL;

COMMENT ON COLUMN stock_items.cost_price IS
    'Reference cost price for display and future Purchase rate-prefill. Not a valuation source.';
COMMENT ON COLUMN stock_items.sale_price IS
    'Reference sale price for display and future Sales rate-prefill. Not a valuation source.';
