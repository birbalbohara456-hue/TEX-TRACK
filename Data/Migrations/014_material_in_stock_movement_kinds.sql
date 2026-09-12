-- TexTrack ERP v0.5 Build 2.6B
-- Material In posting fails PostgreSQL constraint ck_stock_movement_kind (23514) because
-- migration 013 added Material In tables/columns but never extended the movement-kind
-- allow-list. MaterialInRepository.cs posts two kinds not yet accepted by the database:
--   - "MaterialInConsumption"   (negative, raw-material consumption at consumption godown)
--   - "MaterialInFinishedGoods" (positive, finished-goods receipt at receiving godown)
-- Forward-only: drop and recreate the constraint, keeping every previously accepted value.

ALTER TABLE stock_movements DROP CONSTRAINT IF EXISTS ck_stock_movement_kind;
ALTER TABLE stock_movements ADD CONSTRAINT ck_stock_movement_kind CHECK (movement_kind IN (
    'MaterialOutSource',
    'MaterialOutDestination',
    'MaterialOutCancellationSource',
    'MaterialOutCancellationDestination',
    'MaterialInConsumption',
    'MaterialInFinishedGoods'
));
