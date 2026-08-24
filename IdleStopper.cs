using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.ValveConstants.Protobuf;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace IdleStopper;

[MinimumApiVersion(364)]
public sealed class IdleStopper : BasePlugin, IPluginConfig<IdleStopperConfig>
{
    public override string ModuleName => "CS2-IdleStopper";
    public override string ModuleVersion => "1.0.0";
    public override string ModuleAuthor => "BONE";
    public override string ModuleDescription => "Warns, slaps, then moves or kicks players who stop pressing keys.";

    public IdleStopperConfig Config { get; set; } = new();

    // Slot -> seconds without input. Everything runs on the game thread from one timer,
    // so a plain dictionary is fine.
    private readonly Dictionary<int, int> _idle = new();
    private readonly Random _random = new();
    private Timer? _tick;
    private CCSGameRules? _rules;

    public void OnConfigParsed(IdleStopperConfig config)
    {
        config.Sanitize();
        Config = config;
    }

    public override void Load(bool hotReload)
    {
        RegisterListener<Listeners.OnMapStart>(_ => { _rules = null; _idle.Clear(); });
        RegisterListener<Listeners.OnMapEnd>(() => { _rules = null; _idle.Clear(); });
        RegisterListener<Listeners.OnClientDisconnect>(slot => _idle.Remove(slot));
        RegisterListener<Listeners.OnClientPutInServer>(slot => _idle[slot] = 0);
        RegisterEventHandler<EventPlayerSpawn>((e, _) => { ClearWarning(e.Userid); return HookResult.Continue; });
        RegisterEventHandler<EventPlayerTeam>((e, _) => { ClearWarning(e.Userid); return HookResult.Continue; });

        // Hot reload keeps whoever is already on the server, so start them fresh.
        _idle.Clear();
        _tick = AddTimer(1.0f, Tick, TimerFlags.REPEAT);
    }

    public override void Unload(bool hotReload)
    {
        _tick?.Kill();
        _tick = null;
        _idle.Clear();
        _rules = null;
    }

    private void Tick()
    {
        if (InWarmup())
        {
            if (_idle.Count > 0) _idle.Clear();
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
                _idle[slot] = 0;
                continue;
            }

            if (player.Buttons != 0)
            {
                _idle[slot] = 0;
                continue;
            }

            var idle = _idle.GetValueOrDefault(slot) + 1;
            _idle[slot] = idle;

            if (idle < Config.NotifySeconds)
                continue;

            if (idle >= Config.ActionSeconds)
            {
                _idle[slot] = 0;
                Punish(player);
                continue;
            }

            var left = Config.ActionSeconds - idle;

            if (idle == Config.NotifySeconds)
            {
                if (Config.SoundEnabled)
                    player.ExecuteClientCommand("play " + Config.Sound);

                // Chat mode only says it once, so the seconds here are the full countdown.
                if (!Config.CenterMessage)
                    player.PrintToChat($" {ChatColors.Purple}[IdleStopper] You are idle. You will be {Outcome()} in {left} seconds.");
            }

            if (Config.SlapEnabled && (idle - Config.NotifySeconds) % 2 == 0)
                Slap(player);

            if (Config.CenterMessage)
                player.PrintToCenterHtml(
                    $"<font color='#ff4444'><b>YOU ARE IDLE</b></font><br>" +
                    $"<font color='#ffffff'>Press any key or you will be {Outcome()} in</font> " +
                    $"<font color='#ffcc00'><b>{left}</b></font>", 1);
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

    // Shove with zero damage. Just enough to shake the screen and maybe unstick a bot-like stand.
    private void Slap(CCSPlayerController player)
    {
        var pawn = player.PlayerPawn.Value;
        if (pawn is null || !pawn.IsValid || pawn.AbsVelocity is null)
            return;

        var vel = pawn.AbsVelocity;
        vel.X += _random.Next(-180, 181);
        vel.Y += _random.Next(-180, 181);
        vel.Z += _random.Next(200, 300);
        pawn.Teleport(null, null, vel);
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
            _idle[player.Slot] = 0;
    }

    private string Outcome() => Config.ActionType switch
    {
        1 => "moved to spectator",
        2 => "kicked",
        _ => "warned again"
    };
}
