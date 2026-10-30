namespace Sitbio.Desktop.Services;

/// <summary>Dirección de la API. En desarrollo es la local; en otros entornos, la variable SITBIO_API_URL.</summary>
public static class ConfiguracionApi
{
    public const string Variable = "SITBIO_API_URL";
    public const string UrlDesarrollo = "http://localhost:5080/api/v1/";

    public static Uri UrlBase()
    {
        var url = Environment.GetEnvironmentVariable(Variable);
        url = string.IsNullOrWhiteSpace(url) ? UrlDesarrollo : url;
        return new Uri(url.EndsWith('/') ? url : url + "/");
    }
}
