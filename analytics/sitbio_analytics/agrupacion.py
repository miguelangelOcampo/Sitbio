"""Agrupación de equipos con K-Means (HU-009).

Cada equipo se describe por su historial: cuántas intervenciones tiene, qué parte son correctivas, su costo
confirmado (solo intervenciones firmadas) y su tiempo medio de atención. Las variables se estandarizan
antes de K-Means para que el costo en pesos no domine a los conteos. Se agrupa
por tenant: nunca se mezclan clientes.
"""

from __future__ import annotations

from typing import Any

import numpy as np
import pandas as pd
from sklearn.cluster import KMeans
from sklearn.preprocessing import StandardScaler

CARACTERISTICAS = ["intervenciones", "proporcion_correctivos", "costo_confirmado", "tiempo_medio_min"]
SEMILLA = 42


def caracteristicas_por_equipo(df: pd.DataFrame) -> pd.DataFrame:
    """Una fila por equipo con las variables de la agrupación (índice = ``equipo_id``)."""
    if df.empty:
        return pd.DataFrame(columns=CARACTERISTICAS, dtype=float).rename_axis("equipo_id")
    tabla = df.assign(
        es_correctivo=df["tipo_mantenimiento"].eq("correctivo").astype(float),
        costo_confirmado=df["costo_total"].where(df["cerrada_firmada"], 0.0).fillna(0.0),
    )
    resultado = tabla.groupby("equipo_id").agg(
        intervenciones=("_id", "size"),
        proporcion_correctivos=("es_correctivo", "mean"),
        costo_confirmado=("costo_confirmado", "sum"),
        tiempo_medio_min=("tiempo_atencion_min", "mean"),
    )
    return resultado.astype(float)


def agrupar(caracteristicas: pd.DataFrame, k: int = 3) -> tuple[list[dict[str, Any]], list[dict[str, Any]]]:
    """Asigna un grupo a cada equipo. Devuelve (equipos, resumen); el grupo 0 es el de menor carga."""
    n = len(caracteristicas)
    if n == 0:
        return [], []
    k = max(1, min(k, n))
    # Un tiempo faltante no se publica inventado: solo la entrada del modelo usa la mediana del tenant.
    entrada = caracteristicas.fillna(caracteristicas.median()).fillna(0.0)

    if k == 1:
        etiquetas = np.zeros(n, dtype=int)
    else:
        escalado = StandardScaler().fit_transform(entrada.to_numpy())
        etiquetas = KMeans(n_clusters=k, n_init=10, random_state=SEMILLA).fit_predict(escalado)

    # Renumera por carga creciente (intervenciones, luego costo): el número del grupo es legible y estable.
    tabla = caracteristicas.assign(_grupo=etiquetas)
    orden = (
        tabla.groupby("_grupo")[["intervenciones", "costo_confirmado"]]
        .mean()
        .sort_values(["intervenciones", "costo_confirmado"])
        .index.tolist()
    )
    tabla["grupo"] = tabla["_grupo"].map({anterior: nuevo for nuevo, anterior in enumerate(orden)})

    equipos = [
        {"equipo_id": str(equipo), "grupo": int(fila["grupo"]), **_redondear(fila[CARACTERISTICAS])}
        for equipo, fila in tabla.sort_values("grupo", kind="stable").iterrows()
    ]
    resumen = [
        {"grupo": int(grupo), "equipos": len(datos), **_redondear(datos[CARACTERISTICAS].mean())}
        for grupo, datos in tabla.groupby("grupo")
    ]
    return equipos, resumen


def _redondear(serie: pd.Series) -> dict[str, float | None]:
    return {c: (None if pd.isna(v) else round(float(v), 4)) for c, v in serie.items()}
