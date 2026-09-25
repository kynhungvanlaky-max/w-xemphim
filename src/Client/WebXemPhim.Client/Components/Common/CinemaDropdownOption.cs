namespace WebXemPhim.Client.Components.Common;

using WebXemPhim.Client.Components.Icons;

public class CinemaDropdownOption
{
    public string Value { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public CinemaIconName? Icon { get; set; }
    public string? Badge { get; set; }
}
