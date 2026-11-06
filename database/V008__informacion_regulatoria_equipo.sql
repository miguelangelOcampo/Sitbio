-- =====================================================================
-- SITBIO - Migración V008
-- Información regulatoria del equipo (RF-030, RN-010), último preventivo y
-- fotografía (RF-031), ajustes del 04/11/2026:
--   * Autorización sanitaria INVIMA: registro sanitario o permiso de
--     comercialización (equipo biomédico de tecnología controlada),
--     número y vigencia (Decreto 4725 de 2005).
--   * Clase de riesgo I, IIa, IIb o III.
--   * Si el equipo requiere calibración y cada cuántos meses. Es un dato
--     independiente de la clase: un tensiómetro es clase I y se calibra;
--     un estetoscopio es clase I y no se calibra.
--   * Último preventivo conocido en plan_preventivo (ultima_ejecucion).
--   * Fotografía opcional del equipo (foto_documento_id, RF-031).
-- tipo_equipo guarda los valores por defecto; equipo_biomedico, los del
-- equipo (el Ingeniero Biomédico los puede ajustar).
-- Las columnas de autorización y clase admiten NULL para los equipos ya
-- registrados; la aplicación las exige al crear o editar (RN-010).
-- Probada en PostgreSQL 16 sobre 01, V002 a V006 (04/11/2026).
-- Ejecutar después de V006 (V007 es el script de MongoDB). ADR-005.
-- =====================================================================
SET search_path TO sitbio, public;

-- Valores por defecto del tipo de equipo (catálogo técnico, AC-02)
ALTER TABLE tipo_equipo
    ADD COLUMN clase_riesgo_defecto  VARCHAR(3) NULL,
    ADD COLUMN requiere_calibracion  BOOLEAN    NOT NULL DEFAULT TRUE,
    ADD COLUMN periodicidad_calibracion_meses SMALLINT NULL DEFAULT 12,
    ADD CONSTRAINT ck_tipo_equipo_clase CHECK (clase_riesgo_defecto IN ('I', 'IIa', 'IIb', 'III')),
    ADD CONSTRAINT ck_tipo_equipo_calibracion CHECK (
        (requiere_calibracion AND periodicidad_calibracion_meses IS NOT NULL
         AND periodicidad_calibracion_meses BETWEEN 1 AND 60)
        OR (NOT requiere_calibracion AND periodicidad_calibracion_meses IS NULL));

-- Datos del equipo
ALTER TABLE equipo_biomedico
    ADD COLUMN tipo_autorizacion VARCHAR(30) NULL,
    ADD COLUMN registro_invima   VARCHAR(40) NULL,
    ADD COLUMN vence_registro    DATE        NULL,
    ADD COLUMN clase_riesgo      VARCHAR(3)  NULL,
    ADD COLUMN requiere_calibracion BOOLEAN  NOT NULL DEFAULT TRUE,
    ADD COLUMN periodicidad_calibracion_meses SMALLINT NULL DEFAULT 12,
    ADD CONSTRAINT ck_equipo_tipo_autorizacion CHECK (tipo_autorizacion IN ('registro_sanitario', 'permiso_comercializacion')),
    ADD CONSTRAINT ck_equipo_clase CHECK (clase_riesgo IN ('I', 'IIa', 'IIb', 'III')),
    ADD CONSTRAINT ck_equipo_calibracion CHECK (
        (requiere_calibracion AND periodicidad_calibracion_meses IS NOT NULL
         AND periodicidad_calibracion_meses BETWEEN 1 AND 60)
        OR (NOT requiere_calibracion AND periodicidad_calibracion_meses IS NULL));

-- Último preventivo conocido (HU-002, CA-HU-002-06): al registrar un equipo
-- que la IPS ya tenía, la próxima fecha se calcula como última + periodicidad.
-- Puede quedar vacío si no se conoce (equipo nuevo o sin registros).
ALTER TABLE plan_preventivo
    ADD COLUMN ultima_ejecucion DATE NULL,
    ADD CONSTRAINT ck_plan_ultima_antes_proxima CHECK (ultima_ejecucion IS NULL OR ultima_ejecucion < proxima_fecha);

COMMENT ON COLUMN plan_preventivo.ultima_ejecucion IS 'Último preventivo conocido; la próxima fecha = última + periodicidad (puede ser pasada: el plan queda vencido).';

-- Fotografía del equipo (RF-031): es un documento_equipo de tipo «Fotografía»
-- (vista de 512 px; la miniatura de 128 px se guarda junto a ella con el sufijo
-- -128). Así cuenta en el almacenamiento del plan (RN-009) como los demás adjuntos.
ALTER TABLE equipo_biomedico
    ADD COLUMN foto_documento_id UUID NULL,
    ADD CONSTRAINT fk_equipo_foto FOREIGN KEY (foto_documento_id)
        REFERENCES documento_equipo (id_documento) ON UPDATE CASCADE ON DELETE SET NULL;

COMMENT ON COLUMN equipo_biomedico.foto_documento_id IS 'Fotografía opcional (RF-031); el cliente la reduce a 128 y 512 px antes de subirla.';

-- Avisos de registro vencido o por vencer (RN-010)
CREATE INDEX ix_equipo_vence_registro ON equipo_biomedico (tenant_id, vence_registro)
    WHERE fecha_baja IS NULL;

COMMENT ON COLUMN equipo_biomedico.registro_invima IS 'Número del registro sanitario o del permiso de comercialización INVIMA (RF-030).';
COMMENT ON COLUMN equipo_biomedico.clase_riesgo IS 'Clase de riesgo según el Decreto 4725 de 2005: I, IIa, IIb o III.';
COMMENT ON COLUMN equipo_biomedico.requiere_calibracion IS 'Falso si el equipo no realiza mediciones (p. ej., estetoscopio): no se programan calibraciones y RN-007 no aplica (RN-010).';
COMMENT ON COLUMN tipo_equipo.clase_riesgo_defecto IS 'Clase sugerida al registrar un equipo de este tipo; la del equipo puede diferir.';
