using System.Text.Json.Serialization;
using CounterStrikeSharp.API.Core;

namespace IdleStopper;

// The shipped example config carries the comments. CounterStrikeSharp reads JSON with
// comments enabled, so they stay in the file once it's copied.
public sealed class IdleStopperConfig : BasePluginConfig
{
    public override int Version { get; set; } = 1;

    [JsonPropertyName("notify_seconds")]
    public int NotifySeconds { get; set; } = 45;

    [JsonPropertyName("action_seconds")]
    public int ActionSeconds { get; set; } = 75;

    [JsonPropertyName("action_type")]
    public int ActionType { get; set; } = 1;

    [JsonPropertyName("round_start_only")]
    public bool RoundStartOnly { get; set; } = false;

    [JsonPropertyName("spectator_kick_rounds")]
    public int SpectatorKickRounds { get; set; } = 3;

    [JsonPropertyName("sound_enabled")]
    public bool SoundEnabled { get; set; } = true;

    [JsonPropertyName("sound")]
    public string Sound { get; set; } = "ui/panorama/popup_reveal_01";

    [JsonPropertyName("sound_interval_seconds")]
    public int SoundIntervalSeconds { get; set; } = 5;

    [JsonPropertyName("shake_enabled")]
    public bool ShakeEnabled { get; set; } = true;

    [JsonPropertyName("center_message")]
    public bool CenterMessage { get; set; } = true;

    [JsonPropertyName("afk_command_enabled")]
    public bool AfkCommandEnabled { get; set; } = true;

    [JsonPropertyName("afk_command_seconds")]
    public int AfkCommandSeconds { get; set; } = 180;

    [JsonPropertyName("keep_loadout")]
    public bool KeepLoadout { get; set; } = true;

    [JsonPropertyName("admin_immune")]
    public bool AdminImmune { get; set; } = true;

    [JsonPropertyName("admin_roles")]
    public List<string> AdminRoles { get; set; } = ["@css/root", "@css/generic"];

    [JsonPropertyName("notify_admins")]
    public bool NotifyAdmins { get; set; } = true;

    [JsonPropertyName("announce_moves")]
    public bool AnnounceMoves { get; set; } = true;

    public void Sanitize()
    {
        NotifySeconds = Math.Max(1, NotifySeconds);
        ActionSeconds = Math.Max(NotifySeconds + 1, ActionSeconds);
        ActionType = Math.Clamp(ActionType, 0, 2);
        Sound = (Sound ?? string.Empty).Trim();
        if (Sound.Length == 0) SoundEnabled = false;
        SoundIntervalSeconds = Math.Max(0, SoundIntervalSeconds);
        AfkCommandSeconds = Math.Max(1, AfkCommandSeconds);
        SpectatorKickRounds = Math.Max(0, SpectatorKickRounds);
        AdminRoles = (AdminRoles ?? []).Select(r => r.Trim()).Where(r => r.Length > 0).Distinct().ToList();
    }
}
