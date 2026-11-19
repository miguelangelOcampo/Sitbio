"""Agrupación de equipos con K-Means (HU-009).

Cada equipo se describe por su historial: cuántas intervenciones tiene, qué parte son correctivas, su costo
confirmado (solo intervenciones firmadas) y su tiempo medio de atención. Las variables se estandarizan
antes de K-Means para que el costo en pesos no domine a los conteos. Se agrupa
por tenant: nunca se mezclan clientes.

Los grupos se numeran por fallas (correctivos por equipo) de menor a mayor, como en el prototipo, y cada uno
recibe un nombre según su centroide comparado con el promedio del tenant (por ejemplo «Alto costo y muchas
fallas», «Atención lenta» o «Estables»). El lote guarda una agrupación por cada k de 2 a 6 con su inercia,
para que el cliente cambie de k al instante.
"""

from __future__ import annotations

from typing import Any

import numpy as np
import pandas as pd
from sklearn.cluster import KMeans
from sklearn.preprocessing import StandardScaler

CARACTERISTICAS = ["intervenciones", "proporcion_correctivos", "costo_confirmado", "tiempo_medio_min"]
SEMILLA = 42
K_MINIMO = 2
K_MAXIMO = 6

# Un centroide es «alto» en una variable si supera en un 25 % el promedio del tenant, y «bajo» si queda por
# debajo del 75 %.
UMBRAL_ALTO = 1.25
UMBRAL_BAJO = 0.75


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
    """Asigna un grupo a cada equipo. Devuelve (equipos, resumen); el grupo 0 es el de menos fallas."""
    equipos, resumen, _ = agrupar_con_inercia(caracteristicas, k)
    return equipos, resumen


def agrupar_con_inercia(
    caracteristicas: pd.DataFrame, k: int = 3
) -> tuple[list[dict[str, Any]], list[dict[str, Any]], float]:
    """Como :func:`agrupar`, y además la inercia de K-Means sobre las variables estandarizadas."""
    n = len(caracteristicas)
    if n == 0:
        return [], [], 0.0
    k = max(1, min(k, n))
    # Un tiempo faltante no se publica inventado: solo la entrada del modelo usa la mediana del tenant.
    entrada = caracteristicas.fillna(caracteristicas.median()).fillna(0.0)
    escalado = StandardScaler().fit_transform(entrada.to_numpy())

    if k == 1:
        etiquetas = np.zeros(n, dtype=int)
        inercia = float(((escalado - escalado.mean(axis=0)) ** 2).sum())
    else:
        modelo = KMeans(n_clusters=k, n_init=10, random_state=SEMILLA).fit(escalado)
        etiquetas = modelo.labels_
        inercia = float(modelo.inertia_)

    # Renumera por fallas crecientes (correctivos por equipo), luego costo: el número es legible y estable.
    tabla = caracteristicas.assign(
        _grupo=etiquetas,
        _fallas=caracteristicas["intervenciones"] * caracteristicas["proporcion_correctivos"],
    )
    orden = (
        tabla.groupby("_grupo")[["_fallas", "costo_confirmado", "intervenciones"]]
        .mean()
        .sort_values(["_fallas", "costo_confirmado", "intervenciones"])
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
    referencia = _perfil(tabla)
    nombres = _unicos([nombre(_perfil(datos), referencia) for _, datos in tabla.groupby("grupo")])
    for grupo, texto in zip(resumen, nombres, strict=True):
        grupo["nombre"] = texto
    return equipos, resumen, round(inercia, 4)


def agrupaciones(caracteristicas: pd.DataFrame) -> list[dict[str, Any]]:
    """Una agrupación por cada k de 2 a 6 (sin pasar del número de equipos), con su inercia."""
    n = len(caracteristicas)
    resultado = []
    for k in range(K_MINIMO, min(K_MAXIMO, n) + 1):
        equipos, grupos, inercia = agrupar_con_inercia(caracteristicas, k)
        resultado.append({"k": k, "inercia": inercia, "grupos": grupos, "equipos": equipos})
    return resultado


def nombre(perfil: dict[str, float], referencia: dict[str, float]) -> str:
    """Nombre legible de un grupo según su centroide frente al promedio del tenant."""

    def nivel(variable: str) -> int:
        base = referencia.get(variable, 0.0)
        valor = perfil.get(variable, 0.0)
        if base <= 0:
            return 1 if valor > 0 else 0
        return 1 if valor > base * UMBRAL_ALTO else -1 if valor < base * UMBRAL_BAJO else 0

    costo, fallas, tiempo = nivel("costo_confirmado"), nivel("fallas"), nivel("tiempo_medio_min")
    if costo > 0 and fallas > 0:
        return "Alto costo y muchas fallas"
    if fallas > 0:
        return "Muchas fallas"
    if costo > 0:
        return "Alto costo"
    if tiempo > 0:
        return "Atención lenta"
    if fallas < 0 and costo <= 0:
        return "Estables"
    return "Seguimiento"


def _perfil(datos: pd.DataFrame) -> dict[str, float]:
    medias = datos[["costo_confirmado", "tiempo_medio_min"]].mean()
    fallas = (datos["intervenciones"] * datos["proporcion_correctivos"]).mean()
    perfil = {"fallas": float(fallas)}
    perfil.update({c: 0.0 if pd.isna(v) else float(v) for c, v in medias.items()})
    return perfil


def _unicos(nombres: list[str]) -> list[str]:
    """Dos grupos con el mismo perfil se distinguen por su posición («Estables 1», «Estables 2»)."""
    vistos: dict[str, int] = {}
    total = {n: nombres.count(n) for n in nombres}
    resultado = []
    for texto in nombres:
        if total[texto] == 1:
            resultado.append(texto)
            continue
        vistos[texto] = vistos.get(texto, 0) + 1
        resultado.append(f"{texto} {vistos[texto]}")
    return resultado


def _redondear(serie: pd.Series) -> dict[str, float | None]:
    return {c: (None if pd.isna(v) else round(float(v), 4)) for c, v in serie.items()}
