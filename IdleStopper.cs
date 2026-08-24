using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.UserMessages;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.ValveConstants.Protobuf;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace IdleStopper;

[MinimumApiVersion(364)]
public sealed class IdleStopper : BasePlugin, IPluginConfig<IdleStopperConfig>
{
    public override string ModuleName => "CS2-IdleStopper";
    public override string ModuleVersion => "1.5.0";
    public override string ModuleAuthor => "BONE";
    public override string ModuleDescription => "Warns, shakes, then moves or kicks players who stop pressing keys.";

    public IdleStopperConfig Config { get; set; } = new();

    // Slot -> seconds without input. Everything runs on the game thread from one timer,
    // so a plain dictionary is fine.
    private readonly Dictionary<int, int> _idle = new();
    // Center panel text per slot. The html panel only lives for a frame, so OnTick resends it.
    private readonly Dictionary<int, string> _center = new();
    // Slot -> seconds of !afk grace left.
    private readonly Dictionary<int, int> _afk = new();
    // Slots that pressed something this round. Only used with round_start_only.
    private readonly HashSet<int> _movedThisRound = new();
    // Slot -> rounds sat in spectator after the plugin put them there.
    private readonly Dictionary<int, int> _specRounds = new();
    private Timer? _tick;
    private CCSGameRules? _rules;

    public void OnConfigParsed(IdleStopperConfig config)
    {
        config.Sanitize();
        Config = config;
    }

    public override void Load(bool hotReload)
    {
        RegisterListener<Listeners.OnMapStart>(_ => ClearAll());
        RegisterListener<Listeners.OnMapEnd>(ClearAll);
        RegisterListener<Listeners.OnClientDisconnect>(slot =>
        {
            _idle.Remove(slot); _center.Remove(slot); _afk.Remove(slot); _movedThisRound.Remove(slot); _specRounds.Remove(slot);
        });
        RegisterEventHandler<EventRoundStart>((_, _) => { OnRoundStart(); return HookResult.Continue; });
        RegisterListener<Listeners.OnTick>(OnTick);
        RegisterListener<Listeners.OnClientPutInServer>(slot => _idle[slot] = 0);
        RegisterEventHandler<EventPlayerSpawn>((e, _) => { ClearWarning(e.Userid); return HookResult.Continue; });
        RegisterEventHandler<EventPlayerTeam>((e, _) => { ClearWarning(e.Userid); return HookResult.Continue; });

        // Hot reload keeps whoever is already on the server, so start them fresh.
        ClearAll();
        _tick = AddTimer(1.0f, Tick, TimerFlags.REPEAT);
    }

    public override void Unload(bool hotReload)
    {
        _tick?.Kill();
        _tick = null;
        ClearAll();
    }

    private void ClearAll()
    {
        _rules = null;
        _idle.Clear();
        _center.Clear();
        _afk.Clear();
        _movedThisRound.Clear();
        _specRounds.Clear();
    }

    private void OnRoundStart()
    {
        _movedThisRound.Clear();

        if (Config.SpectatorKickRounds <= 0)
        {
            _specRounds.Clear();
            return;
        }

        foreach (var slot in _specRounds.Keys.ToList())
        {
            var player = Utilities.GetPlayerFromSlot(slot);
            if (player is null || !player.IsValid || player.Team != CsTeam.Spectator)
            {
                // Gone, or picked a team again. Either way stop counting.
                _specRounds.Remove(slot);
                continue;
            }

            if (++_specRounds[slot] < Config.SpectatorKickRounds)
                continue;

            _specRounds.Remove(slot);
            NotifyAdmins($"{player.PlayerName} was kicked after {Config.SpectatorKickRounds} rounds idle in spectator.");
            player.Disconnect(NetworkDisconnectionReason.NETWORK_DISCONNECT_KICKED_IDLE);
        }
    }

    [ConsoleCommand("css_afk", "Pause idle checks on yourself for a while.")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnAfkCommand(CCSPlayerController? player, CommandInfo _)
    {
        if (player is null || !player.IsValid)
            return;

        if (!Config.AfkCommandEnabled)
        {
            player.PrintToChat($" {ChatColors.Purple}[IdleStopper] The !afk command is disabled on this server.");
            return;
        }

        _afk[player.Slot] = Config.AfkCommandSeconds;
        Reset(player.Slot);
        player.PrintToChat($" {ChatColors.Purple}[IdleStopper] Idle checks paused for you for {Config.AfkCommandSeconds} seconds.");
        NotifyAdmins($"{player.PlayerName} used !afk ({Config.AfkCommandSeconds}s).");
    }

    private void OnTick()
    {
        if (_center.Count == 0)
            return;

        foreach (var (slot, html) in _center)
        {
            var player = Utilities.GetPlayerFromSlot(slot);
            if (player is not null && player.IsValid)
                player.PrintToCenterHtml(html, 1);
        }
    }

    private void Tick()
    {
        if (InWarmup())
        {
            if (_idle.Count > 0) _idle.Clear();
            _center.Clear();
            return;
        }

        // Count down !afk grace. When it runs out the player starts from a clean slate.
        foreach (var slot in _afk.Keys.ToList())
        {
            if (--_afk[slot] > 0)
                continue;

            _afk.Remove(slot);
            Reset(slot);
            var player = Utilities.GetPlayerFromSlot(slot);
            if (player is not null && player.IsValid)
                player.PrintToChat($" {ChatColors.Purple}[IdleStopper] Your !afk time is over. Idle checks are back on.");
        }

        foreach (var player in Utilities.GetPlayers())
        {
            if (!player.IsValid || player.IsBot || player.IsHLTV || player.Connected != PlayerConnectedState.PlayerConnected)
                continue;

            var slot = player.Slot;

            if (_afk.ContainsKey(slot) || (Config.AdminImmune && IsAdmin(player)))
            {
                Reset(slot);
                continue;
            }

            // Only count people who could actually be playing.
            if (!player.PawnIsAlive || (player.Team != CsTeam.Terrorist && player.Team != CsTeam.CounterTerrorist))
            {
                Reset(slot);
                continue;
            }

            if (player.Buttons != 0)
            {
                _movedThisRound.Add(slot);
                Reset(slot);
                continue;
            }

            // Round start only: once they've done anything this round, leave them alone.
            if (Config.RoundStartOnly && _movedThisRound.Contains(slot))
            {
                Reset(slot);
                continue;
            }

            var idle = _idle.GetValueOrDefault(slot) + 1;
            _idle[slot] = idle;

            if (idle < Config.NotifySeconds)
                continue;

            if (idle >= Config.ActionSeconds)
            {
                Reset(slot);
                Punish(player);
                continue;
            }

            var left = Config.ActionSeconds - idle;

            var sinceNotify = idle - Config.NotifySeconds;

            if (Config.SoundEnabled && (sinceNotify == 0 || (Config.SoundIntervalSeconds > 0 && sinceNotify % Config.SoundIntervalSeconds == 0)))
                player.ExecuteClientCommand("play " + Config.Sound);

            if (sinceNotify == 0)
            {
                // Chat mode only says it once, so the seconds here are the full countdown.
                if (!Config.CenterMessage)
                    player.PrintToChat($" {ChatColors.Purple}[IdleStopper] You are idle. You will be {Outcome()} in {left} seconds.{AfkHint()}");

                NotifyAdmins($"{player.PlayerName} is idle, {Outcome()} in {left}s.");
            }

            if (Config.ShakeEnabled && sinceNotify % 2 == 0)
                Shake(player);

            if (Config.CenterMessage)
                _center[slot] =
                    $"<font color='#ff4444'><b>YOU ARE IDLE</b></font><br>" +
                    $"<font color='#ffffff'>Press any key or you will be {Outcome()} in</font> " +
                    $"<font color='#ffcc00'><b>{left}</b></font>" +
                    (Config.AfkCommandEnabled ? $"<br><font color='#aaaaaa'>Type !afk to become immune for {Pretty(Config.AfkCommandSeconds)}</font>" : "");
        }
    }

    private void Punish(CCSPlayerController player)
    {
        var name = player.PlayerName;
        switch (Config.ActionType)
        {
            case 1:
                player.PrintToChat($" {ChatColors.Red}[IdleStopper]{ChatColors.Default} You were moved to spectator for being idle.");
                player.ChangeTeam(CsTeam.Spectator);
                if (Config.SpectatorKickRounds > 0)
                    _specRounds[player.Slot] = 0;
                if (Config.AnnounceMoves)
                    Server.PrintToChatAll($" {ChatColors.Purple}[IdleStopper]{ChatColors.Default} {name} was moved to spectator for being idle.");
                NotifyAdmins($"{name} was moved to spectator for being idle.");
                break;
            case 2:
                NotifyAdmins($"{name} was kicked for being idle.");
                player.Disconnect(NetworkDisconnectionReason.NETWORK_DISCONNECT_KICKED_IDLE);
                break;
        }
    }

    private bool IsAdmin(CCSPlayerController player)
    {
        foreach (var role in Config.AdminRoles)
            if (AdminManager.PlayerHasPermissions(player, role))
                return true;
        return false;
    }

    private void NotifyAdmins(string text)
    {
        if (!Config.NotifyAdmins)
            return;

        foreach (var admin in Utilities.GetPlayers())
        {
            if (admin.IsValid && !admin.IsBot && !admin.IsHLTV && IsAdmin(admin))
                admin.PrintToChat($" {ChatColors.Olive}[IdleStopper admin]{ChatColors.Default} {text}");
        }
    }

    private void Reset(int slot)
    {
        _idle[slot] = 0;
        _center.Remove(slot);
    }

    // Sent straight to the one client, so nobody nearby feels it and no entity is spawned.
    private static void Shake(CCSPlayerController player)
    {
        var msg = UserMessage.FromPartialName("Shake");
        msg.SetUInt("command", 0);
        msg.SetFloat("local_amplitude", 8f);
        msg.SetFloat("frequency", 40f);
        msg.SetFloat("duration", 1f);
        msg.Recipients.Add(player);
        msg.Send();
    }

    private bool InWarmup()
    {
        if (_rules is null)
            _rules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault()?.GameRules;

        return _rules?.WarmupPeriod ?? false;
    }

    private void ClearWarning(CCSPlayerController? player)
    {
        if (player is not null && player.IsValid)
            Reset(player.Slot);
    }

    private string AfkHint() =>
        Config.AfkCommandEnabled ? $" Type !afk to become immune for {Pretty(Config.AfkCommandSeconds)}." : "";

    // 50 seconds / 1 minute / 2 minutes & 14 seconds
    private static string Pretty(int total)
    {
        var m = total / 60;
        var sec = total % 60;
        var mins = m == 0 ? "" : m == 1 ? "1 minute" : $"{m} minutes";
        var secs = sec == 0 ? "" : sec == 1 ? "1 second" : $"{sec} seconds";
        if (mins.Length == 0) return secs;
        if (secs.Length == 0) return mins;
        return $"{mins} & {secs}";
    }

    private string Outcome() => Config.ActionType switch
    {
        1 => "moved to spectator",
        2 => "kicked",
        _ => "warned again"
    };
}
