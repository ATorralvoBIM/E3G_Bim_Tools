namespace E3G.BimTools.RevitBC3.Views;

/// <summary>
/// Item para el ComboBox de codigos BC3 con autocompletado.
/// </summary>
public sealed class BC3CodeOption
{
    public string Code { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;

    public string Display
    {
        get
        {
            var s = Summary;
            if (s.Length > 80) s = s.Substring(0, 77) + "...";
            var u = string.IsNullOrWhiteSpace(Unit) ? "" : $" · {Unit}";
            return string.IsNullOrEmpty(s) ? $"{Code}{u}" : $"{Code}{u}  —  {s}";
        }
    }

    public override string ToString() => Display;
}
