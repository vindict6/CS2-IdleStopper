using System.Text.Json.Serialization;
using CounterStrikeSharp.API.Core;

namespace IdleStopper;

public sealed class IdleStopperConfig : BasePluginConfig
{
    public override int Version { get; set; } = 1;

    // JSON has no comments, so the help lives in the file as plain keys.
    [JsonPropertyName("_help_notify_seconds")]
    public string HelpNotify { get; set; } = "Seconds of no input before the player gets the warning, sound, and slaps.";

    [JsonPropertyName("notify_seconds")]
    public int NotifySeconds { get; set; } = 30;

    [JsonPropertyName("_help_action_seconds")]
    public string HelpAction { get; set; } = "Seconds of no input before the action fires. Must be higher than notify_seconds.";

    [JsonPropertyName("action_seconds")]
    public int ActionSeconds { get; set; } = 60;

    [JsonPropertyName("_help_action_type")]
    public string HelpType { get; set; } = "0 = do nothing (warning only, timer restarts), 1 = move to spectator, 2 = kick.";

    [JsonPropertyName("action_type")]
    public int ActionType { get; set; } = 1;

    public void Sanitize()
    {
        NotifySeconds = Math.Max(1, NotifySeconds);
        ActionSeconds = Math.Max(NotifySeconds + 1, ActionSeconds);
        ActionType = Math.Clamp(ActionType, 0, 2);
    }
}
