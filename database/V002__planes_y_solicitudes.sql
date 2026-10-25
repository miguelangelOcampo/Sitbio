-- =====================================================================
-- SITBIO - Migración V002
-- Planes del servicio en tenant (RN-009, DT-009) y solicitudes del
-- servicio (RF-029, DT-010). Ejecutar después del script 01 (ADR-005).
-- =====================================================================
SET search_path TO sitbio, public;

-- ---------------------------------------------------------------------
-- 1. Límites del plan contratado (DOC-01 Tabla 4). Valores por defecto
--    del plan Clínica; el proveedor (ST-10) los ajusta al aprovisionar.
-- ---------------------------------------------------------------------
ALTER TABLE tenant
    ADD COLUMN plan                  VARCHAR(20) NOT NULL DEFAULT 'Clínica',
    ADD COLUMN max_usuarios_gestion  INTEGER     NOT NULL DEFAULT 7,
    ADD COLUMN max_equipos           INTEGER     NOT NULL DEFAULT 1000,
    ADD COLUMN max_almacenamiento_gb INTEGER     NOT NULL DEFAULT 10,
    ADD COLUMN vigente_hasta         DATE;

ALTER TABLE tenant
    ADD CONSTRAINT ck_tenant_plan CHECK (plan IN ('Clínica', 'Hospital', 'Red de Salud')),
    ADD CONSTRAINT ck_tenant_limites CHECK (max_usuarios_gestion > 0 AND max_equipos > 0 AND max_almacenamiento_gb > 0);

COMMENT ON COLUMN tenant.plan IS 'Plan contratado (RN-009). Solo cambia la capacidad, no las funciones.';
COMMENT ON COLUMN tenant.max_usuarios_gestion IS 'Usuarios activos de AC-01, AC-02, AC-03, AC-05 y AC-06. AC-04 y AC-07 no cuentan.';
COMMENT ON COLUMN tenant.max_equipos IS 'Equipos activos; los dados de baja no cuentan.';

-- ---------------------------------------------------------------------
-- 2. Solicitudes de demostración, cotización o cambio de plan (RF-029).
--    Las administra el proveedor (ST-10); no pertenecen a un tenant
--    hasta que la institución es cliente (tenant_id opcional).
-- ---------------------------------------------------------------------
CREATE TABLE solicitud_servicio (
    id_solicitud     UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    codigo           VARCHAR(20)  NOT NULL,
    tipo             VARCHAR(20)  NOT NULL,
    tenant_id        UUID         REFERENCES tenant (id_tenant),
    institucion      VARCHAR(150) NOT NULL,
    nit              VARCHAR(20)  NOT NULL,
    contacto         VARCHAR(120) NOT NULL,
    correo           VARCHAR(150) NOT NULL,
    telefono         VARCHAR(30),
    plan_solicitado  VARCHAR(20),
    mensaje          VARCHAR(1000),
    estado           VARCHAR(20)  NOT NULL DEFAULT 'recibida',
    fecha_creacion   TIMESTAMPTZ  NOT NULL DEFAULT now(),
    CONSTRAINT uq_solicitud_codigo UNIQUE (codigo),
    CONSTRAINT ck_solicitud_tipo CHECK (tipo IN ('demostracion', 'cotizacion', 'cambio_plan')),
    CONSTRAINT ck_solicitud_plan CHECK (plan_solicitado IS NULL OR plan_solicitado IN ('Clínica', 'Hospital', 'Red de Salud')),
    CONSTRAINT ck_solicitud_estado CHECK (estado IN ('recibida', 'en_gestion', 'cerrada')),
    CONSTRAINT ck_solicitud_cambio_con_tenant CHECK (tipo <> 'cambio_plan' OR tenant_id IS NOT NULL)
);

CREATE INDEX ix_solicitud_estado ON solicitud_servicio (estado, fecha_creacion DESC);

COMMENT ON TABLE solicitud_servicio IS 'Solicitudes de demostración, cotización o cambio de plan (RF-029, MSG-021).';
