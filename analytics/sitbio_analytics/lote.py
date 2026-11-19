"""Analítica por lotes (ADR-006): lee ``intervenciones``, calcula y escribe ``resultados_analitica``.

Uso::

    python -m sitbio_analytics                       # lee y escribe en MongoDB (SITBIO_MONGO_URI)
    python -m sitbio_analytics --fuente semilla.json --sin-escribir  # Extended JSON; imprime el resultado

Se escribe un documento por tenant (``_id`` = ``tenant_id``) que se reemplaza en cada ejecución; la API lo
entrega a UI-023. Los resultados no son en tiempo real: llevan
``generado_en`` y la huella de los datos de entrada. ``agrupaciones`` trae una agrupación por cada k de 2 a 6
(con inercia y el nombre de cada grupo); ``agrupacion`` repite la del k por defecto para clientes
anteriores.
"""

from __future__ import annotations

import argparse
import hashlib
import sys
from collections.abc import Iterable, Mapping, Sequence
from datetime import UTC, datetime
from pathlib import Path
from typing import Any

import pandas as pd

from sitbio_analytics import __version__, agrupacion, metricas
from sitbio_analytics.config import cargar

COLECCION_ENTRADA = "intervenciones"
COLECCION_SALIDA = "resultados_analitica"
CAMPOS_LECTURA = {
    "_id": 1,
    "tenant_id": 1,
    "equipo_id": 1,
    "tipo_mantenimiento": 1,
    "fecha_intervencion": 1,
    "costo_total": 1,
    "tiempo_atencion_min": 1,
    "cerrada_firmada": 1,
    "repuestos": 1,
}


def huella(documentos: Sequence[Mapping[str, Any]]) -> str:
    """SHA-256 de los campos leídos, en orden de ``_id``: identifica exactamente los datos analizados."""
    from bson import json_util

    filas = sorted(({k: d.get(k) for k in CAMPOS_LECTURA} for d in documentos), key=lambda d: str(d["_id"]))
    texto = json_util.dumps(filas, sort_keys=True)
    return hashlib.sha256(texto.encode("utf-8")).hexdigest()


def calcular(
    documentos: Iterable[Mapping[str, Any]],
    fecha_corte: datetime,
    generado_en: datetime,
    k: int = 3,
) -> list[dict[str, Any]]:
    """Devuelve un documento de resultados por tenant, listo para ``resultados_analitica``."""
    documentos = list(documentos)
    corte = pd.Timestamp(fecha_corte)
    corte = corte.tz_localize("UTC") if corte.tzinfo is None else corte.tz_convert("UTC")
    df = metricas.preparar(documentos, corte)

    resultados = []
    for tenant_id, del_tenant in df.groupby("tenant_id", sort=True):
        caracteristicas = agrupacion.caracteristicas_por_equipo(del_tenant)
        equipos, grupos, inercia = agrupacion.agrupar_con_inercia(caracteristicas, k=k)
        todas = [
            {
                "algoritmo": "KMeans",
                "semilla": agrupacion.SEMILLA,
                "caracteristicas": agrupacion.CARACTERISTICAS,
                **a,
            }
            for a in agrupacion.agrupaciones(caracteristicas)
        ]
        ids = set(del_tenant["_id"])
        resultados.append(
            {
                "_id": str(tenant_id),
                "tenant_id": str(tenant_id),
                "generado_en": generado_en,
                "fecha_corte": corte.to_pydatetime(),
                "version": __version__,
                "intervenciones_analizadas": len(del_tenant),
                "huella_entrada": huella([d for d in documentos if str(d.get("_id")) in ids]),
                "indicadores": metricas.indicadores(del_tenant),
                "agrupacion": {
                    "algoritmo": "KMeans",
                    "k": len(grupos),
                    "inercia": inercia,
                    "semilla": agrupacion.SEMILLA,
                    "caracteristicas": agrupacion.CARACTERISTICAS,
                    "grupos": grupos,
                    "equipos": equipos,
                },
                "agrupaciones": todas,
            }
        )
    return resultados


def leer_archivo(ruta: Path) -> list[dict[str, Any]]:
    from bson import json_util

    return json_util.loads(ruta.read_text(encoding="utf-8"))


def escribir(coleccion: Any, resultados: Iterable[Mapping[str, Any]]) -> int:
    """Reemplaza el documento de cada tenant (upsert). Devuelve cuántos se escribieron."""
    total = 0
    for doc in resultados:
        coleccion.replace_one({"_id": doc["_id"]}, dict(doc), upsert=True)
        total += 1
    return total


def _fecha(texto: str) -> datetime:
    fecha = datetime.fromisoformat(texto.replace("Z", "+00:00"))
    return fecha if fecha.tzinfo else fecha.replace(tzinfo=UTC)


def main(argv: Sequence[str] | None = None) -> int:
    parser = argparse.ArgumentParser(prog="sitbio_analytics", description="Analítica por lotes de SITBIO.")
    parser.add_argument("--fuente", type=Path, help="Archivo Extended JSON en lugar de MongoDB.")
    parser.add_argument(
        "--fecha-corte", type=_fecha, help="Fecha de corte ISO-8601 (por defecto, ahora en UTC)."
    )
    parser.add_argument(
        "-k",
        type=int,
        default=3,
        help="Grupos de la agrupación por defecto (3); además se guardan las de 2 a 6.",
    )
    parser.add_argument(
        "--sin-escribir", action="store_true", help="Imprime el resultado sin escribir en MongoDB."
    )
    args = parser.parse_args(argv)

    ahora = datetime.now(UTC).replace(microsecond=0)
    corte = args.fecha_corte or ahora
    necesita_mongo = args.fuente is None or not args.sin_escribir

    cliente = None
    if necesita_mongo:
        from pymongo import MongoClient

        cfg = cargar()
        cliente = MongoClient(cfg.mongo_uri, serverSelectionTimeoutMS=5000, tz_aware=True)
        base = cliente[cfg.mongo_db]
    try:
        if args.fuente is not None:
            documentos = leer_archivo(args.fuente)
        else:
            documentos = list(base[COLECCION_ENTRADA].find({}, CAMPOS_LECTURA))
        resultados = calcular(documentos, corte, ahora, k=args.k)
        if args.sin_escribir:
            from bson import json_util

            print(json_util.dumps(resultados, indent=2, ensure_ascii=False))
        else:
            escritos = escribir(base[COLECCION_SALIDA], resultados)
            print(
                f"{escritos} documento(s) escritos en {COLECCION_SALIDA} ({len(documentos)} intervenciones)."
            )
    finally:
        if cliente is not None:
            cliente.close()
    return 0


if __name__ == "__main__":
    sys.exit(main())
