using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Shell;

namespace E3G.BimTools.DiTools.UI.Controls;

/// <summary>
/// Barra de titulo custom "Fluent Light" reutilizable.  Reemplaza el chrome
/// nativo de Windows: logo MEP, titulo/subtitulo, estado de conexion y los
/// botones de caption (minimizar / restaurar-maximizar / cerrar) con glifos
/// Lucide.  Se coloca como hijo superior de la ventana (DockPanel.Dock=Top o
/// Grid.Row=0); la ventana debe declarar WindowStyle=None, AllowsTransparency
/// y un WindowChrome con CaptionHeight=44 para que el arrastre y el doble-clic
/// de maximizar los gestione el sistema en la zona de caption.
///
/// Los botones llevan WindowChrome.IsHitTestVisibleInChrome=True (via estilo),
/// asi reciben el clic dentro de la zona de caption.
/// </summary>
public partial class E3gTitleBar : UserControl
{
    public E3gTitleBar()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    // ============================================================
    // Dependency properties
    // ============================================================

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(E3gTitleBar),
        new PropertyMetadata("e3gbimtools"));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public static readonly DependencyProperty SubtitleProperty = DependencyProperty.Register(
        nameof(Subtitle), typeof(string), typeof(E3gTitleBar),
        new PropertyMetadata(string.Empty));

    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public static readonly DependencyProperty StatusTextProperty = DependencyProperty.Register(
        nameof(StatusText), typeof(string), typeof(E3gTitleBar),
        new PropertyMetadata("Revit conectado"));

    public string StatusText
    {
        get => (string)GetValue(StatusTextProperty);
        set => SetValue(StatusTextProperty, value);
    }

    public static readonly DependencyProperty ShowStatusProperty = DependencyProperty.Register(
        nameof(ShowStatus), typeof(bool), typeof(E3gTitleBar),
        new PropertyMetadata(true));

    public bool ShowStatus
    {
        get => (bool)GetValue(ShowStatusProperty);
        set => SetValue(ShowStatusProperty, value);
    }

    public static readonly DependencyProperty ShowMinimizeProperty = DependencyProperty.Register(
        nameof(ShowMinimize), typeof(bool), typeof(E3gTitleBar),
        new PropertyMetadata(true));

    public bool ShowMinimize
    {
        get => (bool)GetValue(ShowMinimizeProperty);
        set => SetValue(ShowMinimizeProperty, value);
    }

    public static readonly DependencyProperty ShowMaximizeProperty = DependencyProperty.Register(
        nameof(ShowMaximize), typeof(bool), typeof(E3gTitleBar),
        new PropertyMetadata(true));

    public bool ShowMaximize
    {
        get => (bool)GetValue(ShowMaximizeProperty);
        set => SetValue(ShowMaximizeProperty, value);
    }

    // ============================================================
    // Window wiring
    // ============================================================

    private Window? _window;
    private HwndSource? _hwndSource;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _window = Window.GetWindow(this);
        if (_window is null) return;

        _window.StateChanged -= OnWindowStateChanged;
        _window.StateChanged += OnWindowStateChanged;

        var handle = new WindowInteropHelper(_window).Handle;
        if (handle != IntPtr.Zero)
        {
            _hwndSource = HwndSource.FromHwnd(handle);
            _hwndSource?.AddHook(WindowProc);
        }

        ApplyMaximizedState(_window.WindowState);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_window is not null)
            _window.StateChanged -= OnWindowStateChanged;

        _hwndSource?.RemoveHook(WindowProc);
        _hwndSource = null;
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        if (_window is not null)
            ApplyMaximizedState(_window.WindowState);
    }

    /// <summary>
    /// Ajusta el borde exterior (margen de la sombra + esquinas) y el glifo del
    /// boton segun el estado de la ventana.  Al maximizar quitamos el margen de
    /// la sombra y las esquinas redondeadas para que ocupe toda el area visible.
    /// </summary>
    private void ApplyMaximizedState(WindowState state)
    {
        var maximized = state == WindowState.Maximized;

        if (MaximizeGlyph is not null)
            MaximizeGlyph.Visibility = maximized ? Visibility.Collapsed : Visibility.Visible;
        if (RestoreGlyph is not null)
            RestoreGlyph.Visibility = maximized ? Visibility.Visible : Visibility.Collapsed;
        if (MaxButton is not null)
            MaxButton.ToolTip = maximized ? "Restaurar" : "Maximizar";

        if (BarBorder is not null)
            BarBorder.CornerRadius = maximized ? new CornerRadius(0) : new CornerRadius(9, 9, 0, 0);

        // El contenido raiz de la ventana es el Border exterior (sombra+esquinas).
        if (_window?.Content is Border outer)
        {
            outer.Margin = maximized ? new Thickness(0) : new Thickness(ShadowMargin);
            outer.CornerRadius = maximized ? new CornerRadius(0) : new CornerRadius(10);
            ApplyResizeBorder(maximized);

            // El hijo directo es el Border de recorte (ChromeClip) cuya OpacityMask
            // define la curva del contenido.  Debe ir 1px MENOS que el borde exterior
            // (10 - BorderThickness=1 => 9) para casar con la curva interior del borde;
            // si no, el contenido asoma un "pico" recto en las esquinas inferiores.
            if (outer.Child is Border clip)
                clip.CornerRadius = maximized ? new CornerRadius(0) : new CornerRadius(9);
        }
    }

    // ============================================================
    // Zona de agarre para redimensionar
    // ============================================================

    /// <summary>Margen transparente que rodea al borde visible para pintar la sombra.</summary>
    private const double ShadowMargin = 12;

    /// <summary>Parte de la zona de agarre que queda por dentro del borde visible.</summary>
    private const double ResizeGripInsideEdge = 6;

    /// <summary>
    /// WindowChrome mide la zona de agarre desde el limite de la ventana, que incluye
    /// el margen de la sombra: los ResizeBorderThickness=8 de los XAML quedaban enteros
    /// fuera del borde visible, a 4 px de el.  Aqui la zona se extiende hasta dentro
    /// del borde visible, y se anula al maximizar.
    /// Los botones de la barra siguen respondiendo (IsHitTestVisibleInChrome).
    /// </summary>
    private void ApplyResizeBorder(bool maximized)
    {
        if (_window is null
            || _window.ResizeMode is ResizeMode.NoResize or ResizeMode.CanMinimize)
            return;

        var chrome = WindowChrome.GetWindowChrome(_window);
        if (chrome is null) return;

        if (chrome.IsFrozen)
        {
            chrome = (WindowChrome)chrome.Clone();
            WindowChrome.SetWindowChrome(_window, chrome);
        }

        chrome.ResizeBorderThickness = maximized
            ? new Thickness(0)
            : new Thickness(ShadowMargin + ResizeGripInsideEdge);
    }

    // ============================================================
    // Caption buttons
    // ============================================================

    private void OnMinimizeClick(object sender, RoutedEventArgs e)
    {
        if (_window is not null)
            _window.WindowState = WindowState.Minimized;
    }

    private void OnMaximizeRestoreClick(object sender, RoutedEventArgs e)
    {
        if (_window is null) return;
        _window.WindowState = _window.WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => _window?.Close();

    // ============================================================
    // WM_GETMINMAXINFO — corrige el tamaño/posicion al maximizar con
    // WindowStyle=None (si no, la ventana se sale de pantalla y tapa la
    // barra de tareas).  Constrenimos al area de trabajo del monitor.
    // ============================================================

    private const int WM_GETMINMAXINFO = 0x0024;
    private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

    private static IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_GETMINMAXINFO)
        {
            WmGetMinMaxInfo(hwnd, lParam);
            handled = false; // dejamos que WPF siga procesando
        }
        return IntPtr.Zero;
    }

    private static void WmGetMinMaxInfo(IntPtr hwnd, IntPtr lParam)
    {
        var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);

        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (monitor != IntPtr.Zero)
        {
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfo(monitor, ref mi))
            {
                var work = mi.rcWork;
                var monitorArea = mi.rcMonitor;
                mmi.ptMaxPosition.X = work.Left - monitorArea.Left;
                mmi.ptMaxPosition.Y = work.Top - monitorArea.Top;
                mmi.ptMaxSize.X = work.Right - work.Left;
                mmi.ptMaxSize.Y = work.Bottom - work.Top;
            }
        }

        Marshal.StructureToPtr(mmi, lParam, true);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr handle, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }
}
