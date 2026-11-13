using Sitbio.Api;
using Microsoft.AspNetCore.Authorization;
using Sitbio.Api.Errores;
using Sitbio.Api.Modules.Auditoria;
using Sitbio.Api.Modules.Autenticacion;
using Sitbio.Api.Modules.Catalogos;
using Sitbio.Api.Modules.ConfiguracionCampos;
using Sitbio.Api.Modules.Equipos;
using Sitbio.Api.Modules.Instituciones;
using Sitbio.Api.Modules.Mantenimiento;
using Sitbio.Api.Modules.Ordenes;
using Sitbio.Api.Modules.Salud;
using Sitbio.Api.Modules.Tiquetes;
using Sitbio.Api.Modules.Usuarios;
using Sitbio.Api.Seguridad;
using Sitbio.Application.Auditoria;
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
builder.Services.AddScoped<IOrigenSolicitud, OrigenSolicitudHttp>();
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, RegistroDenegacionesAutorizacion>();
builder.Services.AddInfraestructura(builder.Configuration);
builder.Services.AddModuloAutenticacion(builder.Configuration);
builder.Services.AddModuloUsuarios();
builder.Services.AddModuloAuditoria();
builder.Services.AddModuloEquipos();
builder.Services.AddModuloCatalogos();
builder.Services.AddModuloInstituciones();
builder.Services.AddModuloConfiguracionCampos();
builder.Services.AddModuloTiquetes();
builder.Services.AddModuloOrdenes();
builder.Services.AddModuloMantenimiento();

var app = builder.Build();

app.UseManejoErrores();
app.UseAuthentication();
app.UseCambioClaveObligatorio();
app.UseAuthorization();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

var api = app.MapGroup(RutasApi.V1);
api.MapSalud();
api.MapAutenticacion();
api.MapUsuarios();
api.MapAuditoria();
api.MapEquipos();
api.MapCatalogos();
api.MapInstitucion();
api.MapConfiguracionCampos();
api.MapTiquetes();
api.MapOrdenes();
api.MapMantenimiento();

app.Run();

/// <summary>Expuesta para las pruebas de integración (WebApplicationFactory).</summary>
public partial class Program
{
}
