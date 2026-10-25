# ADR-002: Persistencia híbrida

- **Estado:** Aceptado
- **Fecha:** 22/10/2026
- **Drivers:** DRV-004, DRV-006
- **Responsables:** Esteban Bedoya Rojo y Héctor Steven Cuesta

## Contexto

Las intervenciones tienen parámetros distintos por tipo de equipo y cada tenant debe poder definir los suyos sin cambios de esquema; el resto del dominio exige integridad referencial y unicidad.

## Alternativas

Solo PostgreSQL con columnas JSONB; solo MongoDB; híbrido.

## Decisión

Híbrido: PostgreSQL para el núcleo y MongoDB para intervenciones, configuracion_campos y bitacora_auditoria.

## Justificación

Aprovecha la integridad relacional donde se necesita y el esquema flexible validado donde los datos varían; cumple el objetivo formativo OBJ-04.

## Consecuencias

- Positivas: Nuevos campos sin migración; bitácora de alto volumen fuera del núcleo.
- Negativas: Dos motores que operar y respaldar; sin transacciones entre ambos.

**Riesgos:** RT-002 y RT-006.
