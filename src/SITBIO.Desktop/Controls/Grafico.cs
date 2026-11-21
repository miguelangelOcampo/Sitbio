using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Sitbio.Desktop.Controls;

public enum TipoGrafico
{
    Linea,
    Area,
    Barras,
    Dispersion,

    /// <summary>Anillo: cada serie es un segmento con el primer valor de <see cref="SerieGrafico.Valores"/>.</summary>
    Arco,
}

/// <summary>Serie de un gráfico. <paramref name="ClaveColor"/> es un pincel del tema («Brush.Primary»).</summary>
public sealed record SerieGrafico(string Nombre, string ClaveColor, IReadOnlyList<double> Valores, bool Discontinua = false);

/// <summary>Punto de un gráfico de dispersión; <see cref="Serie"/> es el índice de su serie (grupo).</summary>
public sealed record PuntoGrafico(double X, double Y, int Serie, string Titulo, string Detalle = "", object? Dato = null);

/// <summary>
/// Gráfico reutilizable (I-01, prototipo <c>LineChart</c>): eje con valores redondos, cuadrícula suave, leyenda opcional
/// y una etiqueta flotante con la serie, el valor y la etiqueta del periodo al pasar el mouse (o con las flechas cuando
/// tiene el foco). Línea, área, barras agrupadas y dispersión. En dispersión, el clic en un punto ejecuta
/// <see cref="ComandoPunto"/> con su <see cref="PuntoGrafico"/>. La etiqueta va en un <see cref="Popup"/> (capa superior
/// de la ventana, J-02) para que no se corte en los bordes del gráfico ni de su tarjeta; ver <see cref="UbicarEtiqueta"/>.
/// </summary>
public sealed class Grafico : Control
{
    public const double MargenIzquierdo = 36;
    public const double MargenDerecho = 10;
    public const double MargenSuperior = 12;
    public const double MargenInferior = 26;
    private const double AltoLeyenda = 24;
    private const double RadioPunto = 5;

    /// <summary>Distancia entre el punto resaltado y la etiqueta, y margen con el borde de la ventana.</summary>
    public const double SeparacionEtiqueta = 10;

    public static readonly DependencyProperty TipoProperty = Registrar(nameof(Tipo), TipoGrafico.Linea);
    public static readonly DependencyProperty EtiquetasProperty = Registrar<IReadOnlyList<string>>(nameof(Etiquetas), []);
    public static readonly DependencyProperty SeriesProperty = Registrar<IReadOnlyList<SerieGrafico>>(nameof(Series), []);
    public static readonly DependencyProperty PuntosProperty = Registrar<IReadOnlyList<PuntoGrafico>>(nameof(Puntos), []);
    public static readonly DependencyProperty MaximoProperty = Registrar<double?>(nameof(Maximo), null);
    public static readonly DependencyProperty MostrarLeyendaProperty = Registrar(nameof(MostrarLeyenda), true);
    public static readonly DependencyProperty TituloEjeXProperty = Registrar(nameof(TituloEjeX), string.Empty);
    public static readonly DependencyProperty TituloEjeYProperty = Registrar(nameof(TituloEjeY), string.Empty);
    public static readonly DependencyProperty FormatoValorProperty = Registrar(nameof(FormatoValor), "N0");
    public static readonly DependencyProperty ComandoPuntoProperty = Registrar<ICommand?>(nameof(ComandoPunto), null);
    public static readonly DependencyProperty CompactoProperty = Registrar(nameof(Compacto), false);

    private static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("es-CO");

    static Grafico()
    {
        FocusableProperty.OverrideMetadata(typeof(Grafico), new FrameworkPropertyMetadata(true));
        ClipToBoundsProperty.OverrideMetadata(typeof(Grafico), new FrameworkPropertyMetadata(false));
    }

    private readonly StackPanel _lineasEtiqueta = new() { Margin = new Thickness(9, 5, 9, 5) };
    private readonly Border _globo;
    private Popup? _capa;

    public Grafico()
    {
        MinHeight = 24;
        SnapsToDevicePixels = true;
        FocusVisualStyle = null;
        _globo = new Border { CornerRadius = new CornerRadius(8), Child = _lineasEtiqueta, SnapsToDevicePixels = true };
        Unloaded += (_, _) => CerrarEtiqueta();
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is false)
            {
                Resaltar(null);
            }
        };
    }

    public TipoGrafico Tipo
    {
        get => (TipoGrafico)GetValue(TipoProperty);
        set => SetValue(TipoProperty, value);
    }

    /// <summary>Etiquetas del eje horizontal (periodos) para línea, área y barras.</summary>
    public IReadOnlyList<string> Etiquetas
    {
        get => (IReadOnlyList<string>)GetValue(EtiquetasProperty);
        set => SetValue(EtiquetasProperty, value);
    }

    public IReadOnlyList<SerieGrafico> Series
    {
        get => (IReadOnlyList<SerieGrafico>)GetValue(SeriesProperty);
        set => SetValue(SeriesProperty, value);
    }

    public IReadOnlyList<PuntoGrafico> Puntos
    {
        get => (IReadOnlyList<PuntoGrafico>)GetValue(PuntosProperty);
        set => SetValue(PuntosProperty, value);
    }

    /// <summary>Tope del eje vertical; null = el mayor valor más un 10 %.</summary>
    public double? Maximo
    {
        get => (double?)GetValue(MaximoProperty);
        set => SetValue(MaximoProperty, value);
    }

    public bool MostrarLeyenda
    {
        get => (bool)GetValue(MostrarLeyendaProperty);
        set => SetValue(MostrarLeyendaProperty, value);
    }

    public string TituloEjeX
    {
        get => (string)GetValue(TituloEjeXProperty);
        set => SetValue(TituloEjeXProperty, value);
    }

    public string TituloEjeY
    {
        get => (string)GetValue(TituloEjeYProperty);
        set => SetValue(TituloEjeYProperty, value);
    }

    /// <summary>Formato numérico de los valores en los ejes y en la etiqueta («N0», «N1», «C0»).</summary>
    public string FormatoValor
    {
        get => (string)GetValue(FormatoValorProperty);
        set => SetValue(FormatoValorProperty, value);
    }

    public ICommand? ComandoPunto
    {
        get => (ICommand?)GetValue(ComandoPuntoProperty);
        set => SetValue(ComandoPuntoProperty, value);
    }

    /// <summary>Mini gráfico de los indicadores: sin ejes, cuadrícula ni leyenda, pero con la etiqueta al pasar el mouse.</summary>
    public bool Compacto
    {
        get => (bool)GetValue(CompactoProperty);
        set => SetValue(CompactoProperty, value);
    }

    /// <summary>Periodo (línea, área, barras) o punto (dispersión) bajo el mouse; null = sin etiqueta.</summary>
    public int? Resaltado { get; private set; }

    /// <summary>Texto de la etiqueta flotante del elemento resaltado.</summary>
    public string? TextoEtiqueta => Resaltado is { } i ? Etiqueta(i) : null;

    /// <summary>La etiqueta está abierta en la capa superior (solo cuando el gráfico está en una ventana).</summary>
    public bool EtiquetaAbierta => _capa?.IsOpen == true;

    /// <summary>Esquina superior izquierda de la etiqueta abierta, en coordenadas del gráfico.</summary>
    public Point? PosicionEtiqueta => EtiquetaAbierta ? new Point(_capa!.HorizontalOffset, _capa.VerticalOffset) : null;

    /// <summary>
    /// Ubica la etiqueta junto al <paramref name="ancla"/> sin salirse de <paramref name="limites"/> (la ventana): a la
    /// derecha del punto y, si no cabe, a la izquierda; arriba y, si no cabe, abajo. Si tampoco cabe así, se pega al borde.
    /// </summary>
    public static Point UbicarEtiqueta(Point ancla, Size etiqueta, Rect limites, double separacion = SeparacionEtiqueta)
    {
        var x = ancla.X + separacion;
        if (x + etiqueta.Width > limites.Right)
        {
            x = ancla.X - separacion - etiqueta.Width;
        }

        var y = ancla.Y - separacion - etiqueta.Height;
        if (y < limites.Top)
        {
            y = ancla.Y + separacion;
        }

        x = Math.Max(limites.Left, Math.Min(x, limites.Right - etiqueta.Width));
        y = Math.Max(limites.Top, Math.Min(y, limites.Bottom - etiqueta.Height));
        return new Point(x, y);
    }

    // ---------------- Escala ----------------

    /// <summary>Paso redondo de la cuadrícula (1, 2, 5, 10, 20, 50…) para cuatro divisiones, como el prototipo.</summary>
    public static double Paso(double maximo)
    {
        var bruto = Math.Max(maximo, 0) / 4;
        if (bruto <= 0)
        {
            return 1;
        }

        var potencia = Math.Pow(10, Math.Floor(Math.Log10(bruto)));
        foreach (var factor in new[] { 1, 2, 2.5, 5, 10 })
        {
            if (factor * potencia >= bruto - (bruto * 1e-9))
            {
                return factor * potencia;
            }
        }

        return 10 * potencia;
    }

    /// <summary>Índice del periodo más cercano a la posición horizontal <paramref name="x"/>.</summary>
    public static int IndiceMasCercano(double x, double izquierda, double ancho, int cantidad, bool barras)
    {
        if (cantidad <= 1 || ancho <= 0)
        {
            return 0;
        }

        var indice = barras
            ? (int)Math.Floor((x - izquierda) / (ancho / cantidad))
            : (int)Math.Round((x - izquierda) / ancho * (cantidad - 1));
        return Math.Clamp(indice, 0, cantidad - 1);
    }

    public string Etiqueta(int indice)
    {
        if (Tipo == TipoGrafico.Dispersion)
        {
            if (indice < 0 || indice >= Puntos.Count)
            {
                return string.Empty;
            }

            var punto = Puntos[indice];
            var serie = punto.Serie >= 0 && punto.Serie < Series.Count ? Series[punto.Serie].Nombre : string.Empty;
            var ejes = $"{Nombre(TituloEjeX, "X")}: {Valor(punto.X)} · {Nombre(TituloEjeY, "Y")}: {Valor(punto.Y)}";
            return string.Join('\n', new[] { punto.Titulo, punto.Detalle, ejes, serie }.Where(t => !string.IsNullOrWhiteSpace(t)));
        }

        if (Tipo == TipoGrafico.Arco)
        {
            if (indice < 0 || indice >= Series.Count || Series[indice].Valores.Count == 0)
            {
                return string.Empty;
            }

            var total = Series.Sum(x => x.Valores.Count > 0 ? x.Valores[0] : 0);
            var parte = Series[indice].Valores[0];
            var porcentaje = total > 0 ? $" ({(100 * parte / total).ToString("0.#", Cultura)} %)" : string.Empty;
            return $"{Series[indice].Nombre}: {Valor(parte)}{porcentaje}";
        }

        var valores = string.Join(" · ", Series
            .Where(s => indice < s.Valores.Count)
            .Select(s => $"{s.Nombre}: {Valor(s.Valores[indice])}"));
        var periodo = indice < Etiquetas.Count ? Etiquetas[indice] : string.Empty;
        return string.IsNullOrEmpty(periodo) ? valores : $"{valores}\n{periodo}";
    }

    private static string Nombre(string titulo, string respaldo) => string.IsNullOrWhiteSpace(titulo) ? respaldo : titulo;

    private string Valor(double valor) => valor.ToString(FormatoValor, Cultura);

    private int Cantidad => Tipo switch
    {
        TipoGrafico.Dispersion => Puntos.Count,
        TipoGrafico.Arco => Series.Count,
        _ => Series.Count == 0 ? 0 : Series.Max(s => s.Valores.Count),
    };

    private double MaximoY => Maximo ?? Tope(Tipo == TipoGrafico.Dispersion
        ? Puntos.Select(p => p.Y)
        : Series.SelectMany(s => s.Valores));

    private double MaximoX => Tope(Puntos.Select(p => p.X));

    private static double Tope(IEnumerable<double> valores)
    {
        var mayor = valores.DefaultIfEmpty(0).Max();
        var paso = Paso(mayor * 1.1);
        return paso * 4;
    }

    private Rect Area()
    {
        if (Compacto || Tipo == TipoGrafico.Arco)
        {
            return new Rect(2, 4, Math.Max(0, ActualWidth - 4), Math.Max(0, ActualHeight - 6));
        }

        var arriba = MargenSuperior + (MostrarLeyenda && Series.Count > 0 ? AltoLeyenda : 0);
        var abajo = MargenInferior + (Tipo == TipoGrafico.Dispersion && !string.IsNullOrEmpty(TituloEjeX) ? 18 : 0);
        var izquierda = MargenIzquierdo + (Tipo == TipoGrafico.Dispersion && !string.IsNullOrEmpty(TituloEjeY) ? 16 : 0);
        return new Rect(
            izquierda,
            arriba,
            Math.Max(0, ActualWidth - izquierda - MargenDerecho),
            Math.Max(0, ActualHeight - arriba - abajo));
    }

    private Point Posicion(int periodo, double valor, Rect area)
    {
        var n = Cantidad;
        var x = Tipo == TipoGrafico.Barras
            ? area.Left + ((periodo + 0.5) * area.Width / Math.Max(n, 1))
            : area.Left + (n <= 1 ? area.Width / 2 : periodo * area.Width / (n - 1));
        return new Point(x, Y(valor, area));
    }

    private double Y(double valor, Rect area) => area.Bottom - (Math.Clamp(valor / Math.Max(MaximoY, 1e-9), 0, 1) * area.Height);

    private Point PosicionDispersion(PuntoGrafico punto, Rect area) => new(
        area.Left + (Math.Clamp(punto.X / Math.Max(MaximoX, 1e-9), 0, 1) * area.Width),
        Y(punto.Y, area));

    // ---------------- Interacción ----------------

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Resaltar(Buscar(e.GetPosition(this)));
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        Resaltar(null);
    }

    /// <summary>Al desplazar la página la etiqueta quedaría fuera de lugar: se quita hasta que el mouse se mueva.</summary>
    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        base.OnPreviewMouseWheel(e);
        Resaltar(null);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (Tipo == TipoGrafico.Dispersion && Buscar(e.GetPosition(this)) is { } i)
        {
            Ejecutar(i);
            e.Handled = true;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var n = Cantidad;
        if (n == 0)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Left:
                Resaltar(Resaltado is { } a ? Math.Max(0, a - 1) : n - 1);
                e.Handled = true;
                break;
            case Key.Right:
                Resaltar(Resaltado is { } b ? Math.Min(n - 1, b + 1) : 0);
                e.Handled = true;
                break;
            case Key.Enter when Tipo == TipoGrafico.Dispersion && Resaltado is { } c:
                Ejecutar(c);
                e.Handled = true;
                break;
            case Key.Escape:
                Resaltar(null);
                break;
        }
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        if (!IsMouseOver)
        {
            Resaltar(null);
        }
    }

    private void Ejecutar(int indice)
    {
        var punto = Puntos[indice];
        if (ComandoPunto is { } comando && comando.CanExecute(punto))
        {
            comando.Execute(punto);
        }
    }

    private int? Buscar(Point posicion)
    {
        var area = Area();
        if (Cantidad == 0 || area.Width <= 0)
        {
            return null;
        }

        if (Tipo == TipoGrafico.Arco)
        {
            return SegmentoEn(posicion, area);
        }

        if (Tipo != TipoGrafico.Dispersion)
        {
            var dentro = posicion.X >= area.Left - 8 && posicion.X <= area.Right + 8 && posicion.Y >= area.Top - 8 && posicion.Y <= area.Bottom + 8;
            return dentro ? IndiceMasCercano(posicion.X, area.Left, area.Width, Cantidad, Tipo == TipoGrafico.Barras) : null;
        }

        int? cercano = null;
        var mejor = (RadioPunto + 6) * (RadioPunto + 6);
        for (var i = 0; i < Puntos.Count; i++)
        {
            var p = PosicionDispersion(Puntos[i], area);
            var d = ((p.X - posicion.X) * (p.X - posicion.X)) + ((p.Y - posicion.Y) * (p.Y - posicion.Y));
            if (d <= mejor)
            {
                mejor = d;
                cercano = i;
            }
        }

        return cercano;
    }

    /// <summary>Resalta un periodo o punto y actualiza la etiqueta (también para el lector de pantalla).</summary>
    public void Resaltar(int? indice)
    {
        if (indice is { } i && (i < 0 || i >= Cantidad))
        {
            indice = null;
        }

        if (Resaltado == indice)
        {
            return;
        }

        Resaltado = indice;
        Cursor = indice is not null && Tipo == TipoGrafico.Dispersion && ComandoPunto is not null ? Cursors.Hand : null;
        AutomationProperties.SetHelpText(this, TextoEtiqueta?.Replace('\n', ' ') ?? string.Empty);
        InvalidateVisual();
        ActualizarEtiqueta();
    }

    private static void AlCambiarDatos(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var grafico = (Grafico)d;
        grafico.Resaltado = null;
        grafico.CerrarEtiqueta();
        grafico.InvalidateVisual();
    }

    // ---------------- Etiqueta en la capa superior (J-02) ----------------

    private void ActualizarEtiqueta()
    {
        var texto = TextoEtiqueta;
        var area = Area();
        if (Resaltado is not { } indice || string.IsNullOrEmpty(texto) || PresentationSource.FromVisual(this) is null || area.Width <= 0 || area.Height <= 0)
        {
            CerrarEtiqueta();
            return;
        }

        var letra = Pincel("Brush.OnAction", Brushes.White);
        _globo.Background = Pincel("Brush.Action", Brushes.DarkGreen);
        _lineasEtiqueta.Children.Clear();
        var lineas = texto.Split('\n');
        for (var k = 0; k < lineas.Length; k++)
        {
            _lineasEtiqueta.Children.Add(new TextBlock
            {
                Text = lineas[k],
                FontFamily = FontFamily,
                FontSize = k == 0 ? 12 : 11,
                FontWeight = k == 0 ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = letra,
            });
        }

        _globo.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var posicion = UbicarEtiqueta(Ancla(indice, area), _globo.DesiredSize, LimitesVentana());
        _capa ??= new Popup
        {
            Child = _globo,
            PlacementTarget = this,
            Placement = PlacementMode.Relative,
            AllowsTransparency = true,
            IsHitTestVisible = false,
            Focusable = false,
            StaysOpen = true,
        };
        _capa.HorizontalOffset = posicion.X;
        _capa.VerticalOffset = posicion.Y;
        _capa.IsOpen = true;
    }

    private void CerrarEtiqueta()
    {
        if (_capa is not null)
        {
            _capa.IsOpen = false;
        }
    }

    /// <summary>El contenido de la ventana en coordenadas del gráfico, con un margen; sin ventana, el propio gráfico.</summary>
    private Rect LimitesVentana()
    {
        if (Window.GetWindow(this)?.Content is UIElement raiz && raiz.IsAncestorOf(this))
        {
            var origen = raiz.TranslatePoint(default, this);
            var limites = new Rect(origen, raiz.RenderSize);
            limites.Inflate(-4, -4);
            return limites.Width > 0 && limites.Height > 0 ? limites : new Rect(RenderSize);
        }

        return new Rect(RenderSize);
    }

    // ---------------- Dibujo ----------------

    protected override Size MeasureOverride(Size constraint) => new(
        double.IsInfinity(constraint.Width) ? 300 : constraint.Width,
        double.IsInfinity(constraint.Height) ? Math.Max(MinHeight, Compacto ? 40 : 180) : Math.Max(MinHeight, Math.Min(constraint.Height, 400)));

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        var area = Area();
        if (area.Width <= 0 || area.Height <= 0)
        {
            return;
        }

        var texto3 = Pincel("Brush.Text3", Brushes.Gray);
        var cuadricula = new Pen(Pincel("Brush.Border", Brushes.LightGray), 1) { DashStyle = new DashStyle([3, 4], 0) };
        var base0 = new Pen(Pincel("Brush.Border", Brushes.LightGray), 1);
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        if (Tipo == TipoGrafico.Arco)
        {
            DibujarArco(dc, area);
            return;
        }

        if (Compacto)
        {
            DibujarPeriodos(dc, area, texto3, dpi);
            return;
        }

        if (MostrarLeyenda && Series.Count > 0)
        {
            DibujarLeyenda(dc, dpi);
        }

        // Cuadrícula y valores del eje vertical
        var paso = MaximoY / 4;
        for (var t = 0; t <= 4; t++)
        {
            var y = Y(t * paso, area);
            dc.DrawLine(t == 0 ? base0 : cuadricula, new Point(area.Left, y), new Point(area.Right, y));
            var valor = Texto(Valor(t * paso), 11, texto3, dpi);
            dc.DrawText(valor, new Point(area.Left - 8 - valor.Width, y - (valor.Height / 2)));
        }

        if (Tipo == TipoGrafico.Dispersion)
        {
            DibujarDispersion(dc, area, texto3, cuadricula, dpi);
        }
        else
        {
            DibujarPeriodos(dc, area, texto3, dpi);
        }
    }

    private void DibujarLeyenda(DrawingContext dc, double dpi)
    {
        var x = MargenIzquierdo;
        var texto2 = Pincel("Brush.Text2", Brushes.DimGray);
        foreach (var serie in Series)
        {
            var color = Pincel(serie.ClaveColor, Brushes.SeaGreen);
            if (Tipo == TipoGrafico.Dispersion)
            {
                dc.DrawEllipse(color, null, new Point(x + 5, MargenSuperior + 7), 4.5, 4.5);
            }
            else
            {
                dc.DrawRoundedRectangle(color, null, new Rect(x, MargenSuperior + 5.5, 10, 3), 1.5, 1.5);
            }

            var nombre = Texto(serie.Nombre, 12, texto2, dpi);
            dc.DrawText(nombre, new Point(x + 16, MargenSuperior));
            x += 16 + nombre.Width + 18;
        }
    }

    private void DibujarPeriodos(DrawingContext dc, Rect area, Brush texto3, double dpi)
    {
        var n = Cantidad;
        if (n == 0)
        {
            return;
        }

        var barras = Tipo == TipoGrafico.Barras;
        var anchoPeriodo = area.Width / Math.Max(barras ? n : n - 1, 1);

        // Fondo del periodo resaltado
        if (Resaltado is { } r)
        {
            var centro = Posicion(r, 0, area).X;
            var ancho = barras ? Math.Max(anchoPeriodo - (Compacto ? 1 : 6), Compacto ? 4 : 12) : Compacto ? 8 : 28;
            dc.DrawRoundedRectangle(Pincel("Brush.Surface2", Brushes.WhiteSmoke), null,
                new Rect(centro - (ancho / 2), area.Top, ancho, area.Height), 8, 8);
        }

        // Etiquetas del eje horizontal (como mucho unas diez)
        var cada = n <= 12 ? 1 : (int)Math.Ceiling(n / 10.0);
        for (var i = 0; i < n && i < Etiquetas.Count && !Compacto; i++)
        {
            if (i % cada != 0)
            {
                continue;
            }

            var etiqueta = Texto(Etiquetas[i], 11, Resaltado == i ? Pincel("Brush.Text", Brushes.Black) : texto3, dpi);
            var x = Posicion(i, 0, area).X;
            dc.DrawText(etiqueta, new Point(x - (etiqueta.Width / 2), area.Bottom + 7));
        }

        if (barras)
        {
            var grupo = Math.Min(anchoPeriodo * (Compacto ? 0.75 : 0.7), Series.Count * 22.0);
            var ancho = Math.Max(2, (grupo - ((Series.Count - 1) * 3)) / Math.Max(Series.Count, 1));
            for (var s = 0; s < Series.Count; s++)
            {
                var color = Pincel(Series[s].ClaveColor, Brushes.SeaGreen);
                for (var i = 0; i < Series[s].Valores.Count; i++)
                {
                    var tope = Posicion(i, Series[s].Valores[i], area);
                    var izquierda = tope.X - (grupo / 2) + (s * (ancho + 3));
                    var alto = area.Bottom - tope.Y;
                    if (alto > 0)
                    {
                        var radio = Math.Min(Compacto ? 2 : 5, Math.Min(ancho / 2, alto / 2));
                        dc.DrawRoundedRectangle(color, null, new Rect(izquierda, tope.Y, ancho, alto), radio, radio);
                    }
                }
            }

            return;
        }

        for (var s = 0; s < Series.Count; s++)
        {
            var serie = Series[s];
            if (serie.Valores.Count == 0)
            {
                continue;
            }

            var color = Pincel(serie.ClaveColor, Brushes.SeaGreen);
            var puntos = serie.Valores.Select((v, i) => Posicion(i, v, area)).ToList();
            var linea = new StreamGeometry();
            using (var g = linea.Open())
            {
                g.BeginFigure(puntos[0], false, false);
                g.PolyLineTo(puntos.Skip(1).ToList(), true, true);
            }

            linea.Freeze();
            if (Tipo == TipoGrafico.Area || Tipo == TipoGrafico.Linea)
            {
                // Relleno suave bajo la línea, como el degradado del prototipo
                var relleno = new StreamGeometry();
                using (var g = relleno.Open())
                {
                    g.BeginFigure(new Point(puntos[0].X, area.Bottom), true, true);
                    g.PolyLineTo(puntos.Append(new Point(puntos[^1].X, area.Bottom)).ToList(), false, false);
                }

                relleno.Freeze();
                var opacidad = Tipo == TipoGrafico.Area ? 0.28 : 0.16;
                var degradado = new LinearGradientBrush(Desvanecer(color, opacidad), Desvanecer(color, 0), 90);
                degradado.Freeze();
                dc.DrawGeometry(degradado, null, relleno);
            }

            var pluma = new Pen(color, s == 0 ? 2 : 1.6) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            if (serie.Discontinua)
            {
                pluma.DashStyle = new DashStyle([3, 2.5], 0);
            }

            dc.DrawGeometry(null, pluma, linea);
            if (Resaltado is { } h && h < puntos.Count)
            {
                var radioPunto = Compacto ? 3 : 4.5;
                dc.DrawEllipse(Pincel("Brush.Surface", Brushes.White), new Pen(color, 2), puntos[h], radioPunto, radioPunto);
            }
        }
    }

    private void DibujarDispersion(DrawingContext dc, Rect area, Brush texto3, Pen cuadricula, double dpi)
    {
        var pasoX = MaximoX / 4;
        for (var t = 0; t <= 4; t++)
        {
            var x = area.Left + (t * area.Width / 4);
            if (t > 0)
            {
                dc.DrawLine(cuadricula, new Point(x, area.Top), new Point(x, area.Bottom));
            }

            var valor = Texto(Valor(t * pasoX), 11, texto3, dpi);
            dc.DrawText(valor, new Point(x - (valor.Width / 2), area.Bottom + 7));
        }

        if (!string.IsNullOrEmpty(TituloEjeX))
        {
            var titulo = Texto(TituloEjeX, 12, Pincel("Brush.Text2", Brushes.DimGray), dpi);
            dc.DrawText(titulo, new Point(area.Left + ((area.Width - titulo.Width) / 2), area.Bottom + 24));
        }

        if (!string.IsNullOrEmpty(TituloEjeY))
        {
            var titulo = Texto(TituloEjeY, 12, Pincel("Brush.Text2", Brushes.DimGray), dpi);
            dc.PushTransform(new RotateTransform(-90));
            dc.DrawText(titulo, new Point(-(area.Top + (area.Height / 2) + (titulo.Width / 2)), 0));
            dc.Pop();
        }

        var borde = Pincel("Brush.Surface", Brushes.White);
        for (var i = 0; i < Puntos.Count; i++)
        {
            var punto = Puntos[i];
            var color = punto.Serie >= 0 && punto.Serie < Series.Count ? Pincel(Series[punto.Serie].ClaveColor, Brushes.SeaGreen) : texto3;
            var radio = Resaltado == i ? RadioPunto + 2 : RadioPunto;
            dc.DrawEllipse(color, new Pen(borde, 1.5), PosicionDispersion(punto, area), radio, radio);
        }
    }

    /// <summary>Punto junto al que va la etiqueta: el punto de dispersión, el valor mayor del periodo o el centro del arco.</summary>
    private Point Ancla(int indice, Rect area)
    {
        if (Tipo == TipoGrafico.Arco)
        {
            return new Point(area.Left + (area.Width / 2), area.Top + (area.Height / 2));
        }

        if (Tipo == TipoGrafico.Dispersion)
        {
            return PosicionDispersion(Puntos[indice], area);
        }

        var mayor = Series.Where(s => indice < s.Valores.Count).Select(s => s.Valores[indice]).DefaultIfEmpty(0).Max();
        return Posicion(indice, mayor, area);
    }

    private (Point Centro, double Radio, double Grosor) Anillo(Rect area)
    {
        var exterior = Math.Max(0, (Math.Min(area.Width, area.Height) / 2) - 1);
        var grosor = Compacto ? Math.Max(4, exterior * 0.22) : Math.Max(8, exterior * 0.2);
        return (new Point(area.Left + (area.Width / 2), area.Top + (area.Height / 2)), exterior - (grosor / 2), grosor);
    }

    private IEnumerable<(int Indice, double Inicio, double Barrido)> Segmentos()
    {
        var total = Series.Sum(x => x.Valores.Count > 0 ? Math.Max(0, x.Valores[0]) : 0);
        if (total <= 0)
        {
            yield break;
        }

        var inicio = -90.0;
        for (var i = 0; i < Series.Count; i++)
        {
            var valor = Series[i].Valores.Count > 0 ? Math.Max(0, Series[i].Valores[0]) : 0;
            var barrido = 360 * valor / total;
            if (barrido > 0)
            {
                yield return (i, inicio, barrido);
            }

            inicio += barrido;
        }
    }

    private int? SegmentoEn(Point posicion, Rect area)
    {
        var (centro, radio, grosor) = Anillo(area);
        var dx = posicion.X - centro.X;
        var dy = posicion.Y - centro.Y;
        var distancia = Math.Sqrt((dx * dx) + (dy * dy));
        if (distancia < radio - grosor - 4 || distancia > radio + grosor + 4)
        {
            return null;
        }

        var angulo = Math.Atan2(dy, dx) * 180 / Math.PI;
        foreach (var (indice, inicio, barrido) in Segmentos())
        {
            var relativo = (((angulo - inicio) % 360) + 360) % 360;
            if (relativo <= barrido)
            {
                return indice;
            }
        }

        return null;
    }

    private void DibujarArco(DrawingContext dc, Rect area)
    {
        var (centro, radio, grosor) = Anillo(area);
        if (radio <= 0)
        {
            return;
        }

        dc.DrawEllipse(null, new Pen(Pincel("Brush.Surface3", Brushes.WhiteSmoke), grosor), centro, radio, radio);
        foreach (var (indice, inicio, barrido) in Segmentos())
        {
            var pluma = new Pen(Pincel(Series[indice].ClaveColor, Brushes.SeaGreen), Resaltado == indice ? grosor + 3 : grosor);
            if (barrido >= 359.99)
            {
                dc.DrawEllipse(null, pluma, centro, radio, radio);
                continue;
            }

            var figura = new PathFigure { StartPoint = EnAnillo(centro, radio, inicio), IsClosed = false, IsFilled = false };
            figura.Segments.Add(new ArcSegment(EnAnillo(centro, radio, inicio + barrido), new Size(radio, radio), 0, barrido > 180, SweepDirection.Clockwise, true));
            var geometria = new PathGeometry([figura]);
            geometria.Freeze();
            dc.DrawGeometry(null, pluma, geometria);
        }
    }

    private static Point EnAnillo(Point centro, double radio, double grados) => new(
        centro.X + (radio * Math.Cos(grados * Math.PI / 180)),
        centro.Y + (radio * Math.Sin(grados * Math.PI / 180)));

    private FormattedText Texto(string texto, double tamano, Brush color, double dpi, FontWeight? peso = null) => new(
        texto,
        Cultura,
        FlowDirection.LeftToRight,
        new Typeface(FontFamily, FontStyles.Normal, peso ?? FontWeights.Normal, FontStretches.Normal),
        tamano,
        color,
        dpi);

    private Brush Pincel(string clave, Brush respaldo) => TryFindResource(clave) as Brush ?? respaldo;

    private static Color Desvanecer(Brush pincel, double opacidad)
    {
        var color = pincel is SolidColorBrush solido ? solido.Color : Colors.SeaGreen;
        return Color.FromArgb((byte)(opacidad * 255), color.R, color.G, color.B);
    }

    private static DependencyProperty Registrar<T>(string nombre, T valor) => DependencyProperty.Register(
        nombre,
        typeof(T),
        typeof(Grafico),
        new FrameworkPropertyMetadata(valor, FrameworkPropertyMetadataOptions.AffectsRender, AlCambiarDatos));
}
