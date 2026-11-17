-- =====================================================================
-- SITBIO - Migración V010
-- Documentos adjuntos del equipo (HU-015, RF-015, API-EQ-DOC).
--   * Desactivación lógica: el documento deja de mostrarse en la hoja de
--     vida, pero el registro y el archivo se conservan para auditoría.
--     Por eso su tamaño sigue contando en el almacenamiento del plan
--     (RN-009).
--   * Quién lo desactivó y cuándo; el detalle completo queda en la
--     bitácora (acción ELIMINAR).
-- Probada en PostgreSQL 16 sobre 01, V002 a V009 (17/11/2026).
-- Ejecutar después de V009. ADR-005.
-- =====================================================================
SET search_path TO sitbio, public;

ALTER TABLE documento_equipo
    ADD COLUMN estado_activo        BOOLEAN     NOT NULL DEFAULT TRUE,
    ADD COLUMN fecha_desactivacion  TIMESTAMPTZ NULL,
    ADD COLUMN desactivado_por_id   UUID        NULL,
    ADD CONSTRAINT fk_documento_desactivado_por FOREIGN KEY (desactivado_por_id)
        REFERENCES usuario (id_usuario) ON UPDATE CASCADE,
    ADD CONSTRAINT ck_documento_desactivacion CHECK (
        (estado_activo AND fecha_desactivacion IS NULL AND desactivado_por_id IS NULL)
        OR (NOT estado_activo AND fecha_desactivacion IS NOT NULL AND desactivado_por_id IS NOT NULL));

COMMENT ON COLUMN documento_equipo.estado_activo IS 'Falso = desactivado (HU-015): no se muestra en la hoja de vida; el archivo se conserva y sigue contando en el almacenamiento del plan.';
COMMENT ON COLUMN documento_equipo.desactivado_por_id IS 'Usuario AC-02 que desactivó el documento (sale del token).';
