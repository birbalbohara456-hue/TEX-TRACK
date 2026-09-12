-- TexTrack ERP v0.5 Build 2.1
-- Restore the accepted migration that permits Material Out cancellation movement names.
-- Safe for databases where movement_kind is still varchar(30).

ALTER TABLE stock_movements
    ALTER COLUMN movement_kind TYPE varchar(50);
