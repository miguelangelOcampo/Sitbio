using Sitbio.Application.Comun;
using Sitbio.Application.Comun.Errores;
using Sitbio.Application.Seguridad;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Application.Usuarios;

/// <summary>
/// Gestión de usuarios del Administrador (HU-011, RF-001, RF-012, RN-002, RN-006). El
/// permiso «solo AC-01» lo exige la API; aquí se aplican las reglas de negocio. El tenant
/// sale siempre del token (<see cref="IContextoUsuario"/>).
/// </summary>
public sealed class GestionUsuarios(
    IRepositorioUsuarios repositorio,
    IHasherContrasenas hasher,
    IGeneradorClaves generadorClaves,
    IContextoUsuario contexto)
{
    public const string CodigoCorreoEnUso = "USR-001";
    public const string CodigoPropiaCuenta = "USR-002";
    public const string CodigoLimitePlan = "RN-009";

    public async Task<Pagina<UsuarioDto>> ListarAsync(int pagina, int tamano, CancellationToken cancellationToken)
    {
        if (pagina < 1 || tamano < 1 || tamano > Pagina<UsuarioDto>.TamanoMaximo)
        {
            throw ErrorValidacion.DeCampo("pageSize", $"La página empieza en 1 y el tamaño va de 1 a {Pagina<UsuarioDto>.TamanoMaximo}.");
        }

        var (items, total) = await repositorio.ListarAsync(pagina, tamano, cancellationToken);
        return new Pagina<UsuarioDto>(items.Select(i => UsuarioDto.Desde(i.Usuario, i.CodigoRol)).ToList(), pagina, tamano, total);
    }

    public async Task<UsuarioDto> ObtenerAsync(Guid id, CancellationToken cancellationToken)
    {
        var (usuario, codigoRol) = await Buscar(id, cancellationToken);
        return UsuarioDto.Desde(usuario, codigoRol);
    }

    /// <summary>Crea la cuenta con una clave temporal que debe cambiarse al entrar (RF-012).</summary>
    public async Task<UsuarioCreado> CrearAsync(DatosUsuario datos, CancellationToken cancellationToken)
    {
        var rol = await RolValido(datos.Rol, cancellationToken);
        var correo = Normalizar(datos.Correo);
        await ExigirCorreoLibre(correo, null, cancellationToken);
        if (CodigosRol.EsDeGestion(rol.Codigo))
        {
            await ExigirCupoDeGestion(cancellationToken);
        }

        var clave = generadorClaves.Generar();
        var hash = hasher.Hash(clave);
        var usuario = Construir(() => new Usuario(contexto.TenantId, rol.Id, datos.Nombres, datos.Apellidos, correo, hash, datos.Telefono));
        usuario.AsignarClaveTemporal(hash);

        repositorio.Agregar(usuario);
        await repositorio.GuardarAsync(cancellationToken);
        return new UsuarioCreado(UsuarioDto.Desde(usuario, rol.Codigo), clave);
    }

    public async Task<UsuarioDto> ActualizarAsync(Guid id, DatosUsuario datos, CancellationToken cancellationToken)
    {
        var (usuario, codigoAnterior) = await Buscar(id, cancellationToken);
        var rol = await RolValido(datos.Rol, cancellationToken);
        if (usuario.Id == contexto.UsuarioId && rol.Codigo != CodigosRol.AdministradorSistema)
        {
            throw new ErrorConflicto(CodigoPropiaCuenta, "No puede quitarse el rol de Administrador del Sistema.");
        }

        var correo = Normalizar(datos.Correo);
        await ExigirCorreoLibre(correo, usuario.Id, cancellationToken);

        // Pasar una cuenta activa de AC-04 o AC-07 a un rol de gestión equivale a crear un usuario de gestión.
        if (usuario.EstadoActivo && !CodigosRol.EsDeGestion(codigoAnterior) && CodigosRol.EsDeGestion(rol.Codigo))
        {
            await ExigirCupoDeGestion(cancellationToken);
        }

        Construir(() =>
        {
            usuario.Actualizar(datos.Nombres, datos.Apellidos, correo, datos.Telefono);
            return usuario;
        });
        usuario.CambiarRol(rol.Id);

        await repositorio.GuardarAsync(cancellationToken);
        return UsuarioDto.Desde(usuario, rol.Codigo);
    }

    /// <summary>Acciones de PATCH /usuarios/{id}/estado: desactivar, reactivar o desbloquear (RN-006).</summary>
    public async Task<UsuarioDto> CambiarEstadoAsync(Guid id, string accion, CancellationToken cancellationToken)
    {
        var (usuario, codigoRol) = await Buscar(id, cancellationToken);
        switch (accion?.Trim().ToLowerInvariant())
        {
            case "desactivar":
                if (usuario.Id == contexto.UsuarioId)
                {
                    throw new ErrorConflicto(CodigoPropiaCuenta, "No puede desactivar su propia cuenta.");
                }

                usuario.Desactivar();
                break;
            case "reactivar":
                if (!usuario.EstadoActivo && CodigosRol.EsDeGestion(codigoRol))
                {
                    await ExigirCupoDeGestion(cancellationToken);
                }

                usuario.Reactivar();
                break;
            case "desbloquear":
                usuario.Desbloquear();
                break;
            default:
                throw ErrorValidacion.DeCampo("accion", "La acción debe ser «desactivar», «reactivar» o «desbloquear».");
        }

        await repositorio.GuardarAsync(cancellationToken);
        return UsuarioDto.Desde(usuario, codigoRol);
    }

    /// <summary>RF-012: nueva clave temporal; se devuelve una sola vez y el usuario debe cambiarla al entrar.</summary>
    public async Task<string> AsignarClaveTemporalAsync(Guid id, CancellationToken cancellationToken)
    {
        var (usuario, _) = await Buscar(id, cancellationToken);
        var clave = generadorClaves.Generar();
        usuario.AsignarClaveTemporal(hasher.Hash(clave));
        await repositorio.GuardarAsync(cancellationToken);
        return clave;
    }

    private async Task<(Usuario Usuario, string CodigoRol)> Buscar(Guid id, CancellationToken cancellationToken) =>
        await repositorio.ObtenerAsync(id, cancellationToken) ?? throw new ErrorNoEncontrado("El usuario");

    private async Task<RolAsignable> RolValido(string? codigo, CancellationToken cancellationToken)
    {
        var rol = string.IsNullOrWhiteSpace(codigo) ? null : await repositorio.RolPorCodigoAsync(codigo.Trim(), cancellationToken);
        return rol ?? throw ErrorValidacion.DeCampo("rol", "Elija un rol válido (AC-01 a AC-07).");
    }

    private async Task ExigirCorreoLibre(string correo, Guid? excepto, CancellationToken cancellationToken)
    {
        if (await repositorio.CorreoEnUsoAsync(correo, excepto, cancellationToken))
        {
            // No se dice de qué institución es el correo (ADR-011).
            throw new ErrorConflicto(CodigoCorreoEnUso, "Ya existe un usuario con este correo.");
        }
    }

    /// <summary>
    /// RN-009 (CA-HU-011-03): con el cupo de usuarios de gestión lleno no se crean ni reactivan
    /// cuentas de gestión (409, MSG-019). Dos altas exactamente simultáneas podrían pasar una
    /// cuenta de más; con un solo Administrador por institución se acepta ese riesgo.
    /// </summary>
    private async Task ExigirCupoDeGestion(CancellationToken cancellationToken)
    {
        var capacidad = await repositorio.CapacidadGestionAsync(cancellationToken);
        if (capacidad.Llena)
        {
            throw new ErrorConflicto(
                CodigoLimitePlan,
                $"Tu institución alcanzó el máximo de usuarios de gestión del plan {capacidad.Plan}. "
                + "Puedes seguir consultando y trabajando con lo registrado; para ampliar la capacidad, comunícate con SITBIO.");
        }
    }

    private static string Normalizar(string? correo) =>
        Construir(() => Usuario.NormalizarCorreo(correo ?? string.Empty));

    // Las validaciones del dominio (ArgumentException) se responden como 400 por campo.
    private static T Construir<T>(Func<T> accion)
    {
        try
        {
            return accion();
        }
        catch (ArgumentException error)
        {
            throw ErrorValidacion.DeCampo(error.ParamName ?? "datos", error.Message.Split(" (Parameter")[0]);
        }
    }
}
