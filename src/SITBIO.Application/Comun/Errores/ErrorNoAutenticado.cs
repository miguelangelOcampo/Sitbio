namespace Sitbio.Application.Comun.Errores;

/// <summary>La solicitud no trae una identidad válida (401): sin token, vencido o con claims incompletos.</summary>
public sealed class ErrorNoAutenticado(string mensaje = "Debe iniciar sesión para continuar.")
    : ErrorAplicacion(mensaje);
