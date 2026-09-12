-- Migration 015: Fix Material In Movement Kinds (MIG-CLEAN-001)

-- Drop the restrictive check constraint that is blocking Material In saves
ALTER TABLE stock_movements DROP CONSTRAINT IF EXISTS ck_stock_movement_kind;