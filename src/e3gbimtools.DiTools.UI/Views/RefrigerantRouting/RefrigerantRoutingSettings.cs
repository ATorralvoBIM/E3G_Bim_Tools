using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace E3G.BimTools.DiTools.UI.Views.RefrigerantRouting;

/// <summary>
/// Ajustes del trazado de refrigerante, compartidos por las pestañas «Trazado
/// DXF» y «Ajustes» de Climatizacion.  %APPDATA%\e3g\DiTools\ClimaTrazado\settings.json
/// </summary>
public sealed class RefrigerantRoutingSettings
{
    public string LastFolder { get; set; } = string.Empty;
    public double HeightMm { get; set; } = 2650;
    public double SpacingMm { get; set; } = 50;
    public string PipeType { get; set; } = "PIPE_CU";
    public string LiquidSystem { get; set; } = "50_12_LiquidRefrigerant";
    public string GasSystem { get; set; } = "50_13_GasRefrigerant";
    public bool CreateMissingSystems { get; set; } = true;
    public bool AddMissingSizes { get; set; } = true;
    public bool TrimAtRoom { get; set; } = true;
    public double InsideRoomMm { get; set; } = 300;
    public double UnitHeightMm { get; set; } = 220;
    public string PipeLayer { get; set; } = "LATS_PIPE_REFRIGERANT";
    public string RoomLayer { get; set; } = "LATS_BOUND_ROOM";
    public bool ReplacePrevious { get; set; } = true;
    /// <summary>Bloque → uso (nombre del enum) y familia («Familia : Tipo»; vacio = crear desde el bloque).</summary>
    public Dictionary<string, MappingSetting> Mappings { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public sealed class MappingSetting
    {
        public string Role { get; set; } = string.Empty;
        public string Family { get; set; } = string.Empty;
    }

    private static RefrigerantRoutingSettings? _current;
    public static RefrigerantRoutingSettings Current => _current ??= Load();

    /// <summary>Se dispara al guardar (para que la otra pestaña se actualice).</summary>
    public static event EventHandler? Changed;

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "e3g", "DiTools", "ClimaTrazado", "settings.json");

    private static RefrigerantRoutingSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<RefrigerantRoutingSettings>(File.ReadAllText(FilePath));
                if (s is not null)
                {
                    s.Mappings = new Dictionary<string, MappingSetting>(s.Mappings ?? new(), StringComparer.OrdinalIgnoreCase);
                    return s;
                }
            }
        }
        catch { /* ajustes corruptos: valores por defecto */ }
        return new RefrigerantRoutingSettings();
    }

    public void Save(object? sender = null)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* no critico */ }
        Changed?.Invoke(sender, EventArgs.Empty);
    }
}
