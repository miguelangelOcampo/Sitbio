"""Configuración del pipeline a partir de variables de entorno (sin secretos en el código, SEC-008)."""

from __future__ import annotations

import os
from dataclasses import dataclass


@dataclass(frozen=True)
class Configuracion:
    mongo_uri: str
    mongo_db: str


def cargar(entorno: dict[str, str] | None = None) -> Configuracion:
    """Lee la configuración; falla si falta la cadena de conexión."""
    env = os.environ if entorno is None else entorno
    uri = env.get("SITBIO_MONGO_URI", "").strip()
    if not uri:
        raise ValueError("Falta la variable SITBIO_MONGO_URI.")
    return Configuracion(mongo_uri=uri, mongo_db=env.get("SITBIO_MONGO_DB", "sitbio_dev"))
