namespace Sitbio.Application.Seguridad;

/// <summary>Hash de contraseñas (RNF-004). La contraseña nunca se guarda ni se registra en claro.</summary>
public interface IHasherContrasenas
{
    string Hash(string contrasena);

    bool Verificar(string contrasena, string hash);
}
