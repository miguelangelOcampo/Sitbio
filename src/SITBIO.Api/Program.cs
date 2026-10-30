using Sitbio.Api;
using Sitbio.Api.Errores;
using Sitbio.Api.Modules.Salud;
using Sitbio.Api.Seguridad;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddManejoErrores();
builder.Services.AddOpenApi();
builder.Services.AddAutenticacionJwt(builder.Configuration);

var app = builder.Build();

app.UseManejoErrores();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

var api = app.MapGroup(RutasApi.V1);
api.MapSalud();

app.Run();

/// <summary>Expuesta para las pruebas de integración (WebApplicationFactory).</summary>
public partial class Program
{
}
