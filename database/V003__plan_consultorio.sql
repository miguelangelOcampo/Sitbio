-- =====================================================================
-- SITBIO - Migración V003
-- Plan Consultorio para la baja complejidad y mayor almacenamiento en los
-- planes Clínica (25 GB) y Hospital (100 GB). DOC-01 Tabla 4, RN-009.
-- Ejecutar después de V002 (ADR-005: no se edita V002).
-- =====================================================================
SET search_path TO sitbio, public;

-- 1. Aceptar el plan Consultorio
ALTER TABLE tenant DROP CONSTRAINT ck_tenant_plan;
ALTER TABLE tenant
    ADD CONSTRAINT ck_tenant_plan CHECK (plan IN ('Consultorio', 'Clínica', 'Hospital', 'Red de Salud'));

ALTER TABLE solicitud_servicio DROP CONSTRAINT ck_solicitud_plan;
ALTER TABLE solicitud_servicio
    ADD CONSTRAINT ck_solicitud_plan CHECK (plan_solicitado IS NULL OR plan_solicitado IN ('Consultorio', 'Clínica', 'Hospital', 'Red de Salud'));

-- 2. Nuevo almacenamiento por defecto del plan Clínica
ALTER TABLE tenant ALTER COLUMN max_almacenamiento_gb SET DEFAULT 25;

-- 3. Ajustar los tenants existentes que tenían los valores anteriores
UPDATE tenant SET max_almacenamiento_gb = 25  WHERE plan = 'Clínica'  AND max_almacenamiento_gb = 10;
UPDATE tenant SET max_almacenamiento_gb = 100 WHERE plan = 'Hospital' AND max_almacenamiento_gb = 50;

COMMENT ON COLUMN tenant.plan IS 'Plan contratado (RN-009): Consultorio, Clínica, Hospital o Red de Salud. Solo cambia la capacidad, no las funciones.';
