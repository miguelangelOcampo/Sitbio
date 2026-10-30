-- =====================================================================
-- SITBIO - Migración V005
-- Correo único en todo el sistema (HU-001). El inicio de sesión (UI-001)
-- pide solo correo y contraseña, y el cliente es el mismo para todas las
-- instituciones (ADR-007): el tenant se deduce del correo en el servidor.
-- Se conserva uq_usuario_tenant_correo del script 01 (ADR-005).
-- Ejecutar después de V004.
-- =====================================================================
SET search_path TO sitbio, public;

-- Sin distinguir mayúsculas: Ana@x.co y ana@x.co son el mismo correo
CREATE UNIQUE INDEX uq_usuario_correo_global ON usuario (lower(correo));

COMMENT ON COLUMN usuario.correo IS 'Correo de acceso; único en todo el sistema sin distinguir mayúsculas (V005). La aplicación lo guarda en minúsculas.';
