using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Calendar = System.Windows.Controls.Calendar;

namespace Sitbio.Desktop.Controls;

/// <summary>
/// Campo de fecha (I-07): sigue siendo una caja de texto «dd/mm/aaaa» (se puede escribir a mano y el ViewModel
/// valida igual que antes) con un botón de calendario que abre un selector con los colores del tema. Elegir un día
/// escribe la fecha en formato colombiano. Alt + ↓ o F4 abren el calendario y Esc lo cierra.
/// </summary>
[TemplatePart(Name = "PART_Calendario", Type = typeof(Calendar))]
public sealed class CampoFecha : TextBox
{
    public const string Formato = "dd/MM/yyyy";

    public static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("es-CO");

    public static readonly DependencyProperty AbiertoProperty = DependencyProperty.Register(
        nameof(Abierto), typeof(bool), typeof(CampoFecha),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, AlAbrirOCerrar));

    private Calendar? calendario;
    private bool sincronizando;

    /// <summary>Si el calendario está abierto.</summary>
    public bool Abierto
    {
        get => (bool)GetValue(AbiertoProperty);
        set => SetValue(AbiertoProperty, value);
    }

    /// <summary>La fecha escrita en «dd/mm/aaaa» (también acepta «d/m/aaaa»); null si no es válida.</summary>
    public static DateTime? Interpretar(string? texto) =>
        DateTime.TryParseExact(texto?.Trim(), ["dd/MM/yyyy", "d/M/yyyy"], Cultura, DateTimeStyles.None, out var fecha) ? fecha : null;

    public static string Formatear(DateTime fecha) => fecha.ToString(Formato, Cultura);

    /// <summary>Escribe el día elegido en el campo y en el ViewModel, y cierra el calendario.</summary>
    public void Elegir(DateTime fecha)
    {
        Text = Formatear(fecha);
        GetBindingExpression(TextProperty)?.UpdateSource();
        CaretIndex = Text.Length;
        Abierto = false;
        Focus();
    }

    public override void OnApplyTemplate()
    {
        if (calendario is not null)
        {
            calendario.SelectedDatesChanged -= AlElegir;
        }

        base.OnApplyTemplate();
        calendario = GetTemplateChild("PART_Calendario") as Calendar;
        if (calendario is not null)
        {
            calendario.SelectedDatesChanged += AlElegir;
        }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        var tecla = e.Key == Key.System ? e.SystemKey : e.Key;
        if (tecla == Key.F4 || (tecla == Key.Down && (Keyboard.Modifiers & ModifierKeys.Alt) != 0))
        {
            Abierto = !Abierto;
            e.Handled = true;
        }
        else if (tecla == Key.Escape && Abierto)
        {
            Abierto = false;
            e.Handled = true;
        }

        base.OnPreviewKeyDown(e);
    }

    private static void AlAbrirOCerrar(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CampoFecha campo && e.NewValue is true)
        {
            campo.Sincronizar();
        }
    }

    /// <summary>Al abrir, el calendario muestra la fecha escrita (o el mes actual si no hay una válida).</summary>
    private void Sincronizar()
    {
        if (calendario is null)
        {
            return;
        }

        sincronizando = true;
        try
        {
            var fecha = Interpretar(Text);
            calendario.SelectedDate = fecha;
            calendario.DisplayMode = CalendarMode.Month;
            calendario.DisplayDate = fecha ?? DateTime.Today;
        }
        finally
        {
            sincronizando = false;
        }

        Dispatcher.BeginInvoke(() => calendario.Focus(), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void AlElegir(object? sender, SelectionChangedEventArgs e)
    {
        if (!sincronizando && calendario?.SelectedDate is { } fecha)
        {
            Elegir(fecha);

            // El calendario captura el mouse al hacer clic; se suelta para que el siguiente clic llegue a la vista.
            Mouse.Capture(null);
        }
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        if (Abierto && e.NewFocus is DependencyObject nuevo && !EsDelCalendario(nuevo))
        {
            Abierto = false;
        }
    }

    private bool EsDelCalendario(DependencyObject elemento) =>
        calendario is not null && (elemento == calendario || calendario.IsAncestorOf(elemento));
}
