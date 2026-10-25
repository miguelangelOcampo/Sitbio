-- =====================================================================
-- SITBIO - Sistema de Información para la Trazabilidad y Gestión de
--          Mantenimientos Biomédicos
-- Script 01: Creación de base de datos y esquema relacional (SQL)
-- Motor:    PostgreSQL 16
-- Documento de referencia: DER oficial (Descripcion_ERD_SITBIO.txt)
--                          módulos 1 a 6
-- Autor:    Proyecto SITBIO - SENA CTMA Regional Antioquia
-- =====================================================================
-- ESTRUCTURA: 6 módulos funcionales
--   1. Seguridad y arquitectura multi-tenant (TENANT, ROL, USUARIO, SESION_TOKEN)
--   2. Catálogos maestros (UBICACION, TIPO_EQUIPO, ESTADO_EQUIPO, PROVEEDOR)
--   3. Inventario y hoja de vida (EQUIPO_BIOMEDICO, DOCUMENTO_EQUIPO,
--      HISTORIAL_ESTADO_EQUIPO)
--   4. Peticiones y tiquetes (TIQUETE, HISTORIAL_TIQUETE)
--   5. Tareas y mantenimiento (PLAN_PREVENTIVO, ORDEN_TRABAJO)
--   6. Calibración (CALIBRACION)
-- =====================================================================

-- ---------------------------------------------------------------------
-- 1. CREACIÓN DE LA BASE DE DATOS
-- (Ejecutar conectado al usuario postgres)
-- ---------------------------------------------------------------------
DROP DATABASE IF EXISTS sitbio_dev;
CREATE DATABASE sitbio_dev
    WITH
    OWNER = postgres
    ENCODING = 'UTF8'
    LC_COLLATE = 'es_CO.UTF-8'
    LC_CTYPE = 'es_CO.UTF-8'
    TABLESPACE = pg_default
    CONNECTION LIMIT = -1;

-- Conectarse a sitbio_dev antes de ejecutar el resto del script.
\c sitbio_dev

-- ---------------------------------------------------------------------
-- 2. EXTENSIONES Y ESQUEMA
-- ---------------------------------------------------------------------
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";   -- UUIDs para claves primarias
CREATE EXTENSION IF NOT EXISTS "pgcrypto";

CREATE SCHEMA IF NOT EXISTS sitbio;
SET search_path TO sitbio, public;

-- =====================================================================
-- MÓDULO 1: SEGURIDAD Y ARQUITECTURA MULTI-TENANT
-- (Sección 2 del ERD)
-- =====================================================================

-- ---------------------------------------------------------------------
-- 1.1 TENANT
-- Raíz del aislamiento multi-tenant SaaS. Cada institución (IPS) es una
-- unidad de separación lógica de los datos.
-- Atributo clave: dias_anticipacion_alerta (anticipación de alertas).
-- ---------------------------------------------------------------------
CREATE TABLE tenant (
    id_tenant                 UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    nombre                    VARCHAR(150) NOT NULL,   -- Nombre de la IPS
    nit                       VARCHAR(20)  NOT NULL,
    dias_anticipacion_alerta  SMALLINT     NOT NULL DEFAULT 15,  -- Alertas de preventivos/calibraciones
    estado_activo             BOOLEAN      NOT NULL DEFAULT TRUE,
    fecha_creacion            TIMESTAMPTZ  NOT NULL DEFAULT now(),
    CONSTRAINT uq_tenant_nit UNIQUE (nit),
    CONSTRAINT ck_tenant_dias_alerta CHECK (dias_anticipacion_alerta >= 0)
);

COMMENT ON TABLE tenant IS 'Institución Prestadora de Servicios (cliente SaaS). Raíz del aislamiento multi-tenant.';

-- ---------------------------------------------------------------------
-- 1.2 ROL
-- Roles disponibles para asignación de permisos. Global (sin tenant_id).
-- PK de tipo entero según el ERD (sección 9.1).
-- ---------------------------------------------------------------------
CREATE TABLE rol (
    id_rol      SERIAL PRIMARY KEY,
    nombre      VARCHAR(50)  NOT NULL UNIQUE,
    descripcion VARCHAR(200) NOT NULL
);

COMMENT ON TABLE rol IS 'Roles del modelo RBAC (AC-01 a AC-06).';

-- ---------------------------------------------------------------------
-- 1.3 USUARIO
-- Restricción de unicidad compuesta: (tenant_id, correo) -> el correo es
-- único dentro de cada institución.
-- RN-006: intentos_fallidos + estado_bloqueo (bloqueo a los 5 intentos).
-- Datos sensibles - Ley 1581/2012 (Habeas Data).
-- ---------------------------------------------------------------------
CREATE TABLE usuario (
    id_usuario                    UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id                     UUID NOT NULL,
    rol_id                        INT  NOT NULL,
    nombres                       VARCHAR(100) NOT NULL,
    apellidos                     VARCHAR(100) NOT NULL,
    correo                        VARCHAR(100) NOT NULL,
    contrasena_hash               VARCHAR(100) NOT NULL,   -- BCrypt, costo >= 11
    telefono                      VARCHAR(30),
    intentos_fallidos             SMALLINT NOT NULL DEFAULT 0,
    estado_bloqueo                BOOLEAN  NOT NULL DEFAULT FALSE,
    cambio_contrasena_obligatorio BOOLEAN  NOT NULL DEFAULT FALSE,
    estado_activo                 BOOLEAN  NOT NULL DEFAULT TRUE,
    ultimo_acceso                 TIMESTAMPTZ NULL,
    fecha_creacion                TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT fk_usuario_tenant FOREIGN KEY (tenant_id)
        REFERENCES tenant (id_tenant) ON UPDATE CASCADE,
    CONSTRAINT fk_usuario_rol FOREIGN KEY (rol_id)
        REFERENCES rol (id_rol) ON UPDATE CASCADE,
    CONSTRAINT uq_usuario_tenant_correo UNIQUE (tenant_id, correo),
    CONSTRAINT ck_usuario_intentos CHECK (intentos_fallidos >= 0)
);

CREATE INDEX ix_usuario_tenant ON usuario (tenant_id);
CREATE INDEX ix_usuario_rol    ON usuario (rol_id);

-- ---------------------------------------------------------------------
-- 1.4 SESION_TOKEN
-- Gestión de sesiones JWT: trazabilidad de emisión, expiración y
-- revocación de tokens (jti = identificador único del token).
-- ---------------------------------------------------------------------
CREATE TABLE sesion_token (
    id_sesion        UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id        UUID NOT NULL,
    usuario_id       UUID NOT NULL,
    jti              VARCHAR(36) NOT NULL,   -- JWT ID único
    fecha_emision    TIMESTAMPTZ NOT NULL,
    fecha_expiracion TIMESTAMPTZ NOT NULL,
    fecha_revocacion TIMESTAMPTZ NULL,
    motivo_revocacion VARCHAR(200) NULL,
    CONSTRAINT uq_sesion_jti UNIQUE (jti),
    CONSTRAINT fk_sesion_tenant FOREIGN KEY (tenant_id)
        REFERENCES tenant (id_tenant) ON UPDATE CASCADE,
    CONSTRAINT fk_sesion_usuario FOREIGN KEY (usuario_id)
        REFERENCES usuario (id_usuario) ON UPDATE CASCADE,
    CONSTRAINT ck_sesion_expiracion CHECK (fecha_expiracion > fecha_emision)
);

CREATE INDEX ix_sesion_usuario ON sesion_token (usuario_id);
CREATE INDEX ix_sesion_vigentes ON sesion_token (usuario_id)
    WHERE fecha_revocacion IS NULL;

-- =====================================================================
-- MÓDULO 2: CATÁLOGOS MAESTROS
-- (Sección 3 del ERD)
-- Clasifican y organizan los equipos biomédicos.
-- =====================================================================

-- ---------------------------------------------------------------------
-- 2.1 UBICACION
-- Ubicaciones físicas de los equipos (áreas asistenciales, laboratorios,
-- departamentos técnicos).
-- ---------------------------------------------------------------------
CREATE TABLE ubicacion (
    id_ubicacion  UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id     UUID NOT NULL,
    nombre        VARCHAR(120) NOT NULL,
    descripcion   VARCHAR(200),
    estado_activo BOOLEAN NOT NULL DEFAULT TRUE,
    CONSTRAINT fk_ubicacion_tenant FOREIGN KEY (tenant_id)
        REFERENCES tenant (id_tenant) ON UPDATE CASCADE,
    CONSTRAINT uq_ubicacion_tenant_nombre UNIQUE (tenant_id, nombre)
);

-- ---------------------------------------------------------------------
-- 2.2 TIPO_EQUIPO
-- Categorías de clasificación de los equipos biomédicos.
-- ---------------------------------------------------------------------
CREATE TABLE tipo_equipo (
    id_tipo_equipo UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id      UUID NOT NULL,
    nombre         VARCHAR(80) NOT NULL,   -- Monitor multiparamétrico, Bomba de infusión, ...
    descripcion    VARCHAR(200),
    estado_activo  BOOLEAN NOT NULL DEFAULT TRUE,
    CONSTRAINT fk_tipo_equipo_tenant FOREIGN KEY (tenant_id)
        REFERENCES tenant (id_tenant) ON UPDATE CASCADE,
    CONSTRAINT uq_tipo_equipo_tenant_nombre UNIQUE (tenant_id, nombre)
);

-- ---------------------------------------------------------------------
-- 2.3 ESTADO_EQUIPO
-- Catálogo de estados operativos/administrativos de un equipo.
-- NOTA (sección 3.3 del ERD): esta entidad NO tiene tenant_id.
-- PK de tipo SMALLINT según el ERD (sección 9.1).
-- Se relaciona con EQUIPO_BIOMEDICO.estado_equipo_id (estado actual) y
-- con HISTORIAL_ESTADO_EQUIPO (estado anterior / nuevo estado).
-- ---------------------------------------------------------------------
CREATE TABLE estado_equipo (
    id_estado     SMALLINT PRIMARY KEY,
    codigo        VARCHAR(20) NOT NULL UNIQUE,   -- OPERATIVO, EN_MANTENIMIENTO, ...
    nombre        VARCHAR(50) NOT NULL,
    descripcion   VARCHAR(200)
);

COMMENT ON TABLE estado_equipo IS 'Catálogo global de estados de equipo (sin tenant_id según ERD).';

-- ---------------------------------------------------------------------
-- 2.4 PROVEEDOR
-- Proveedores de adquisición, suministro o servicios (incluye
-- calibraciones).
-- ---------------------------------------------------------------------
CREATE TABLE proveedor (
    id_proveedor   UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id      UUID NOT NULL,
    nombre         VARCHAR(150) NOT NULL,
    nit            VARCHAR(20)  NOT NULL,
    tipo_proveedor VARCHAR(50)  NOT NULL,   -- Distribuidor, Servicio técnico, Laboratorio de calibración, ...
    nombre_contacto VARCHAR(100),
    correo         VARCHAR(100),
    telefono       VARCHAR(30),
    estado_activo  BOOLEAN NOT NULL DEFAULT TRUE,
    CONSTRAINT fk_proveedor_tenant FOREIGN KEY (tenant_id)
        REFERENCES tenant (id_tenant) ON UPDATE CASCADE,
    CONSTRAINT uq_proveedor_tenant_nit UNIQUE (tenant_id, nit)
);

-- =====================================================================
-- MÓDULO 3: INVENTARIO Y HOJA DE VIDA DE EQUIPOS BIOMÉDICOS
-- (Sección 4 del ERD)
-- =====================================================================

-- ---------------------------------------------------------------------
-- 3.1 EQUIPO_BIOMEDICO
-- Entidad central del modelo. Unicidad compuesta por institución:
--   (tenant_id, numero_serie) y (tenant_id, placa_inventario) -> RN-001.
-- Baja lógica conservando historial (fecha_baja) -> RF-014.
-- ---------------------------------------------------------------------
CREATE TABLE equipo_biomedico (
    id_equipo        UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id        UUID NOT NULL,
    tipo_equipo_id   UUID NOT NULL,
    ubicacion_id     UUID NOT NULL,
    estado_equipo_id SMALLINT NOT NULL,
    proveedor_id     UUID NULL,
    numero_serie     VARCHAR(50)  NOT NULL,
    placa_inventario VARCHAR(50)  NOT NULL,
    marca            VARCHAR(80)  NOT NULL,
    modelo           VARCHAR(80)  NOT NULL,
    fecha_adquisicion DATE NULL,
    fecha_baja       TIMESTAMPTZ  NULL,
    fecha_creacion   TIMESTAMPTZ  NOT NULL DEFAULT now(),
    CONSTRAINT fk_equipo_tenant FOREIGN KEY (tenant_id)
        REFERENCES tenant (id_tenant) ON UPDATE CASCADE,
    CONSTRAINT fk_equipo_tipo FOREIGN KEY (tipo_equipo_id)
        REFERENCES tipo_equipo (id_tipo_equipo) ON UPDATE CASCADE,
    CONSTRAINT fk_equipo_ubicacion FOREIGN KEY (ubicacion_id)
        REFERENCES ubicacion (id_ubicacion) ON UPDATE CASCADE,
    CONSTRAINT fk_equipo_estado FOREIGN KEY (estado_equipo_id)
        REFERENCES estado_equipo (id_estado) ON UPDATE CASCADE,
    CONSTRAINT fk_equipo_proveedor FOREIGN KEY (proveedor_id)
        REFERENCES proveedor (id_proveedor) ON UPDATE CASCADE,
    -- Restricciones de unicidad compuesta por institución (sección 9.3)
    CONSTRAINT uq_equipo_tenant_serie  UNIQUE (tenant_id, numero_serie),
    CONSTRAINT uq_equipo_tenant_placa  UNIQUE (tenant_id, placa_inventario)
);

CREATE INDEX ix_equipo_tenant   ON equipo_biomedico (tenant_id);
CREATE INDEX ix_equipo_tipo     ON equipo_biomedico (tenant_id, tipo_equipo_id);
CREATE INDEX ix_equipo_estado   ON equipo_biomedico (estado_equipo_id);

-- ---------------------------------------------------------------------
-- 3.2 DOCUMENTO_EQUIPO
-- Documentación técnica y administrativa de la hoja de vida de cada
-- equipo (fichas técnicas, certificados, manuales, informes).
-- Guarda la RUTA del archivo, no el binario.
-- ---------------------------------------------------------------------
CREATE TABLE documento_equipo (
    id_documento      UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id         UUID NOT NULL,
    equipo_id         UUID NOT NULL,
    subido_por_id     UUID NOT NULL,     -- Usuario responsable de la carga
    tipo_documento    VARCHAR(60) NOT NULL,  -- Ficha técnica, Certificado, Manual, Informe, ...
    nombre_archivo    VARCHAR(150) NOT NULL,
    ruta_almacenamiento VARCHAR(300) NOT NULL,  -- Referencia al archivo en disco/object storage
    tipo_mime         VARCHAR(80)  NOT NULL,   -- Ej: application/pdf
    tamano_bytes      BIGINT       NOT NULL CHECK (tamano_bytes >= 0),
    fecha_carga       TIMESTAMPTZ  NOT NULL DEFAULT now(),
    CONSTRAINT fk_documento_tenant FOREIGN KEY (tenant_id)
        REFERENCES tenant (id_tenant) ON UPDATE CASCADE,
    CONSTRAINT fk_documento_equipo FOREIGN KEY (equipo_id)
        REFERENCES equipo_biomedico (id_equipo) ON UPDATE CASCADE,
    CONSTRAINT fk_documento_usuario FOREIGN KEY (subido_por_id)
        REFERENCES usuario (id_usuario) ON UPDATE CASCADE
);

CREATE INDEX ix_documento_equipo ON documento_equipo (tenant_id, equipo_id);

-- ---------------------------------------------------------------------
-- 3.3 HISTORIAL_ESTADO_EQUIPO
-- Cambios de estado de un equipo a lo largo de su vida útil.
-- Diferencia clave (sección 4.3): EQUIPO_BIOMEDICO.estado_equipo_id es
-- el estado ACTUAL; esta tabla conserva la EVOLUCIÓN.
-- ---------------------------------------------------------------------
CREATE TABLE historial_estado_equipo (
    id_historial       UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id          UUID NOT NULL,
    equipo_id          UUID NOT NULL,
    estado_anterior_id SMALLINT NULL,   -- NULL en el primer registro (alta)
    nuevo_estado_id    SMALLINT NOT NULL,
    usuario_id         UUID NOT NULL,   -- Quién realizó el cambio
    motivo             VARCHAR(300),
    fecha_cambio       TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT fk_historial_estado_tenant FOREIGN KEY (tenant_id)
        REFERENCES tenant (id_tenant) ON UPDATE CASCADE,
    CONSTRAINT fk_historial_estado_equipo FOREIGN KEY (equipo_id)
        REFERENCES equipo_biomedico (id_equipo) ON UPDATE CASCADE,
    CONSTRAINT fk_historial_estado_anterior FOREIGN KEY (estado_anterior_id)
        REFERENCES estado_equipo (id_estado) ON UPDATE CASCADE,
    CONSTRAINT fk_historial_estado_nuevo FOREIGN KEY (nuevo_estado_id)
        REFERENCES estado_equipo (id_estado) ON UPDATE CASCADE,
    CONSTRAINT fk_historial_estado_usuario FOREIGN KEY (usuario_id)
        REFERENCES usuario (id_usuario) ON UPDATE CASCADE
);

CREATE INDEX ix_historial_estado_equipo
    ON historial_estado_equipo (tenant_id, equipo_id, fecha_cambio DESC);

-- =====================================================================
-- MÓDULO 4: PETICIONES Y TIQUETES
-- (Sección 5 del ERD)
-- =====================================================================

-- ---------------------------------------------------------------------
-- 4.1 TIQUETE
-- Solicitud / reporte de falla sobre un equipo biomédico.
-- Valores controlados (sección 9.4):
--   criticidad: critica | alta | media | baja
--   estado:     recibido | asignado | en_ejecucion | cerrado
-- Unicidad compuesta: (tenant_id, codigo).
-- ---------------------------------------------------------------------
CREATE TABLE tiquete (
    id_tiquete    UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id     UUID NOT NULL,
    equipo_id     UUID NOT NULL,
    reportante_id UUID NOT NULL,   -- Usuario que reporta la falla (AC-04)
    codigo        VARCHAR(20)  NOT NULL,   -- Ej: TQT-000001 (único por institución)
    tipo_falla    VARCHAR(60)  NOT NULL,   -- Eléctrica, Mecánica, Software, Consumible, ...
    descripcion   TEXT         NOT NULL,
    criticidad    VARCHAR(10)  NOT NULL CHECK (criticidad IN ('critica','alta','media','baja')),
    estado        VARCHAR(20)  NOT NULL DEFAULT 'recibido'
                  CHECK (estado IN ('recibido','asignado','en_ejecucion','cerrado')),
    fecha_reporte TIMESTAMPTZ  NOT NULL DEFAULT now(),
    fecha_cierre  TIMESTAMPTZ  NULL,
    CONSTRAINT uq_tiquete_tenant_codigo UNIQUE (tenant_id, codigo),
    CONSTRAINT fk_tiquete_tenant FOREIGN KEY (tenant_id)
        REFERENCES tenant (id_tenant) ON UPDATE CASCADE,
    CONSTRAINT fk_tiquete_equipo FOREIGN KEY (equipo_id)
        REFERENCES equipo_biomedico (id_equipo) ON UPDATE CASCADE,
    CONSTRAINT fk_tiquete_reportante FOREIGN KEY (reportante_id)
        REFERENCES usuario (id_usuario) ON UPDATE CASCADE,
    CONSTRAINT ck_tiquete_cierre CHECK ((estado = 'cerrado' AND fecha_cierre IS NOT NULL)
                                     OR (estado <> 'cerrado'))
);

CREATE INDEX ix_tiquete_tenant ON tiquete (tenant_id);
CREATE INDEX ix_tiquete_equipo ON tiquete (tenant_id, equipo_id);
CREATE INDEX ix_tiquete_estado ON tiquete (tenant_id, estado);

-- ---------------------------------------------------------------------
-- 4.2 HISTORIAL_TIQUETE
-- Secuencia histórica de la evolución de cada solicitud: cambios de
-- estado y comentarios.
-- ---------------------------------------------------------------------
CREATE TABLE historial_tiquete (
    id_historial     UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id        UUID NOT NULL,
    tiquete_id       UUID NOT NULL,
    usuario_id       UUID NOT NULL,   -- Quién registró el cambio
    estado_anterior  VARCHAR(20) NULL,
    nuevo_estado     VARCHAR(20) NOT NULL,
    comentario       VARCHAR(500),
    fecha_cambio     TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT fk_historial_tiquete_tenant FOREIGN KEY (tenant_id)
        REFERENCES tenant (id_tenant) ON UPDATE CASCADE,
    CONSTRAINT fk_historial_tiquete FOREIGN KEY (tiquete_id)
        REFERENCES tiquete (id_tiquete) ON UPDATE CASCADE,
    CONSTRAINT fk_historial_tiquete_usuario FOREIGN KEY (usuario_id)
        REFERENCES usuario (id_usuario) ON UPDATE CASCADE
);

CREATE INDEX ix_historial_tiquete
    ON historial_tiquete (tenant_id, tiquete_id, fecha_cambio DESC);

-- =====================================================================
-- MÓDULO 5: TAREAS Y MANTENIMIENTO
-- (Sección 6 del ERD)
-- =====================================================================

-- ---------------------------------------------------------------------
-- 5.1 PLAN_PREVENTIVO
-- Planes de mantenimiento preventivo por equipo: periodicidad en días y
-- próxima fecha programada. Pueden generar órdenes de trabajo.
-- ---------------------------------------------------------------------
CREATE TABLE plan_preventivo (
    id_plan           UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id         UUID NOT NULL,
    equipo_id         UUID NOT NULL,
    descripcion       VARCHAR(300) NOT NULL,   -- Descripción del mantenimiento
    periodicidad_dias SMALLINT     NOT NULL CHECK (periodicidad_dias > 0),
    proxima_fecha     DATE         NOT NULL,
    estado            VARCHAR(15)  NOT NULL DEFAULT 'activo'
                      CHECK (estado IN ('activo','suspendido','finalizado')),
    fecha_creacion    TIMESTAMPTZ  NOT NULL DEFAULT now(),
    CONSTRAINT fk_plan_tenant FOREIGN KEY (tenant_id)
        REFERENCES tenant (id_tenant) ON UPDATE CASCADE,
    CONSTRAINT fk_plan_equipo FOREIGN KEY (equipo_id)
        REFERENCES equipo_biomedico (id_equipo) ON UPDATE CASCADE
);

CREATE INDEX ix_plan_proxima_fecha ON plan_preventivo (estado, proxima_fecha);
CREATE INDEX ix_plan_equipo ON plan_preventivo (tenant_id, equipo_id);

-- ---------------------------------------------------------------------
-- 5.2 ORDEN_TRABAJO
-- Ejecución formal de una actividad de mantenimiento. Entidad con más
-- relaciones del modelo (sección 6.2):
--   - correctivo -> tiquete_id obligatorio
--   - preventivo -> plan_id obligatorio
--   - intervencion_id: enlace LÓGICO (VARCHAR(24)) con el _id BSON del
--     documento 'intervenciones' en MongoDB (sección 9.6). No es FK de
--     PostgreSQL; su integridad la gestiona la aplicación.
-- Trazabilidad: tecnico_id (ejecución), asignado_por_id (asignación),
-- aprobado_por_id (aprobación).
-- RN-004: una OT cerrada y firmada es INALTERABLE.
-- ---------------------------------------------------------------------
CREATE TABLE orden_trabajo (
    id_orden           UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id          UUID NOT NULL,
    equipo_id          UUID NOT NULL,
    tiquete_id         UUID NULL,      -- Origen correctivo
    plan_id            UUID NULL,      -- Origen preventivo
    tecnico_id         UUID NOT NULL,  -- Ejecuta la intervención (AC-03)
    asignado_por_id    UUID NULL,      -- Quién asignó la orden
    aprobado_por_id    UUID NULL,      -- Quién aprobó la orden
    tipo_mantenimiento VARCHAR(12) NOT NULL CHECK (tipo_mantenimiento IN ('preventivo','correctivo')),
    prioridad          VARCHAR(10) NOT NULL CHECK (prioridad IN ('critica','alta','media','baja')),
    estado             VARCHAR(20) NOT NULL DEFAULT 'asignada'
                       CHECK (estado IN ('asignada','en_ejecucion','pausada','cerrada','aprobada')),
    -- Control temporal
    fecha_programada DATE NULL,
    fecha_asignacion TIMESTAMPTZ NULL,
    fecha_inicio     TIMESTAMPTZ NULL,
    fecha_cierre     TIMESTAMPTZ NULL,
    fecha_aprobacion TIMESTAMPTZ NULL,
    fecha_creacion   TIMESTAMPTZ NOT NULL DEFAULT now(),
    -- Aprobación / firma
    firma_aprobacion VARCHAR(100) NULL,   -- Referencia textual a la firma de aprobación
    -- Enlace lógico con MongoDB (sección 9.6): _id BSON de la intervención
    intervencion_id  VARCHAR(24) NULL,
    CONSTRAINT fk_orden_tenant FOREIGN KEY (tenant_id)
        REFERENCES tenant (id_tenant) ON UPDATE CASCADE,
    CONSTRAINT fk_orden_equipo FOREIGN KEY (equipo_id)
        REFERENCES equipo_biomedico (id_equipo) ON UPDATE CASCADE,
    CONSTRAINT fk_orden_tiquete FOREIGN KEY (tiquete_id)
        REFERENCES tiquete (id_tiquete) ON UPDATE CASCADE,
    CONSTRAINT fk_orden_plan FOREIGN KEY (plan_id)
        REFERENCES plan_preventivo (id_plan) ON UPDATE CASCADE,
    CONSTRAINT fk_orden_tecnico FOREIGN KEY (tecnico_id)
        REFERENCES usuario (id_usuario) ON UPDATE CASCADE,
    CONSTRAINT fk_orden_asignado_por FOREIGN KEY (asignado_por_id)
        REFERENCES usuario (id_usuario) ON UPDATE CASCADE,
    CONSTRAINT fk_orden_aprobado_por FOREIGN KEY (aprobado_por_id)
        REFERENCES usuario (id_usuario) ON UPDATE CASCADE
);

-- Regla de negocio (sección 6.2): el origen depende del tipo de
-- mantenimiento. Se implementa con un índice único parcial para que una
-- OT correctiva no pueda repetir el mismo tiquete.
CREATE UNIQUE INDEX uq_orden_tiquete
    ON orden_trabajo (tiquete_id) WHERE tiquete_id IS NOT NULL;

CREATE INDEX ix_orden_tenant   ON orden_trabajo (tenant_id);
CREATE INDEX ix_orden_equipo   ON orden_trabajo (tenant_id, equipo_id);
CREATE INDEX ix_orden_tecnico  ON orden_trabajo (tecnico_id, estado);
CREATE INDEX ix_orden_plan     ON orden_trabajo (plan_id);
CREATE INDEX ix_orden_estado   ON orden_trabajo (estado);
CREATE INDEX ix_orden_programada ON orden_trabajo (fecha_programada);

-- =====================================================================
-- MÓDULO 6: CALIBRACIÓN
-- (Sección 7 del ERD)
-- =====================================================================

-- ---------------------------------------------------------------------
-- 6.1 CALIBRACION
-- Registros de calibración de los equipos. Vincula equipo, proveedor
-- (laboratorio), orden de trabajo, documento (certificado) y
-- responsables (registró / aprobó).
-- RN-007: la vigencia (fecha_vencimiento) puede bloquear el cierre de
-- la OT como 'Operativo'. Base para el motor de alertas.
-- ---------------------------------------------------------------------
CREATE TABLE calibracion (
    id_calibracion     UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id          UUID NOT NULL,
    equipo_id          UUID NOT NULL,
    proveedor_id       UUID NULL,     -- Laboratorio que realizó la calibración
    orden_id           UUID NULL,     -- OT asociada (cuando aplica)
    documento_id       UUID NULL,     -- Certificado en documento_equipo
    registrado_por_id  UUID NOT NULL, -- Quién documentó la calibración
    aprobado_por_id    UUID NULL,     -- Quién aprobó el registro
    fecha_calibracion  DATE         NOT NULL,
    patron_referencia  VARCHAR(120) NOT NULL,   -- Patrón utilizado como referencia
    resultado          VARCHAR(15)  NOT NULL DEFAULT 'conforme'
                       CHECK (resultado IN ('conforme','no conforme','conforme con observaciones')),
    numero_certificado VARCHAR(80)  NULL,
    fecha_vencimiento  DATE         NOT NULL,
    CONSTRAINT fk_calibracion_tenant FOREIGN KEY (tenant_id)
        REFERENCES tenant (id_tenant) ON UPDATE CASCADE,
    CONSTRAINT fk_calibracion_equipo FOREIGN KEY (equipo_id)
        REFERENCES equipo_biomedico (id_equipo) ON UPDATE CASCADE,
    CONSTRAINT fk_calibracion_proveedor FOREIGN KEY (proveedor_id)
        REFERENCES proveedor (id_proveedor) ON UPDATE CASCADE,
    CONSTRAINT fk_calibracion_orden FOREIGN KEY (orden_id)
        REFERENCES orden_trabajo (id_orden) ON UPDATE CASCADE,
    CONSTRAINT fk_calibracion_documento FOREIGN KEY (documento_id)
        REFERENCES documento_equipo (id_documento) ON UPDATE CASCADE,
    CONSTRAINT fk_calibracion_registrado FOREIGN KEY (registrado_por_id)
        REFERENCES usuario (id_usuario) ON UPDATE CASCADE,
    CONSTRAINT fk_calibracion_aprobado FOREIGN KEY (aprobado_por_id)
        REFERENCES usuario (id_usuario) ON UPDATE CASCADE,
    CONSTRAINT ck_calibracion_vigencia CHECK (fecha_vencimiento >= fecha_calibracion)
);

CREATE INDEX ix_calibracion_vigencia
    ON calibracion (tenant_id, equipo_id, fecha_vencimiento DESC);
CREATE INDEX ix_calibracion_orden ON calibracion (orden_id);

-- =====================================================================
-- REGLAS DE INTEGRIDAD ADICIONALES (Sección 9 del ERD)
-- =====================================================================

-- 9.6: intervencion_id debe ser un ObjectId BSON válido de 24 caracteres
-- hexadecimales (formato de _id en MongoDB).
ALTER TABLE orden_trabajo
    ADD CONSTRAINT ck_orden_intervencion_id
    CHECK (intervencion_id IS NULL OR intervencion_id ~ '^[0-9a-fA-F]{24}$');

-- 9.5: la fecha de aprobación debe existir si hay firma de aprobación.
ALTER TABLE orden_trabajo
    ADD CONSTRAINT ck_orden_firma
    CHECK ((firma_aprobacion IS NOT NULL AND fecha_aprobacion IS NOT NULL)
        OR (firma_aprobacion IS NULL AND fecha_aprobacion IS NULL));

-- =====================================================================
-- DATOS INICIALES (SEED)
-- =====================================================================

-- Roles RBAC (globales)
INSERT INTO rol (nombre, descripcion) VALUES
    ('Administrador SaaS',       'Gestiona tenants, usuarios y configuración global del sistema (AC-01).'),
    ('Ingeniero Biomédico',      'Gestiona inventario, planes preventivos y calibraciones (AC-02).'),
    ('Técnico de Mantenimiento', 'Ejecuta y cierra órdenes de trabajo e intervenciones (AC-03).'),
    ('Personal Clínico',         'Reporta fallas mediante tiquetes (AC-04).'),
    ('Gerente Hospitalario',     'Consulta tableros de KPIs y reportes (AC-05).'),
    ('Auditor',                  'Solo lectura sobre historial y registros (AC-06).');

-- Catálogo global de estados de equipo (sin tenant_id, según ERD 3.3)
INSERT INTO estado_equipo (id_estado, codigo, nombre, descripcion) VALUES
    (1, 'OPERATIVO',          'Operativo',          'Equipo en condiciones normales de funcionamiento.'),
    (2, 'EN_MANTENIMIENTO',   'En mantenimiento',   'Equipo con orden de trabajo en ejecución.'),
    (3, 'FUERA_DE_SERVICIO',  'Fuera de servicio',  'Equipo inoperativo por falla crítica (RN-005).'),
    (4, 'BAJA',               'Baja',               'Equipo dado de baja del inventario.');

-- =====================================================================
-- VISTAS ÚTILES (complementarias, no forman parte del DER)
-- =====================================================================

-- Hoja de vida consolidada de un equipo
CREATE OR REPLACE VIEW v_hoja_vida_equipo AS
SELECT e.id_equipo,
       e.tenant_id,
       e.numero_serie,
       e.placa_inventario,
       te.nombre          AS tipo_equipo,
       e.marca,
       e.modelo,
       u.nombre           AS ubicacion,
       ee.nombre          AS estado_actual,
       p.nombre           AS proveedor,
       e.fecha_adquisicion,
       (SELECT COUNT(*) FROM documento_equipo d WHERE d.equipo_id = e.id_equipo)      AS total_documentos,
       (SELECT COUNT(*) FROM tiquete t WHERE t.equipo_id = e.id_equipo)               AS total_tiquetes,
       (SELECT COUNT(*) FROM orden_trabajo o WHERE o.equipo_id = e.id_equipo)         AS total_ordenes,
       (SELECT MAX(c.fecha_calibracion) FROM calibracion c
         WHERE c.equipo_id = e.id_equipo)                                             AS ultima_calibracion,
       (SELECT c.fecha_vencimiento FROM calibracion c
         WHERE c.equipo_id = e.id_equipo
         ORDER BY c.fecha_vencimiento DESC LIMIT 1)                                   AS calibracion_vence
FROM equipo_biomedico e
JOIN tipo_equipo  te ON te.id_tipo_equipo  = e.tipo_equipo_id
JOIN ubicacion    u  ON u.id_ubicacion     = e.ubicacion_id
JOIN estado_equipo ee ON ee.id_estado       = e.estado_equipo_id
LEFT JOIN proveedor p ON p.id_proveedor     = e.proveedor_id;

-- Tiquetes abiertos (para bandeja de entrada de mantenimiento)
CREATE OR REPLACE VIEW v_tiquetes_abiertos AS
SELECT t.id_tiquete,
       t.tenant_id,
       t.codigo,
       t.tipo_falla,
       t.criticidad,
       t.estado,
       t.fecha_reporte,
       e.numero_serie,
       e.placa_inventario,
       (u.nombres || ' ' || u.apellidos) AS reportante
FROM tiquete t
JOIN equipo_biomedico e ON e.id_equipo = t.equipo_id
JOIN usuario u          ON u.id_usuario = t.reportante_id
WHERE t.estado <> 'cerrado';

-- KPIs por tenant para el tablero gerencial (RF-026)
CREATE OR REPLACE VIEW v_kpi_tenant AS
SELECT t.id_tenant,
       t.nombre,
       COUNT(DISTINCT e.id_equipo)  AS total_equipos,
       COUNT(DISTINCT o.id_orden)   AS total_ordenes,
       COUNT(DISTINCT o.id_orden) FILTER (WHERE o.estado IN ('cerrada','aprobada')) AS ordenes_finalizadas,
       ROUND(100.0 * COUNT(DISTINCT o.id_orden) FILTER (WHERE o.estado IN ('cerrada','aprobada'))
             / NULLIF(COUNT(DISTINCT o.id_orden), 0), 2)                            AS pct_cierre
FROM tenant t
LEFT JOIN equipo_biomedico e ON e.tenant_id = t.id_tenant
LEFT JOIN orden_trabajo o    ON o.tenant_id = t.id_tenant
WHERE t.estado_activo
GROUP BY t.id_tenant, t.nombre;

-- =====================================================================
-- FIN DEL SCRIPT 01 - PostgreSQL (16 tablas, 6 módulos)
-- =====================================================================
