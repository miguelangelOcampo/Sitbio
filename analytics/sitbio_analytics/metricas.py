"""Métricas de mantenimiento por tenant (HU-008), tomadas del laboratorio L4.

Reglas heredadas del notebook: fechas en UTC, ninguna fila se borra (se marca) y cada indicador declara su
universo, numerador y denominador. Un dato que falta no se inventa: el indicador queda en ``None``.
"""

from __future__ import annotations

from collections.abc import Iterable, Mapping
from typing import Any

import numpy as np
import pandas as pd

COLUMNAS_BASE = [
    "_id",
    "tenant_id",
    "equipo_id",
    "tipo_mantenimiento",
    "fecha_intervencion",
    "costo_total",
    "tiempo_atencion_min",
    "cerrada_firmada",
]
TIPOS_MANTENIMIENTO = {"preventivo", "correctivo", "calibracion"}


def preparar(documentos: Iterable[Mapping[str, Any]], fecha_corte: pd.Timestamp) -> pd.DataFrame:
    """Convierte los documentos de ``intervenciones`` en un DataFrame con las marcas del laboratorio."""
    documentos = list(documentos)
    df = pd.json_normalize(documentos) if documentos else pd.DataFrame(columns=COLUMNAS_BASE)
    for columna in COLUMNAS_BASE:
        if columna not in df.columns:
            df[columna] = None
    if "repuestos" not in df.columns:
        df["repuestos"] = [[] for _ in range(len(df))]

    df = df.copy()
    for columna in ("_id", "tenant_id", "equipo_id"):
        df[columna] = df[columna].astype(str)
    df["fecha_intervencion"] = pd.to_datetime(df["fecha_intervencion"], utc=True)
    df["costo_total"] = pd.to_numeric(df["costo_total"], errors="coerce").astype(float)
    df["tiempo_atencion_min"] = pd.to_numeric(df["tiempo_atencion_min"], errors="coerce").astype(float)
    df["cerrada_firmada"] = df["cerrada_firmada"].map(lambda v: v is True).astype(bool)

    desconocidos = set(df["tipo_mantenimiento"].dropna()) - TIPOS_MANTENIMIENTO
    if desconocidos:
        raise ValueError(f"Tipos de mantenimiento desconocidos: {sorted(desconocidos)}")

    df["posterior_corte"] = (df["fecha_intervencion"] >= fecha_corte).astype(bool)
    df["suma_repuestos"] = [_suma_repuestos(r) for r in df["repuestos"]]
    df["conflicto_costo_sin_firma"] = ~df["cerrada_firmada"] & (df["costo_total"] > 0)
    df["conflicto_suma_repuestos"] = ~np.isclose(df["costo_total"].fillna(0.0), df["suma_repuestos"])
    return df


def _suma_repuestos(repuestos: Any) -> float:
    if not isinstance(repuestos, list):
        return 0.0
    return float(sum(float(r.get("cantidad", 0)) * float(r.get("costo_unitario", 0)) for r in repuestos))


def _valor(numero: Any) -> float | None:
    return None if numero is None or pd.isna(numero) else round(float(numero), 4)


def _proporcion(nombre: str, universo: str, en_universo: pd.Series, cumple: pd.Series) -> dict[str, Any]:
    denominador = int(en_universo.sum())
    numerador = int((cumple & en_universo).sum())
    return {
        "indicador": nombre,
        "universo": universo,
        "numerador": numerador,
        "denominador": denominador,
        "valor": _valor(numerador / denominador) if denominador else None,
    }


def _promedio(nombre: str, universo: str, en_universo: pd.Series, valores: pd.Series) -> dict[str, Any]:
    datos = valores[en_universo].dropna()
    return {
        "indicador": nombre,
        "universo": universo,
        "numerador": _valor(datos.sum()) if len(datos) else 0.0,
        "denominador": len(datos),
        "valor": _valor(datos.mean()) if len(datos) else None,
    }


def indicadores(df: pd.DataFrame) -> list[dict[str, Any]]:
    """Indicadores de HU-008 sobre las intervenciones de un solo tenant."""
    todas = pd.Series(True, index=df.index)
    antes_corte = ~df["posterior_corte"]
    firmadas = df["cerrada_firmada"]
    correctivo = df["tipo_mantenimiento"].eq("correctivo")
    return [
        _proporcion("Proporción de intervenciones firmadas", "todas las intervenciones", todas, firmadas),
        _proporcion(
            "Proporción de intervenciones firmadas",
            "intervenciones anteriores a la fecha de corte",
            antes_corte,
            firmadas,
        ),
        _proporcion(
            "Proporción de correctivos",
            "intervenciones anteriores a la fecha de corte",
            antes_corte,
            correctivo,
        ),
        _proporcion(
            "Proporción con costo sin firmar",
            "todas las intervenciones",
            todas,
            df["conflicto_costo_sin_firma"],
        ),
        _promedio(
            "Costo promedio (COP)",
            "correctivos firmados (costo confirmado)",
            correctivo & firmadas,
            df["costo_total"],
        ),
        _promedio(
            "Tiempo de atención promedio (min)",
            "intervenciones anteriores a la fecha de corte",
            antes_corte,
            df["tiempo_atencion_min"],
        ),
    ]
