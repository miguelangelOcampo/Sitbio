# Documentación de SITBIO

| Carpeta | Contenido |
|---|---|
| `analisis/` | DOC-01 Formulación y constitución, DOC-02 Gestión de interesados, DOC-03 Alcance y requisitos (ERS), DOC-04 Backlog e historias de usuario, y sus diagramas: árbol de causas (DIA-FCP-01), casos de uso y flujos DIA-FLU-000, 001, 003 y 005. |
| `diseno/` | DOC-05 Diseño UX/UI y sistema de diseño, mapa de navegación (DIA-IA-01). |
| `arquitectura/` | DOC-06 Arquitectura, datos y diseño técnico; `diagramas/` con C4 de contexto, contenedores y componentes (DIA-001 a DIA-003), secuencias SEQ-001 a SEQ-004 (DIA-004), despliegue (DIA-005) y clases (DIA-009); `datos/` con los modelos conceptual, lógico relacional y documental (DIA-006 a DIA-008), el modelo entidad-relación y la guía NoSQL. |
| `adr/` | Registro de decisiones de arquitectura ADR-001 a ADR-011. |
| `pruebas/` | DOC-07 Plan de calidad, pruebas y aceptación; trazabilidad de casos de prueba (CP) con criterios de aceptación, requisitos y pruebas automáticas; regresión final (`regresion.md`); `diagramas/` con la pirámide de pruebas, ambientes, integración continua, ciclo de defectos y Go/No-Go (DIA-QA-01 a 05). |

Cuando un cambio técnico modifica una decisión, un requisito o el modelo de datos, se actualiza el documento correspondiente en el mismo pull request (DOC-06, Anexo C).

Los `.docx` son las versiones finales de «SITBIO DOCUMENTACION FINAL»; los diagramas se regeneran desde sus fuentes `.dot` (Graphviz) o `.mmd` (Mermaid).
