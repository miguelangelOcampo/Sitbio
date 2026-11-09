using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Sitbio.Desktop.ViewModels;

namespace Sitbio.Desktop.Views;

/// <summary>
/// Los PasswordBox no admiten enlace de datos: aquí se copian las contraseñas al modelo y se
/// sincronizan con los campos visibles («Mostrar contraseñas»), igual que en UI-001.
/// </summary>
public partial class CambioClaveView : UserControl
{
    private CambioClaveViewModel? _modelo;

    public CambioClaveView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Conectar(DataContext as CambioClaveViewModel);
        Loaded += (_, _) => CampoActual.Focus();
    }

    private (PasswordBox Oculto, TextBox Visible, Func<CambioClaveViewModel, string> Leer, Action<CambioClaveViewModel, string> Escribir)[] Campos =>
    [
        (CampoActual, CampoActualVisible, m => m.Actual, (m, v) => m.Actual = v),
        (CampoNueva, CampoNuevaVisible, m => m.Nueva, (m, v) => m.Nueva = v),
        (CampoConfirmacion, CampoConfirmacionVisible, m => m.Confirmacion, (m, v) => m.Confirmacion = v),
    ];

    private void Conectar(CambioClaveViewModel? modelo)
    {
        if (_modelo is not null)
        {
            _modelo.PropertyChanged -= ModeloCambio;
        }

        _modelo = modelo;
        if (_modelo is not null)
        {
            _modelo.PropertyChanged += ModeloCambio;

            // En el marco la vista se reutiliza: cada diálogo nuevo empieza con los campos vacíos y ocultos.
            foreach (var (oculto, texto, leer, _) in Campos)
            {
                oculto.Password = leer(_modelo);
                oculto.Visibility = _modelo.Mostrar ? Visibility.Collapsed : Visibility.Visible;
                texto.Visibility = _modelo.Mostrar ? Visibility.Visible : Visibility.Collapsed;
            }

            if (IsLoaded)
            {
                CampoActual.Focus();
            }
        }
    }

    private void Clave_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_modelo is null)
        {
            return;
        }

        foreach (var (oculto, _, leer, escribir) in Campos)
        {
            if (ReferenceEquals(sender, oculto) && leer(_modelo) != oculto.Password)
            {
                escribir(_modelo, oculto.Password);
            }
        }
    }

    private void ModeloCambio(object? sender, PropertyChangedEventArgs e)
    {
        if (_modelo is null)
        {
            return;
        }

        if (e.PropertyName == nameof(CambioClaveViewModel.Mostrar))
        {
            var visible = _modelo.Mostrar;
            foreach (var (oculto, texto, _, _) in Campos)
            {
                texto.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                oculto.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
            }

            return;
        }

        // El modelo vació o cambió una contraseña (por ejemplo, después de guardar)
        foreach (var (oculto, _, leer, _) in Campos)
        {
            if (oculto.Password != leer(_modelo))
            {
                oculto.Password = leer(_modelo);
            }
        }
    }
}
