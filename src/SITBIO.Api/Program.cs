using Sitbio.Api;
using Sitbio.Api.Errores;
using Sitbio.Api.Modules.Autenticacion;
using Sitbio.Api.Modules.Salud;
using Sitbio.Api.Seguridad;
using Sitbio.Application.Comun;
using Sitbio.Application.Seguridad;
using Sitbio.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddManejoErrores();
builder.Services.AddOpenApi();
builder.Services.AddAutenticacionJwt(builder.Configuration);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ContextoUsuarioHttp>();
builder.Services.AddScoped<IContextoUsuario>(sp => sp.GetRequiredService<ContextoUsuarioHttp>());
builder.Services.AddScoped<IEstablecerContextoUsuario>(sp => sp.GetRequiredService<ContextoUsuarioHttp>());
builder.Services.AddInfraestructura(builder.Configuration);
builder.Services.AddModuloAutenticacion(builder.Configuration);

var app = builder.Build();

app.UseManejoErrores();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

var api = app.MapGroup(RutasApi.V1);
api.MapSalud();
api.MapAutenticacion();

app.Run();

/// <summary>Expuesta para las pruebas de integración (WebApplicationFactory).</summary>
public partial class Program
{
}
