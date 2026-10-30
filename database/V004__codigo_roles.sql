-- =====================================================================
-- SITBIO - Migración V004
-- Código estable de rol (AC-01 a AC-07) y nombres según el DOC-01 (Tabla 3)
-- y la decisión del 24/10/2026 sobre AC-01. Crea AC-07 (Ente Regulador).
-- Ejecutar después de V003 (ADR-005: no se edita el script 01).
-- =====================================================================
SET search_path TO sitbio, public;

ALTER TABLE rol ADD COLUMN codigo VARCHAR(5);

-- Los nombres pasan por un valor temporal para no chocar con UNIQUE(nombre)
UPDATE rol SET nombre = 'tmp-' || id_rol;

UPDATE rol SET codigo = 'AC-01', nombre = 'Administrador del Sistema',
       descripcion = 'Administrador de TI de su institución: usuarios, roles, catálogos y configuración (AC-01).' WHERE id_rol = 1;
UPDATE rol SET codigo = 'AC-02', nombre = 'Ingeniero Biomédico',
       descripcion = 'Gestiona inventario, tiquetes, órdenes, planes preventivos y calibraciones (AC-02).' WHERE id_rol = 2;
UPDATE rol SET codigo = 'AC-03', nombre = 'Técnico de Mantenimiento',
       descripcion = 'Ejecuta y cierra órdenes de trabajo e intervenciones (AC-03).' WHERE id_rol = 3;
UPDATE rol SET codigo = 'AC-04', nombre = 'Personal Clínico',
       descripcion = 'Reporta fallas mediante tiquetes y consulta su seguimiento (AC-04).' WHERE id_rol = 4;
UPDATE rol SET codigo = 'AC-05', nombre = 'Director/Gerente Biomédico',
       descripcion = 'Consulta indicadores, reportes y analítica (AC-05).' WHERE id_rol = 5;
UPDATE rol SET codigo = 'AC-06', nombre = 'Directiva Institucional',
       descripcion = 'Consulta indicadores para la toma de decisiones; solo lectura (AC-06).' WHERE id_rol = 6;

INSERT INTO rol (codigo, nombre, descripcion)
VALUES ('AC-07', 'Ente Regulador / Auditor', 'Acceso temporal de solo lectura para auditoría externa (AC-07).');

ALTER TABLE rol ALTER COLUMN codigo SET NOT NULL;
ALTER TABLE rol ADD CONSTRAINT uq_rol_codigo UNIQUE (codigo);
ALTER TABLE rol ADD CONSTRAINT ck_rol_codigo CHECK (codigo ~ '^AC-0[1-7]$');

COMMENT ON TABLE rol IS 'Roles del modelo RBAC (AC-01 a AC-07). La aplicación usa rol.codigo, nunca id_rol.';
COMMENT ON COLUMN rol.codigo IS 'Código estable del rol (DOC-01 Tabla 3); coincide con CodigosRol del dominio.';
