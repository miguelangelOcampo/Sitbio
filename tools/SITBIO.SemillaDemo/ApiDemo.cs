using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Sitbio.SemillaDemo;

/// <summary>Reloj que la semilla mueve a mano para que cada registro quede con su fecha del pasado.</summary>
public sealed class RelojDemo(DateTimeOffset inicio) : TimeProvider
{
    public DateTimeOffset Ahora { get; set; } = inicio;

    public override DateTimeOffset GetUtcNow() => Ahora.ToUniversalTime();
}

/// <summary>
/// La API real (endpoints, reglas, auditoría, MongoDB) en este mismo proceso, con el reloj de la semilla.
/// Solo aquí se apaga la vigencia del JWT, porque los tokens se emiten con fechas pasadas.
/// </summary>
public sealed class ApiDemo(Opciones opciones, RelojDemo reloj) : WebApplicationFactory<Program>
{
    private readonly string _claveJwt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var raiz = Path.Combine(Directory.GetCurrentDirectory(), "src", "SITBIO.Api");
        builder.UseContentRoot(Directory.Exists(raiz) ? raiz : AppContext.BaseDirectory);
        builder.UseSetting("ConnectionStrings:Postgres", opciones.Postgres);
        builder.UseSetting("ConnectionStrings:Mongo", opciones.Mongo);
        builder.UseSetting("Mongo:Database", opciones.BaseMongo);
        builder.UseSetting("Jwt:Clave", _claveJwt);
        builder.UseSetting(Sitbio.Api.Modules.Autenticacion.AutenticacionEndpoints.SeccionLimite, "100000");
        builder.UseSetting("Logging:LogLevel:Default", "Warning");
        builder.UseSetting("Logging:LogLevel:Microsoft", "Error");
        builder.ConfigureLogging(l => l.SetMinimumLevel(LogLevel.Warning).AddFilter("Microsoft", LogLevel.Error));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(reloj);
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o => o.TokenValidationParameters.ValidateLifetime = false);
        });
    }
}

/// <summary>Cliente HTTP de un usuario de la demo; inicia sesión de nuevo cuando el reloj avanza.</summary>
public sealed class Sesiones(ApiDemo api, RelojDemo reloj, string clave)
{
    private const string V1 = "/api/v1";
    private readonly Dictionary<string, (HttpClient Cliente, DateTimeOffset Momento)> _abiertas = [];

    public async Task<HttpClient> ComoAsync(string correo)
    {
        if (_abiertas.TryGetValue(correo, out var abierta) && abierta.Momento == reloj.Ahora)
        {
            return abierta.Cliente;
        }

        abierta.Cliente?.Dispose();
        var cliente = api.CreateClient();
        var respuesta = await cliente.PostAsJsonAsync($"{V1}/auth/login", new { correo, contrasena = clave });
        var cuerpo = await LeerAsync(respuesta, $"inicio de sesión de {correo}");
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cuerpo.GetProperty("token").GetString());
        _abiertas[correo] = (cliente, reloj.Ahora);
        return cliente;
    }

    public async Task<JsonElement> EnviarAsync(string correo, HttpMethod metodo, string ruta, object? cuerpo = null)
    {
        var cliente = await ComoAsync(correo);
        using var peticion = new HttpRequestMessage(metodo, V1 + ruta);
        if (cuerpo is not null)
        {
            peticion.Content = JsonContent.Create(cuerpo);
        }

        using var respuesta = await cliente.SendAsync(peticion);
        return await LeerAsync(respuesta, $"{metodo} {ruta} como {correo}");
    }

    public Task<JsonElement> GetAsync(string correo, string ruta) => EnviarAsync(correo, HttpMethod.Get, ruta);

    public Task<JsonElement> PostAsync(string correo, string ruta, object cuerpo) => EnviarAsync(correo, HttpMethod.Post, ruta, cuerpo);

    public Task<JsonElement> PutAsync(string correo, string ruta, object cuerpo) => EnviarAsync(correo, HttpMethod.Put, ruta, cuerpo);

    public Task<JsonElement> PatchAsync(string correo, string ruta, object cuerpo) => EnviarAsync(correo, HttpMethod.Patch, ruta, cuerpo);

    private static async Task<JsonElement> LeerAsync(HttpResponseMessage respuesta, string que)
    {
        var texto = await respuesta.Content.ReadAsStringAsync();
        if (!respuesta.IsSuccessStatusCode)
        {
            throw new ErrorSemilla($"{que} respondió {(int)respuesta.StatusCode}: {texto}");
        }

        return string.IsNullOrWhiteSpace(texto) ? default : JsonDocument.Parse(texto).RootElement.Clone();
    }
}

public sealed class ErrorSemilla(string mensaje) : Exception(mensaje);
