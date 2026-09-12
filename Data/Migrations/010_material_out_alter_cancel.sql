-- TexTrack ERP v0.5 Build 2.0
-- Material Out alteration/deletion/cancellation stock reversal support.

ALTER TABLE stock_movements DROP CONSTRAINT IF EXISTS ck_stock_movement_kind;
ALTER TABLE stock_movements ADD CONSTRAINT ck_stock_movement_kind CHECK (movement_kind IN (
    'MaterialOutSource',
    'MaterialOutDestination',
    'MaterialOutCancellationSource',
    'MaterialOutCancellationDestination'
));

CREATE INDEX IF NOT EXISTS ix_stock_movements_voucher_kind
    ON stock_movements(voucher_id, movement_kind, id);
