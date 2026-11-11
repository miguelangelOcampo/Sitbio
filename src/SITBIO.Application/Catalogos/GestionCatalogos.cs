using Sitbio.Application.Auditoria;
using Sitbio.Application.Comun;
using Sitbio.Application.Comun.Errores;
using Sitbio.Domain.Inventario;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Application.Catalogos;

/// <summary>
/// Datos para crear o editar un elemento de catálogo (API-CAT). Cada catálogo usa los suyos:
/// descripción en tipos de equipo y ubicaciones; NIT, tipo y contacto en proveedores; clase y
/// calibración por defecto en tipos de equipo (RF-030). <see cref="Activo"/> solo se usa al editar;
/// vacío conserva el estado.
/// </summary>
public sealed record DatosCatalogo(
    string? Nombre,
    string? Descripcion = null,
    string? Nit = null,
    string? TipoProveedor = null,
    string? NombreContacto = null,
    string? Correo = null,
    string? Telefono = null,
    string? ClaseRiesgo = null,
    bool? RequiereCalibracion = null,
    int? PeriodicidadCalibracionMeses = null,
    bool? Activo = null);

/// <summary>
/// Gestión de los catálogos maestros (HU-025, RF-025). El Ingeniero Biomédico (AC-02) gestiona los
/// cuatro; el Administrador del Sistema (AC-01) solo las ubicaciones y consulta el resto
/// (CA-HU-025-03). La API exige además que el rol sea AC-01 o AC-02 (RNF-003). Los elementos no se
/// borran: se inactivan (CA-HU-025-02). Crear y editar quedan en la bitácora sin datos de contacto.
/// </summary>
public sealed class GestionCatalogos(
    IRepositorioCatalogos repositorio,
    IRegistroAuditoria auditoria,
    IContextoUsuario contexto)
{
    public const int PeriodicidadCalibracionPorDefecto = 12;

    private static readonly Dictionary<string, (string Entidad, string Nombre)> Catalogos = new(StringComparer.Ordinal)
    {
        [TiposCatalogo.TiposEquipo] = ("tipo_equipo", "tipos de equipo"),
        [TiposCatalogo.Ubicaciones] = ("ubicacion", "ubicaciones"),
        [TiposCatalogo.Proveedores] = ("proveedor", "proveedores"),
        [TiposCatalogo.TiposFalla] = ("tipo_falla", "tipos de falla"),
    };

    /// <summary>Nombres de parámetro del dominio que no coinciden con el campo del cuerpo.</summary>
    private static readonly Dictionary<string, string> Campos = new(StringComparer.Ordinal)
    {
        ["clase"] = "claseRiesgo",
        ["meses"] = "periodicidadCalibracionMeses",
    };

    public static string Entidad(string tipo) => Catalogos[tipo].Entidad;

    public async Task<CatalogoDto> ObtenerAsync(string tipo, Guid id, CancellationToken cancellationToken)
    {
        var catalogo = Catalogo(tipo);
        var elemento = await repositorio.ObtenerAsync(catalogo, id, cancellationToken) ?? throw new ErrorNoEncontrado("El elemento del catálogo");
        return CatalogoDto.Desde(elemento);
    }

    /// <summary>CA-HU-025-01: el elemento nace activo y queda disponible en los formularios de la institución.</summary>
    public async Task<CatalogoDto> CrearAsync(string tipo, DatosCatalogo datos, CancellationToken cancellationToken)
    {
        var catalogo = Catalogo(tipo);
        ExigirPermiso(catalogo);

        var elemento = Construir(() => Nuevo(catalogo, datos));
        await ExigirSinDuplicados(catalogo, elemento, null, cancellationToken);

        repositorio.Agregar(elemento);
        await repositorio.GuardarAsync(cancellationToken);
        await auditoria.RegistrarAsync(
            contexto.UsuarioId,
            AccionesAuditoria.Crear,
            Entidad(catalogo),
            elemento.Id.ToString(),
            new Dictionary<string, string> { ["nombre"] = elemento.Nombre },
            cancellationToken);

        return CatalogoDto.Desde(elemento);
    }

    /// <summary>
    /// Edita el elemento y, si se indica, lo activa o inactiva (CA-HU-025-02: los registros
    /// anteriores conservan su valor). Solo se audita si algo cambió.
    /// </summary>
    public async Task<CatalogoDto> ActualizarAsync(string tipo, Guid id, DatosCatalogo datos, CancellationToken cancellationToken)
    {
        var catalogo = Catalogo(tipo);
        ExigirPermiso(catalogo);

        var elemento = await repositorio.ObtenerAsync(catalogo, id, cancellationToken) ?? throw new ErrorNoEncontrado("El elemento del catálogo");
        var antes = Instantanea(elemento);

        Construir(() =>
        {
            Editar(elemento, datos);
            return elemento;
        });

        if (datos.Activo is { } activo && activo != elemento.EstadoActivo)
        {
            if (activo)
            {
                elemento.Activar();
            }
            else
            {
                elemento.Inactivar();
            }
        }

        var cambios = Instantanea(elemento).Where(c => antes[c.Key] != c.Value).Select(c => c.Key).ToList();
        if (cambios.Count == 0)
        {
            return CatalogoDto.Desde(elemento);
        }

        await ExigirSinDuplicados(catalogo, elemento, elemento.Id, cancellationToken);
        await repositorio.GuardarAsync(cancellationToken);

        var detalle = new Dictionary<string, string> { ["nombre"] = elemento.Nombre, ["campos"] = string.Join(",", cambios) };
        if (cambios.Contains("estado_activo"))
        {
            detalle["estado"] = elemento.EstadoActivo ? "activo" : "inactivo";
        }

        await auditoria.RegistrarAsync(contexto.UsuarioId, AccionesAuditoria.Actualizar, Entidad(catalogo), elemento.Id.ToString(), detalle, cancellationToken);
        return CatalogoDto.Desde(elemento);
    }

    private static string Catalogo(string tipo) => TiposCatalogo.Normalizar(tipo) ?? throw new ErrorNoEncontrado("El catálogo");

    /// <summary>CA-HU-025-03: el Administrador del Sistema solo gestiona las ubicaciones.</summary>
    private void ExigirPermiso(string catalogo)
    {
        var permitido = contexto.Rol switch
        {
            CodigosRol.IngenieroBiomedico => true,
            CodigosRol.AdministradorSistema => catalogo == TiposCatalogo.Ubicaciones,
            _ => false,
        };

        if (!permitido)
        {
            throw new ErrorPermiso($"Solo el Ingeniero Biomédico gestiona los {Catalogos[catalogo].Nombre}; puedes consultarlos.");
        }
    }

    private IElementoCatalogo Nuevo(string catalogo, DatosCatalogo datos)
    {
        var tenant = contexto.TenantId;
        switch (catalogo)
        {
            case TiposCatalogo.TiposEquipo:
                var tipo = new TipoEquipo(tenant, datos.Nombre!, datos.Descripcion);
                var requiere = datos.RequiereCalibracion ?? true;
                tipo.DefinirValoresRegulatorios(
                    Limpio(datos.ClaseRiesgo), requiere, datos.PeriodicidadCalibracionMeses ?? (requiere ? PeriodicidadCalibracionPorDefecto : null));
                return tipo;
            case TiposCatalogo.Ubicaciones:
                return new Ubicacion(tenant, datos.Nombre!, datos.Descripcion);
            case TiposCatalogo.Proveedores:
                return new Proveedor(tenant, datos.Nombre!, datos.Nit!, datos.TipoProveedor!, Contacto(datos));
            default:
                return new TipoFalla(tenant, datos.Nombre!);
        }
    }

    private static void Editar(IElementoCatalogo elemento, DatosCatalogo datos)
    {
        switch (elemento)
        {
            case TipoEquipo tipo:
                tipo.Actualizar(datos.Nombre!, datos.Descripcion);
                var requiere = datos.RequiereCalibracion ?? tipo.RequiereCalibracion;
                var periodicidad = datos.PeriodicidadCalibracionMeses
                    ?? (requiere ? tipo.PeriodicidadCalibracionMeses ?? PeriodicidadCalibracionPorDefecto : null);
                tipo.DefinirValoresRegulatorios(Limpio(datos.ClaseRiesgo), requiere, periodicidad);
                break;
            case Ubicacion ubicacion:
                ubicacion.Actualizar(datos.Nombre!, datos.Descripcion);
                break;
            case Proveedor proveedor:
                proveedor.Actualizar(datos.Nombre!, datos.Nit!, datos.TipoProveedor!, Contacto(datos));
                break;
            case TipoFalla falla:
                falla.Renombrar(datos.Nombre!);
                break;
        }
    }

    /// <summary>Nombre único en el catálogo y, en proveedores, NIT único (409 CAT-001).</summary>
    private async Task ExigirSinDuplicados(string catalogo, IElementoCatalogo elemento, Guid? exceptoId, CancellationToken cancellationToken)
    {
        if (await repositorio.NombreEnUsoAsync(catalogo, elemento.Nombre, exceptoId, cancellationToken))
        {
            throw new ErrorConflicto(
                ErroresCatalogo.CodigoDuplicado,
                $"«{elemento.Nombre}» ya está en los {Catalogos[catalogo].Nombre} de tu institución. Si está inactivo, actívalo.");
        }

        if (elemento is Proveedor proveedor && await repositorio.NitProveedorEnUsoAsync(proveedor.Nit, exceptoId, cancellationToken))
        {
            throw new ErrorConflicto(ErroresCatalogo.CodigoDuplicado, $"Ya hay un proveedor con el NIT {proveedor.Nit} en tu institución.");
        }
    }

    /// <summary>
    /// Campos que se comparan para saber qué cambió. El contacto del proveedor se compara como un
    /// solo campo y su valor no va a la bitácora (sin datos personales).
    /// </summary>
    private static Dictionary<string, string?> Instantanea(IElementoCatalogo elemento)
    {
        var campos = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["nombre"] = elemento.Nombre,
            ["estado_activo"] = elemento.EstadoActivo.ToString(),
        };

        switch (elemento)
        {
            case TipoEquipo t:
                campos["descripcion"] = t.Descripcion;
                campos["clase_riesgo_defecto"] = t.ClaseRiesgoDefecto;
                campos["requiere_calibracion"] = t.RequiereCalibracion.ToString();
                campos["periodicidad_calibracion_meses"] = t.PeriodicidadCalibracionMeses?.ToString(System.Globalization.CultureInfo.InvariantCulture);
                break;
            case Ubicacion u:
                campos["descripcion"] = u.Descripcion;
                break;
            case Proveedor p:
                campos["nit"] = p.Nit;
                campos["tipo_proveedor"] = p.TipoProveedor;
                campos["contacto"] = string.Join('\u001f', p.NombreContacto, p.Correo, p.Telefono);
                break;
        }

        return campos;
    }

    private static ContactoProveedor Contacto(DatosCatalogo datos) => new(datos.NombreContacto, datos.Correo, datos.Telefono);

    private static string? Limpio(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    /// <summary>Convierte la validación del dominio en un 400 por campo, como en equipos.</summary>
    private static T Construir<T>(Func<T> accion)
    {
        try
        {
            return accion();
        }
        catch (ArgumentException error)
        {
            var campo = error.ParamName ?? "datos";
            throw ErrorValidacion.DeCampo(Campos.GetValueOrDefault(campo, campo), error.Message.Split(" (Parameter")[0]);
        }
    }
}
