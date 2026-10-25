import pytest

from sitbio_analytics.config import cargar


def test_cargar_con_uri():
    cfg = cargar({"SITBIO_MONGO_URI": "mongodb://localhost:27017"})
    assert cfg.mongo_uri == "mongodb://localhost:27017"
    assert cfg.mongo_db == "sitbio_dev"


def test_cargar_sin_uri_falla():
    with pytest.raises(ValueError):
        cargar({})
