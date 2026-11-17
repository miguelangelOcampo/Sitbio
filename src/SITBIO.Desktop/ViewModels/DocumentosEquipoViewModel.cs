using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sitbio.Desktop.Services;

namespace Sitbio.Desktop.ViewModels;

/// <summary>
/// Pestaña «Documentos» de la hoja de vida UI-006 (HU-015; prototipo HojaVida.tsx, DocumentosEquipo).
/// Lista con tipo, nombre, tamaño, fecha y quién lo subió; «Descargar» para todos los roles que ven la
/// hoja de vida; «Agregar documento» y «Desactivar» (con confirmación) solo para AC-02 y equipos activos.
/// La API valida permisos, formato y tamaño otra vez (RNF-003).
/// </summary>
public partial class DocumentosEquipoViewModel(
    IServicioDocumentosEquipo servicio,
    IDialogoArchivos dialogo,
    Guid equipoId,
    string placa,
    bool puedeGestionar) : ObservableObject
{
    public const int TamanoMaximoMb = 10;
    public const string FiltroArchivos = "PDF, JPG o PNG (*.pdf;*.jpg;*.jpeg;*.png)|*.pdf;*.jpg;*.jpeg;*.png";
    private static readonly string[] Extensiones = [".pdf", ".jpg", ".jpeg", ".png"];

    public static IReadOnlyList<string> Tipos { get; } =
        ["Manual de usuario", "Certificado de calibración", "Protocolo de mantenimiento", "Ficha técnica", "Fotografía", "Otro"];

    public string Placa { get; } = placa;

    /// <summary>Solo AC-02 y con el equipo activo (el prototipo oculta las acciones en un equipo de baja).</summary>
    public bool PuedeGestionar { get; } = puedeGestionar;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SinDocumentos))]
    private bool _cargando;

    [ObservableProperty]
    private Aviso? _aviso;

    /// <summary>Confirmación breve tras agregar, descargar o desactivar.</summary>
    [ObservableProperty]
    private Aviso? _confirmacion;

    /// <summary>null hasta cargarlos la primera vez.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SinDocumentos), nameof(Cantidad))]
    private IReadOnlyList<FilaDocumento>? _filas;

    public bool SinDocumentos => !Cargando && Filas is { Count: 0 };

    public string Cantidad => Filas?.Count is not { } n ? string.Empty : n == 1 ? "1 archivo" : $"{n} archivos";

    public string TextoVacio => PuedeGestionar ? "Adjunta el manual, la ficha técnica o los certificados de calibración." : string.Empty;

    // ---------- Agregar documento ----------

    /// <summary>Diálogo «Agregar documento» abierto.</summary>
    [ObservableProperty]
    private bool _agregando;

    [ObservableProperty]
    private string? _tipo;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NombreArchivo), nameof(TamanoArchivo), nameof(HayArchivo))]
    private string? _rutaArchivo;

    private long _bytesArchivo;

    public bool HayArchivo => RutaArchivo is not null;

    public string NombreArchivo => RutaArchivo is null ? "Ningún archivo seleccionado" : Path.GetFileName(RutaArchivo);

    public string TamanoArchivo => RutaArchivo is null ? string.Empty : FilaDocumento.Tamano(_bytesArchivo);

    [ObservableProperty]
    private string? _errorTipo;

    [ObservableProperty]
    private string? _errorArchivo;

    /// <summary>MSG-008 o el error de la API dentro del diálogo.</summary>
    [ObservableProperty]
    private Aviso? _avisoFormulario;

    [ObservableProperty]
    private bool _enviando;

    // ---------- Desactivar ----------

    /// <summary>Documento que se va a desactivar (diálogo de confirmación abierto).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoDesactivar))]
    private FilaDocumento? _porDesactivar;

    public string TextoDesactivar => PorDesactivar is null
        ? string.Empty
        : $"«{PorDesactivar.Nombre}» dejará de mostrarse en la hoja de vida de {Placa}. El archivo se conserva y la acción queda en la bitácora de auditoría.";

    [RelayCommand]
    private async Task CargarAsync()
    {
        Cargando = true;
        try
        {
            Filas = (await servicio.ListarAsync(equipoId, CancellationToken.None)).Select(d => new FilaDocumento(d)).ToList();
            Aviso = null;
        }
        catch (ErrorApi error)
        {
            Aviso = AvisoDe(error, "No pudimos cargar los documentos");
        }
        finally
        {
            Cargando = false;
        }
    }

    [RelayCommand]
    private void AbrirAgregar()
    {
        if (!PuedeGestionar)
        {
            return;
        }

        Tipo = null;
        RutaArchivo = null;
        _bytesArchivo = 0;
        ErrorTipo = ErrorArchivo = null;
        AvisoFormulario = null;
        Confirmacion = null;
        Agregando = true;
    }

    [RelayCommand]
    private void CancelarAgregar() => Agregando = false;

    [RelayCommand]
    private void ElegirArchivo()
    {
        var ruta = dialogo.ElegirArchivo($"Agregar documento · {Placa}", FiltroArchivos);
        if (ruta is null)
        {
            return;
        }

        _bytesArchivo = dialogo.Tamano(ruta);
        RutaArchivo = ruta;
        if (ErrorArchivo is not null)
        {
            ErrorArchivo = ValidarArchivo();
        }
    }

    [RelayCommand]
    private async Task ConfirmarAgregarAsync()
    {
        if (Enviando)
        {
            return;
        }

        ErrorTipo = string.IsNullOrWhiteSpace(Tipo) ? "Selecciona el tipo de documento." : null;
        ErrorArchivo = ValidarArchivo();
        if (ErrorTipo is not null || ErrorArchivo is not null)
        {
            AvisoFormulario = new Aviso("Revisa los campos marcados", "Corrige los datos señalados para continuar.", "MSG-008");
            return;
        }

        Enviando = true;
        try
        {
            var nombre = Path.GetFileName(RutaArchivo!);
            var creado = await servicio.AdjuntarAsync(equipoId, Tipo!, nombre, dialogo.Leer(RutaArchivo!), CancellationToken.None);
            Agregando = false;
            Confirmacion = new Aviso("Documento adjuntado", $"{creado.Nombre} ya aparece en la hoja de vida de {Placa}.", null, EsError: false);
            await CargarAsync();
        }
        catch (ErrorApi error)
        {
            // Los errores por campo de la API (400) van junto al campo; los demás (409 RN-009, DOC-001, sin conexión) arriba.
            ErrorTipo = error.Errores.TryGetValue("tipo", out var tipo) ? tipo : null;
            ErrorArchivo = error.Errores.TryGetValue("archivo", out var archivo) ? archivo : null;
            AvisoFormulario = AvisoDe(error, "No pudimos adjuntar el documento");
        }
        catch (IOException)
        {
            ErrorArchivo = "No se pudo leer el archivo. Verifica que exista y no esté abierto en otro programa.";
        }
        finally
        {
            Enviando = false;
        }
    }

    [RelayCommand]
    private async Task DescargarAsync(FilaDocumento? fila)
    {
        if (fila is null)
        {
            return;
        }

        var destino = dialogo.ElegirDestino(fila.Nombre);
        if (destino is null)
        {
            return;
        }

        try
        {
            var archivo = await servicio.DescargarAsync(equipoId, fila.Documento.Id, CancellationToken.None);
            dialogo.Guardar(destino, archivo.Contenido);
            Confirmacion = new Aviso("Documento descargado", $"{fila.Nombre} ({fila.TamanoTexto}) quedó guardado en {Path.GetDirectoryName(destino)}.", null, EsError: false);
            Aviso = null;
        }
        catch (ErrorApi error)
        {
            Aviso = AvisoDe(error, "No pudimos descargar el documento");
        }
        catch (IOException)
        {
            Aviso = new Aviso("No pudimos guardar el archivo", "Elige otra carpeta o cierra el archivo si está abierto en otro programa.", null);
        }
        catch (UnauthorizedAccessException)
        {
            Aviso = new Aviso("No pudimos guardar el archivo", "No tienes permiso para escribir en esa carpeta. Elige otra.", null);
        }
    }

    [RelayCommand]
    private void PedirDesactivar(FilaDocumento? fila)
    {
        if (PuedeGestionar && fila is not null)
        {
            Confirmacion = null;
            PorDesactivar = fila;
        }
    }

    [RelayCommand]
    private void CancelarDesactivar() => PorDesactivar = null;

    [RelayCommand]
    private async Task ConfirmarDesactivarAsync()
    {
        if (PorDesactivar is not { } fila || Enviando)
        {
            return;
        }

        Enviando = true;
        try
        {
            await servicio.DesactivarAsync(equipoId, fila.Documento.Id, CancellationToken.None);
            PorDesactivar = null;
            Confirmacion = new Aviso("Documento desactivado", $"{fila.Nombre} ya no se muestra en la hoja de vida. Queda registro en la bitácora.", null, EsError: false);
            await CargarAsync();
        }
        catch (ErrorApi error)
        {
            PorDesactivar = null;
            Aviso = AvisoDe(error, "No pudimos desactivar el documento");
        }
        finally
        {
            Enviando = false;
        }
    }

    [RelayCommand]
    private void CerrarConfirmacion() => Confirmacion = null;

    private string? ValidarArchivo()
    {
        if (RutaArchivo is null)
        {
            return "Selecciona un archivo.";
        }

        if (!Extensiones.Contains(Path.GetExtension(RutaArchivo), StringComparer.OrdinalIgnoreCase))
        {
            return "Solo se aceptan PDF, JPG o PNG.";
        }

        if (_bytesArchivo > TamanoMaximoMb * 1024L * 1024L)
        {
            return $"El archivo supera {TamanoMaximoMb} MB.";
        }

        return Filas?.Any(f => string.Equals(f.Nombre, Path.GetFileName(RutaArchivo), StringComparison.OrdinalIgnoreCase)) == true
            ? "Ya hay un documento con ese nombre en este equipo."
            : null;
    }

    private static Aviso AvisoDe(ErrorApi error, string titulo) => new(
        error.SinConexion ? "Sin conexión" : titulo,
        error.Message,
        error.SinConexion ? "MSG-011" : error.Codigo == "RN-009" ? "MSG-019" : error.Codigo,
        PuedeReintentar: error.SinConexion);
}

/// <summary>Una fila de la pestaña Documentos: tipo · tamaño · fecha · quién lo subió.</summary>
public sealed record FilaDocumento(DocumentoEquipo Documento)
{
    public string Nombre => Documento.Nombre;

    public string Tipo => Documento.Tipo;

    public bool EsImagen => Documento.TipoMime.StartsWith("image/", StringComparison.Ordinal);

    public string TamanoTexto => Tamano(Documento.TamanoBytes);

    public string Fecha => Documento.FechaCarga.ToLocalTime().ToString("dd/MM/yyyy", TextosEquipo.Cultura);

    public string SubidoPor => Documento.SubidoPor;

    /// <summary>«Ficha técnica · 393 KB · 17/11/2026 · Laura Gómez».</summary>
    public string Detalle => $"{Tipo} · {TamanoTexto} · {Fecha} · {SubidoPor}";

    /// <summary>Como el prototipo: «1,2 MB» desde 1 MB; si no, KB redondeados (mínimo 1).</summary>
    public static string Tamano(long bytes) => bytes >= 1024 * 1024
        ? string.Create(TextosEquipo.Cultura, $"{bytes / (1024d * 1024d):0.0} MB")
        : string.Create(CultureInfo.InvariantCulture, $"{Math.Max(1, (long)Math.Round(bytes / 1024d))} KB");
}
