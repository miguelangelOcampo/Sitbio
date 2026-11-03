using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using Sitbio.Desktop.Services;

namespace Sitbio.Desktop.ViewModels;

/// <summary>Panel lateral de UI-004 para crear o editar un usuario. Valida en el cliente y muestra los errores de la API por campo.</summary>
public partial class FormularioUsuarioViewModel : ObservableObject
{
    public FormularioUsuarioViewModel(UsuarioResumen? existente, string? rolInicial = null)
    {
        Id = existente?.Id;
        _nombres = existente?.Nombres ?? string.Empty;
        _apellidos = existente?.Apellidos ?? string.Empty;
        _correo = existente?.Correo ?? string.Empty;
        _telefono = existente?.Telefono ?? string.Empty;
        _rol = GuiaRoles.Buscar(existente?.Rol ?? rolInicial ?? "AC-04");
    }

    public Guid? Id { get; }

    public bool EsNuevo => Id is null;

    public string Titulo => EsNuevo ? "Nuevo usuario" : "Editar usuario";

    public string TextoGuardar => EsNuevo ? "Crear usuario" : "Guardar cambios";

    public IReadOnlyList<InfoRol> Roles => GuiaRoles.Roles;

    [ObservableProperty]
    private string _nombres;

    [ObservableProperty]
    private string _apellidos;

    [ObservableProperty]
    private string _correo;

    [ObservableProperty]
    private string _telefono;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FuncionesDelRol), nameof(EsAuditor))]
    private InfoRol? _rol;

    public IReadOnlyList<string> FuncionesDelRol => Rol?.Funciones.Take(3).ToList() ?? [];

    public bool EsAuditor => Rol?.Codigo == "AC-07";

    [ObservableProperty]
    private string? _errorNombres;

    [ObservableProperty]
    private string? _errorApellidos;

    [ObservableProperty]
    private string? _errorCorreo;

    [ObservableProperty]
    private string? _errorTelefono;

    [ObservableProperty]
    private string? _errorRol;

    /// <summary>Aviso del formulario: MSG-008 (revisar campos), MSG-019 (límite del plan) u otro error de la API.</summary>
    [ObservableProperty]
    private Aviso? _aviso;

    public DatosUsuario Datos() => new(
        Nombres.Trim(),
        Apellidos.Trim(),
        Correo.Trim(),
        string.IsNullOrWhiteSpace(Telefono) ? null : Telefono.Trim(),
        Rol?.Codigo ?? string.Empty);

    /// <summary>Validación previa (la API vuelve a validar todo).</summary>
    public bool Validar()
    {
        ErrorNombres = string.IsNullOrWhiteSpace(Nombres) ? "Escribe los nombres." : null;
        ErrorApellidos = string.IsNullOrWhiteSpace(Apellidos) ? "Escribe los apellidos." : null;
        ErrorCorreo = Regex.IsMatch(Correo.Trim(), @"^[^@\s]+@[^@\s]+\.[^@\s]{2,}$", RegexOptions.None, TimeSpan.FromSeconds(1))
            ? null
            : "Escribe un correo válido, por ejemplo nombre.apellido@institucion.co.";
        ErrorTelefono = null;
        ErrorRol = Rol is null ? "Elige un rol." : null;

        var valido = ErrorNombres is null && ErrorApellidos is null && ErrorCorreo is null && ErrorRol is null;
        Aviso = valido ? null : CamposMarcados();
        return valido;
    }

    /// <summary>Ubica el error de la API: USR-001 en el correo, RN-009 como MSG-019, 400 en cada campo.</summary>
    public void MostrarError(ErrorApi error)
    {
        switch (error.Codigo)
        {
            case "USR-001":
                ErrorCorreo = error.Message;
                Aviso = CamposMarcados();
                return;
            case "RN-009":
                Aviso = new Aviso("Límite del plan alcanzado", error.Message + " Puedes elegir Personal Clínico, que no tiene límite.", "MSG-019");
                return;
        }

        if (error.Errores.Count > 0)
        {
            ErrorNombres = error.Errores.GetValueOrDefault("nombres");
            ErrorApellidos = error.Errores.GetValueOrDefault("apellidos");
            ErrorCorreo = error.Errores.GetValueOrDefault("correo");
            ErrorTelefono = error.Errores.GetValueOrDefault("telefono");
            ErrorRol = error.Errores.GetValueOrDefault("rol");
            Aviso = CamposMarcados();
            return;
        }

        Aviso = new Aviso(error.SinConexion ? "Sin conexión" : "No se guardaron los cambios", error.Message, error.Codigo ?? (error.SinConexion ? "MSG-011" : null));
    }

    private static Aviso CamposMarcados() => new("Revisa los campos marcados", "Corrige los datos señalados y vuelve a guardar.", "MSG-008");
}
