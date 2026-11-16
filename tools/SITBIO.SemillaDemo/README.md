# Semilla de demostración

Crea la institución ficticia **Hospital Demo SITBIO** con seis meses de operación simulada, para revisar la app con datos realistas. No es parte del producto ni se usa en producción.

## Uso

Desde la raíz, con Docker y `.env`:

```bash
scripts/semilla-demo.sh
```

- `--reemplazar` borra la demo anterior (PostgreSQL y MongoDB, solo ese tenant) y la vuelve a crear.
- `--dias N` cambia el periodo simulado (30 a 365; por omisión 180).
- `SITBIO_DEMO_CLAVE` fija la contraseña de los usuarios; si no está, se genera una.

Los correos y la contraseña quedan en `demo-credenciales.local.txt` (ignorado por git). La API de desarrollo debe apuntar a las mismas bases (`sitbio_dev`).

## Cómo funciona

1. Crea el tenant y sus 13 usuarios directamente en PostgreSQL, porque no hay endpoint para eso. Hay uno por cada rol de AC-01 a AC-07, más tres técnicos y tres clínicos, y un técnico inactivo.
2. Levanta la API en el mismo proceso con un reloj controlado y hace todo lo demás por sus endpoints, como lo haría la app:
   - los catálogos y los campos dinámicos;
   - 32 equipos con planes preventivos;
   - calibraciones con su aprobación;
   - fallas reportadas por el personal clínico;
   - órdenes correctivas, y las preventivas que generan las alertas;
   - intervenciones en MongoDB con pausas y equipos que salen fuera de servicio;
   - aprobaciones con firma y una baja.
3. Cada evento queda con su fecha del pasado, así que los indicadores, la línea de tiempo y la bitácora muestran historia real. Lo que cae después de hoy no se ejecuta, por eso quedan órdenes y tiquetes abiertos.

Además deja casos para ver las alertas:

- HDS-0021 con la calibración vencida.
- HDS-0019 con la calibración por vencer.
- HDS-0013 con el registro INVIMA por vencer.

La simulación usa una semilla fija de `Random`, así que en el mismo día produce los mismos datos.
