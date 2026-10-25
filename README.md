# SITBIO

**Sistema de Información para la Trazabilidad y Gestión de Mantenimientos Biomédicos.**
Aplicación de escritorio (C# WPF) con API REST (ASP.NET Core), PostgreSQL, MongoDB y analítica en Python, ofrecida como servicio multi-tenant en la nube del proveedor.

Proyecto formativo del programa Tecnología en Análisis y Desarrollo de Software (ADSO), ficha 3223875, del Servicio Nacional de Aprendizaje (SENA), Centro de la Tecnología y Manufactura Avanzada (CTMA), Medellín.

## Estructura

```
docs/            analisis (DOC-01 a DOC-04), diseno (DOC-05), arquitectura (DOC-06), adr
src/
  SITBIO.Desktop/        cliente WPF (MVVM): Views, ViewModels, Services, Themes
  SITBIO.Api/            API REST: Modules/, middleware
  SITBIO.Application/    casos de uso y DTO
  SITBIO.Domain/         entidades, estados y reglas de negocio
  SITBIO.Infrastructure/ PostgreSQL, MongoDB, archivos y reportes
analytics/       pipeline Python (PyMongo, Pandas, K-Means)
tests/           xUnit (.NET) y pytest (analytics)
database/        scripts 01 y 02 y migraciones V###
seeds/           datos sintéticos
```

## Requisitos

- Windows 10/11 con Visual Studio 2026 (o 2022 17.14+) y la carga de trabajo **Desarrollo de escritorio de .NET**.
- SDK de .NET 10.
- Docker Desktop.
- Python 3.12 (solo para `analytics`).
- Git.

## Puesta en marcha

```powershell
# 1. Variables locales (no se suben al repositorio)
Copy-Item .env.example .env          # cambia las contraseñas

# 2. Bases de datos de desarrollo (PostgreSQL 16 y MongoDB 7)
docker compose up -d

# 3. Compilar y probar
dotnet build SITBIO.sln
dotnet test SITBIO.sln

# 4. Ejecutar la API y comprobar el estado
dotnet run --project src/SITBIO.Api      # GET http://localhost:5080/api/v1/salud

# 5. Ejecutar el cliente de escritorio
dotnet run --project src/SITBIO.Desktop
```

Los secretos de la API (cadenas de conexión y clave JWT) se guardan con User Secrets; ver `appsettings.example.json`:

```powershell
dotnet user-secrets set "ConnectionStrings:Postgres" "Host=localhost;Port=5432;Database=sitbio_dev;Username=sitbio;Password=..." --project src/SITBIO.Api
```

Analítica:

```powershell
python -m venv .venv; .venv\Scripts\Activate.ps1
pip install -r analytics/requirements.txt -r analytics/requirements-dev.txt
ruff check . ; pytest
```

## Forma de trabajo (DOC-06, sección 13.1)

- `main` está protegida: todo cambio entra por pull request con al menos una aprobación y la integración continua aprobada (squash merge).
- Ramas: `feature/HU-###-descripcion` para historias de usuario; `fix/`, `docs/` y `chore/` para correcciones, documentación y configuración.
- Commits con Conventional Commits en español: `tipo(alcance): descripción`, por ejemplo `feat(HU-001): validar credenciales con BCrypt`. Tipos: `feat`, `fix`, `test`, `refactor`, `docs`, `chore`, `ci` y `db`.
- El esquema de la base de datos se cambia solo con scripts `database/V###__descripcion.sql` (ADR-005).
- El `tenant_id` siempre sale del token, nunca del formulario (RN-008, ADR-004).

## Equipo

| Integrante | Rol |
|---|---|
| Miguel Ángel Ocampo Muñoz | Líder del proyecto y desarrollo del cliente WPF |
| Esteban Bedoya Rojo | Desarrollo backend (API, seguridad, MongoDB y analítica) |
| Héctor Steven Cuesta | Datos, multi-tenant y pruebas |
| Juan José García Laverde | Requisitos y calidad de software |
