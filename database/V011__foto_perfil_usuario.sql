-- =====================================================================
-- SITBIO - Migración V011
-- Foto de perfil del usuario (HU-011, J-11; API-USR-FOTO).
--   * Ruta relativa de la vista (hasta 512 px) en el almacenamiento de
--     archivos; la miniatura de 128 px está al lado con el sufijo -128.
--   * Nula = sin foto: la aplicación muestra las iniciales.
--   * No es un documento del equipo: no cuenta en el almacenamiento del
--     plan (RN-009). Al cambiarla o quitarla se borran los archivos
--     anteriores.
-- Probada en PostgreSQL 16 sobre 01, V002 a V010 (23/11/2026).
-- Ejecutar después de V010. ADR-005.
-- =====================================================================
SET search_path TO sitbio, public;

ALTER TABLE usuario
    ADD COLUMN IF NOT EXISTS foto_ruta VARCHAR(500) NULL;

COMMENT ON COLUMN usuario.foto_ruta IS 'Ruta relativa de la foto de perfil (vista de hasta 512 px); la miniatura de 128 px lleva el sufijo -128. Nula = sin foto (se muestran las iniciales).';
