using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Sitbio.Desktop.ViewModels;

namespace Sitbio.Desktop.Views;

/// <summary>
/// El PasswordBox no admite enlace de datos: aquí se copia la contraseña al modelo,
/// se sincroniza con el campo visible («Mostrar») y se aplica el foco que pide el modelo.
/// </summary>
public partial class LoginView : UserControl
{
    private LoginViewModel? _modelo;

    public LoginView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Conectar(DataContext as LoginViewModel);
        Loaded += (_, _) => CampoCorreo.Focus();
    }

    private void Conectar(LoginViewModel? modelo)
    {
        if (_modelo is not null)
        {
            _modelo.PropertyChanged -= ModeloCambio;
            _modelo.FocoEnContrasenaSolicitado -= EnfocarContrasena;
        }

        _modelo = modelo;
        if (_modelo is not null)
        {
            _modelo.PropertyChanged += ModeloCambio;
            _modelo.FocoEnContrasenaSolicitado += EnfocarContrasena;
        }
    }

    private void CampoContrasena_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_modelo is not null && _modelo.Contrasena != CampoContrasena.Password)
        {
            _modelo.Contrasena = CampoContrasena.Password;
        }
    }

    private void ModeloCambio(object? sender, PropertyChangedEventArgs e)
    {
        if (_modelo is null)
        {
            return;
        }

        if (e.PropertyName == nameof(LoginViewModel.Contrasena) && CampoContrasena.Password != _modelo.Contrasena)
        {
            CampoContrasena.Password = _modelo.Contrasena;
        }
        else if (e.PropertyName == nameof(LoginViewModel.MostrarContrasena))
        {
            var visible = _modelo.MostrarContrasena;
            CampoContrasenaVisible.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            CampoContrasena.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
            (visible ? (Control)CampoContrasenaVisible : CampoContrasena).Focus();
        }
    }

    private void EnfocarContrasena(object? sender, EventArgs e)
    {
        (_modelo?.MostrarContrasena == true ? (Control)CampoContrasenaVisible : CampoContrasena).Focus();
    }
}
