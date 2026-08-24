using System.Text.Json.Serialization;
using CounterStrikeSharp.API.Core;

namespace IdleStopper;

public sealed class IdleStopperConfig : BasePluginConfig
{
    public override int Version { get; set; } = 1;

    // JSON has no comments, so the help lives in the file as plain keys.
    [JsonPropertyName("_help_notify_seconds")]
    public string HelpNotify { get; set; } = "Seconds of no input before the player gets the warning, sound, and screen shake.";

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

    [JsonPropertyName("_help_sound")]
    public string HelpSound { get; set; } = "Play a sound when the warning starts. sound is any client-side sound path.";

    [JsonPropertyName("sound_enabled")]
    public bool SoundEnabled { get; set; } = true;

    [JsonPropertyName("sound")]
    public string Sound { get; set; } = "ui/panorama/popup_reveal_01";

    [JsonPropertyName("_help_sound_interval")]
    public string HelpSoundInterval { get; set; } = "Seconds between repeats of the sound during the warning. 0 = play it once only.";

    [JsonPropertyName("sound_interval_seconds")]
    public int SoundIntervalSeconds { get; set; } = 5;

    [JsonPropertyName("_help_shake")]
    public string HelpShake { get; set; } = "Shake the player's screen every two seconds while they are warned. No damage, no movement.";

    [JsonPropertyName("shake_enabled")]
    public bool ShakeEnabled { get; set; } = true;

    [JsonPropertyName("_help_center_message")]
    public string HelpCenter { get; set; } = "true = countdown in the middle of the screen. false = one purple chat message at notify time instead.";

    [JsonPropertyName("center_message")]
    public bool CenterMessage { get; set; } = true;

    [JsonPropertyName("_help_afk_command")]
    public string HelpAfk { get; set; } = "Let players type !afk to pause idle checks on themselves for afk_command_seconds. The timer starts from zero when it ends.";

    [JsonPropertyName("afk_command_enabled")]
    public bool AfkCommandEnabled { get; set; } = true;

    [JsonPropertyName("afk_command_seconds")]
    public int AfkCommandSeconds { get; set; } = 180;

    [JsonPropertyName("_help_admins")]
    public string HelpAdmins { get; set; } = "admin_immune skips anyone holding one of admin_roles. notify_admins tells those same people about idle warnings, moves, kicks, and !afk use.";

    [JsonPropertyName("admin_immune")]
    public bool AdminImmune { get; set; } = true;

    [JsonPropertyName("admin_roles")]
    public List<string> AdminRoles { get; set; } = ["@css/root", "@css/generic"];

    [JsonPropertyName("notify_admins")]
    public bool NotifyAdmins { get; set; } = true;

    [JsonPropertyName("_help_announce_moves")]
    public string HelpAnnounce { get; set; } = "Tell everyone in chat when a player gets moved to spectator.";

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
        AdminRoles = (AdminRoles ?? []).Select(r => r.Trim()).Where(r => r.Length > 0).Distinct().ToList();
    }
}
