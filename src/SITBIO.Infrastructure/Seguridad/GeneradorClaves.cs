using System.Security.Cryptography;
using Sitbio.Application.Usuarios;

namespace Sitbio.Infrastructure.Seguridad;

/// <summary>
/// Claves temporales de 14 caracteres con generador criptográfico: al menos una mayúscula,
/// una minúscula, un dígito y un símbolo. Se omiten caracteres que se confunden (0/O, 1/l/I).
/// </summary>
public sealed class GeneradorClaves : IGeneradorClaves
{
    public const int Longitud = 14;

    private const string Mayusculas = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Minusculas = "abcdefghijkmnpqrstuvwxyz";
    private const string Digitos = "23456789";
    private const string Simbolos = "#$%*+-=?@";
    private const string Todos = Mayusculas + Minusculas + Digitos + Simbolos;

    public string Generar()
    {
        var caracteres = new char[Longitud];
        caracteres[0] = Elegir(Mayusculas);
        caracteres[1] = Elegir(Minusculas);
        caracteres[2] = Elegir(Digitos);
        caracteres[3] = Elegir(Simbolos);
        for (var i = 4; i < Longitud; i++)
        {
            caracteres[i] = Elegir(Todos);
        }

        RandomNumberGenerator.Shuffle(caracteres.AsSpan());
        return new string(caracteres);
    }

    private static char Elegir(string conjunto) => conjunto[RandomNumberGenerator.GetInt32(conjunto.Length)];
}
