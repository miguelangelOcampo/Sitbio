from datetime import UTC, datetime
from pathlib import Path

import pandas as pd
import pytest
from bson import json_util

from sitbio_analytics import agrupacion, lote, metricas

RAIZ = Path(__file__).resolve().parents[2]
SEMILLA = RAIZ / "seeds" / "mongo" / "data" / "intervenciones.ejson.json"
CORTE = datetime(2026, 10, 21, tzinfo=UTC)
AHORA = datetime(2026, 11, 14, tzinfo=UTC)
ANTES_CORTE = "intervenciones anteriores a la fecha de corte"
TENANT_A = "11111111-1111-4111-8111-111111111111"


def _doc(i, equipo, tipo="preventivo", costo=0.0, firmada=True, minutos=60, tenant="t1", dia=1):
    return {
        "_id": f"i{i}",
        "tenant_id": tenant,
        "equipo_id": equipo,
        "tipo_mantenimiento": tipo,
        "fecha_intervencion": datetime(2026, 10, dia, tzinfo=UTC),
        "costo_total": costo,
        "tiempo_atencion_min": minutos,
        "cerrada_firmada": firmada,
        "repuestos": [{"cantidad": 1, "costo_unitario": costo}] if costo else [],
    }


def _indicador(resultado, nombre, universo):
    return next(i for i in resultado["indicadores"] if i["indicador"] == nombre and i["universo"] == universo)


@pytest.fixture(scope="module")
def resultados_semilla():
    return lote.calcular(lote.leer_archivo(SEMILLA), CORTE, AHORA)


def test_un_documento_por_tenant(resultados_semilla):
    assert [r["_id"] for r in resultados_semilla] == sorted(r["tenant_id"] for r in resultados_semilla)
    assert sum(r["intervenciones_analizadas"] for r in resultados_semilla) == 12
    for r in resultados_semilla:
        assert r["generado_en"] == AHORA and len(r["huella_entrada"]) == 64


def test_indicadores_coinciden_con_el_laboratorio(resultados_semilla):
    lab = pd.read_csv(RAIZ / "analytics" / "data" / "processed" / "intervenciones_lab.csv")
    del_a = lab[lab["tenant_id"] == TENANT_A]
    antes = del_a[~del_a["posterior_corte"]]
    a = next(r for r in resultados_semilla if r["tenant_id"] == TENANT_A)

    firmadas = _indicador(a, "Proporción de intervenciones firmadas", ANTES_CORTE)
    assert firmadas["denominador"] == len(antes)
    assert firmadas["numerador"] == int(antes["cerrada_firmada"].sum())

    tiempo = _indicador(a, "Tiempo de atención promedio (min)", ANTES_CORTE)
    assert tiempo["valor"] == pytest.approx(antes["tiempo_atencion_min"].mean(), abs=1e-4)


def test_agrupacion_no_mezcla_tenants(resultados_semilla):
    lab = pd.read_csv(RAIZ / "analytics" / "data" / "processed" / "intervenciones_lab.csv")
    for r in resultados_semilla:
        propios = set(lab.loc[lab["tenant_id"] == r["tenant_id"], "equipo_id"])
        equipos = r["agrupacion"]["equipos"]
        assert {e["equipo_id"] for e in equipos} == propios
        assert sum(g["equipos"] for g in r["agrupacion"]["grupos"]) == len(equipos)


def test_proceso_determinista(resultados_semilla):
    assert lote.calcular(lote.leer_archivo(SEMILLA), CORTE, AHORA) == resultados_semilla


def test_kmeans_separa_equipos_de_alta_carga():
    docs = [_doc(1, "bajo1"), _doc(2, "bajo2"), _doc(3, "bajo3")]
    docs += [_doc(10 + i, "alto1", "correctivo", 500_000) for i in range(4)]
    docs += [_doc(20 + i, "alto2", "correctivo", 450_000) for i in range(4)]
    df = metricas.preparar(docs, pd.Timestamp(CORTE))
    equipos, grupos = agrupacion.agrupar(agrupacion.caracteristicas_por_equipo(df), k=2)
    grupo = {e["equipo_id"]: e["grupo"] for e in equipos}
    assert grupo["bajo1"] == grupo["bajo2"] == grupo["bajo3"] == 0
    assert grupo["alto1"] == grupo["alto2"] == 1
    assert [g["equipos"] for g in grupos] == [3, 2]


def test_k_mayor_que_los_equipos_se_ajusta():
    df = metricas.preparar([_doc(1, "e1")], pd.Timestamp(CORTE))
    equipos, grupos = agrupacion.agrupar(agrupacion.caracteristicas_por_equipo(df), k=3)
    assert equipos[0]["grupo"] == 0 and len(grupos) == 1


def test_sin_datos_el_indicador_queda_nulo():
    df = metricas.preparar([_doc(1, "e1", dia=25)], pd.Timestamp(CORTE))
    tiempo = next(i for i in metricas.indicadores(df) if i["indicador"].startswith("Tiempo"))
    assert tiempo["denominador"] == 0 and tiempo["valor"] is None


def test_costo_sin_firmar_no_cuenta_como_confirmado():
    df = metricas.preparar([_doc(1, "e1", "correctivo", 100_000, firmada=False)], pd.Timestamp(CORTE))
    assert df["conflicto_costo_sin_firma"].all()
    assert agrupacion.caracteristicas_por_equipo(df).loc["e1", "costo_confirmado"] == 0


def test_tipo_desconocido_falla():
    with pytest.raises(ValueError):
        metricas.preparar([_doc(1, "e1", "otro")], pd.Timestamp(CORTE))


class ColeccionFalsa:
    def __init__(self):
        self.docs = {}

    def replace_one(self, filtro, doc, upsert):
        assert upsert
        self.docs[filtro["_id"]] = doc


def test_escribir_reemplaza_por_tenant(resultados_semilla):
    coleccion = ColeccionFalsa()
    assert lote.escribir(coleccion, resultados_semilla) == 2
    assert lote.escribir(coleccion, resultados_semilla) == 2
    assert len(coleccion.docs) == 2


def test_main_sin_escribir_imprime_json(capsys):
    argumentos = ["--fuente", str(SEMILLA), "--fecha-corte", "2026-10-21T00:00:00Z", "--sin-escribir"]
    assert lote.main(argumentos) == 0
    salida = json_util.loads(capsys.readouterr().out)
    assert len(salida) == 2 and all("agrupacion" in r for r in salida)
