-- =====================================================================
-- SITBIO - Migración V009
-- Catálogo de tipos de falla (HU-025, RF-025; ajuste de roles del
-- 01/11/2026): el Ingeniero Biomédico (AC-02) gestiona tipos de equipo,
-- proveedores, tipos de falla y ubicaciones. Los tres primeros ya tenían
-- tabla en el script 01 (DAT-CATALOGOS); esta migración agrega la cuarta.
--   * Cada institución arma su propia lista: no hay valores iniciales.
--   * Los valores no se borran, se inactivan (CA-HU-025-02).
--   * tiquete.tipo_falla sigue siendo texto: el tiquete guarda el nombre
--     elegido, así que inactivar o renombrar un tipo no cambia los
--     tiquetes anteriores.
--   * Nombre único por institución sin distinguir mayúsculas.
-- Probada en PostgreSQL 16 sobre 01, V002 a V008 (11/11/2026).
-- Ejecutar después de V008. ADR-005.
-- =====================================================================
SET search_path TO sitbio, public;

CREATE TABLE tipo_falla (
    id_tipo_falla UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id     UUID NOT NULL,
    nombre        VARCHAR(60) NOT NULL,   -- Alarma o mensaje de error, No enciende, Eléctrica, ...
    estado_activo BOOLEAN NOT NULL DEFAULT TRUE,
    CONSTRAINT fk_tipo_falla_tenant FOREIGN KEY (tenant_id)
        REFERENCES tenant (id_tenant) ON UPDATE CASCADE
);

CREATE UNIQUE INDEX uq_tipo_falla_tenant_nombre ON tipo_falla (tenant_id, lower(nombre));

COMMENT ON TABLE tipo_falla IS 'Catálogo de tipos de falla por institución (HU-025); lo gestiona AC-02 y alimenta el reporte de fallas (HU-003).';
COMMENT ON COLUMN tipo_falla.nombre IS 'Mismo largo que tiquete.tipo_falla, que guarda el nombre elegido (VARCHAR 60).';
