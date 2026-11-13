namespace Sitbio.Desktop.ViewModels;

/// <summary>Pedido de una pantalla para abrir otra opción del menú con un dato (por ejemplo, un código de tiquete).</summary>
public sealed record SolicitudNavegacion(string IdOpcion, string? Parametro = null);

/// <summary>Pantalla que puede pedir al marco que abra otra (MSG-005 «Ver seguimiento» abre UI-010).</summary>
public interface ISolicitaNavegacion
{
    event EventHandler<SolicitudNavegacion>? NavegacionSolicitada;
}

/// <summary>Pantalla que se abre con un dato que llega de otra pantalla.</summary>
public interface IRecibeParametro
{
    void Recibir(string parametro);
}
