using System.Text.Json.Serialization;

namespace SaveNEIN.Shared;

public class SliderMarker
{
    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    [JsonPropertyName("tooltipDescription")]
    public string TooltipDescription { get; set; } = "";

    [JsonPropertyName("value")]
    public double Value { get; set; }
}
