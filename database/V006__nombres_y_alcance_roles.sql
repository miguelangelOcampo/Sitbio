-- =====================================================================
-- SITBIO - Migración V006
-- Ajuste de roles del 01/11/2026 (DOC-01 a DOC-06):
--   * AC-05 pasa a llamarse «Gerente de Tecnología Biomédica» (no es el
--     gerente general de la IPS, que hace parte de la Directiva AC-06).
--   * Los campos dinámicos (RF-006) y los catálogos técnicos (RF-025) los
--     gestiona el Ingeniero Biomédico (AC-02); las áreas y ubicaciones,
--     AC-01 y AC-02.
-- Solo cambia nombre y descripción: el código AC-0x no cambia.
-- Ejecutar después de V005 (ADR-005: no se edita la V004).
-- =====================================================================
SET search_path TO sitbio, public;

UPDATE rol SET nombre = 'Gerente de Tecnología Biomédica',
       descripcion = 'Responsable gerencial del área biomédica: consulta indicadores, reportes y analítica (AC-05).'
 WHERE codigo = 'AC-05';

UPDATE rol SET descripcion = 'Administrador de TI de su institución: usuarios, roles, datos de la institución y ubicaciones (AC-01).'
 WHERE codigo = 'AC-01';

UPDATE rol SET descripcion = 'Gestiona inventario, tiquetes, órdenes, planes preventivos, calibraciones, campos dinámicos y catálogos técnicos (AC-02).'
 WHERE codigo = 'AC-02';
