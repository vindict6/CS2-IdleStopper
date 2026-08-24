using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
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
    public override string ModuleVersion => "1.3.0";
    public override string ModuleAuthor => "BONE";
    public override string ModuleDescription => "Warns, shakes, then moves or kicks players who stop pressing keys.";

    public IdleStopperConfig Config { get; set; } = new();

    // Slot -> seconds without input. Everything runs on the game thread from one timer,
    // so a plain dictionary is fine.
    private readonly Dictionary<int, int> _idle = new();
    // Center panel text per slot. The html panel only lives for a frame, so OnTick resends it.
    private readonly Dictionary<int, string> _center = new();
    private Timer? _tick;
    private CCSGameRules? _rules;

    public void OnConfigParsed(IdleStopperConfig config)
    {
        config.Sanitize();
        Config = config;
    }

    public override void Load(bool hotReload)
    {
        RegisterListener<Listeners.OnMapStart>(_ => { _rules = null; _idle.Clear(); _center.Clear(); });
        RegisterListener<Listeners.OnMapEnd>(() => { _rules = null; _idle.Clear(); _center.Clear(); });
        RegisterListener<Listeners.OnClientDisconnect>(slot => { _idle.Remove(slot); _center.Remove(slot); });
        RegisterListener<Listeners.OnTick>(OnTick);
        RegisterListener<Listeners.OnClientPutInServer>(slot => _idle[slot] = 0);
        RegisterEventHandler<EventPlayerSpawn>((e, _) => { ClearWarning(e.Userid); return HookResult.Continue; });
        RegisterEventHandler<EventPlayerTeam>((e, _) => { ClearWarning(e.Userid); return HookResult.Continue; });

        // Hot reload keeps whoever is already on the server, so start them fresh.
        _idle.Clear();
        _center.Clear();
        _tick = AddTimer(1.0f, Tick, TimerFlags.REPEAT);
    }

    public override void Unload(bool hotReload)
    {
        _tick?.Kill();
        _tick = null;
        _idle.Clear();
        _center.Clear();
        _rules = null;
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

        foreach (var player in Utilities.GetPlayers())
        {
            if (!player.IsValid || player.IsBot || player.IsHLTV || player.Connected != PlayerConnectedState.PlayerConnected)
                continue;

            var slot = player.Slot;

            // Only count people who could actually be playing.
            if (!player.PawnIsAlive || (player.Team != CsTeam.Terrorist && player.Team != CsTeam.CounterTerrorist))
            {
                Reset(slot);
                continue;
            }

            if (player.Buttons != 0)
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
                    player.PrintToChat($" {ChatColors.Purple}[IdleStopper] You are idle. You will be {Outcome()} in {left} seconds.");
            }

            if (Config.ShakeEnabled && sinceNotify % 2 == 0)
                Shake(player);

            if (Config.CenterMessage)
                _center[slot] =
                    $"<font color='#ff4444'><b>YOU ARE IDLE</b></font><br>" +
                    $"<font color='#ffffff'>Press any key or you will be {Outcome()} in</font> " +
                    $"<font color='#ffcc00'><b>{left}</b></font>";
        }
    }

    private void Punish(CCSPlayerController player)
    {
        switch (Config.ActionType)
        {
            case 1:
                player.PrintToChat($" {ChatColors.Red}[IdleStopper]{ChatColors.Default} You were moved to spectator for being idle.");
                player.ChangeTeam(CsTeam.Spectator);
                break;
            case 2:
                player.Disconnect(NetworkDisconnectionReason.NETWORK_DISCONNECT_KICKED_IDLE);
                break;
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

    private string Outcome() => Config.ActionType switch
    {
        1 => "moved to spectator",
        2 => "kicked",
        _ => "warned again"
    };
}
