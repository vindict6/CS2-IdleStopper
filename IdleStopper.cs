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
    public override string ModuleVersion => "1.8.0";
    public override string ModuleAuthor => "BONE";
    public override string ModuleDescription => "Warns, shakes, then moves or kicks players who stop pressing keys.";

    // Degrees of aim movement in one frame that counts as "they're at the keyboard".
    // Small on purpose: a nudge of the mouse should clear the warning.
    private const float AimTolerance = 0.35f;

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
    // Slots worth sampling every frame, rebuilt once a second by the timer.
    private readonly List<int> _watch = new();
    // Slots that did something since the last second. Filled every frame.
    private readonly HashSet<int> _active = new();
    // Last aim angles per slot, so mouse movement counts as being there.
    private readonly Dictionary<int, (float Pitch, float Yaw)> _aim = new();
    // SteamID -> what they had when we took them out. Dropped when the match ends.
    private readonly Dictionary<ulong, Loadout> _saved = new();
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
            _idle.Remove(slot); _center.Remove(slot); _afk.Remove(slot); _movedThisRound.Remove(slot);
            _specRounds.Remove(slot); _watch.Remove(slot); _active.Remove(slot); _aim.Remove(slot);
        });
        RegisterEventHandler<EventRoundStart>((_, _) => { OnRoundStart(); return HookResult.Continue; });
        RegisterEventHandler<EventCsWinPanelMatch>((_, _) => { _saved.Clear(); return HookResult.Continue; });

        // Chat commands normally get echoed to everyone first. Swallow ours instead.
        AddCommandListener("say", OnSay, HookMode.Pre);
        AddCommandListener("say_team", OnSay, HookMode.Pre);
        RegisterListener<Listeners.OnTick>(OnTick);
        RegisterListener<Listeners.OnClientPutInServer>(slot => _idle[slot] = 0);
        RegisterEventHandler<EventPlayerSpawn>((e, _) => { ClearWarning(e.Userid); RestoreLater(e.Userid); return HookResult.Continue; });
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
        _watch.Clear();
        _active.Clear();
        _aim.Clear();
        _saved.Clear();
    }

    private HookResult OnSay(CCSPlayerController? player, CommandInfo info)
    {
        if (player is null || !player.IsValid)
            return HookResult.Continue;

        var said = info.GetArg(1).Trim().Trim('"');
        if (said.Length > 1 && (said[0] == '!' || said[0] == '/'))
            said = said[1..];

        if (!said.Equals("afk", StringComparison.OrdinalIgnoreCase))
            return HookResult.Continue;

        DoAfk(player);
        return HookResult.Handled;
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

        DoAfk(player);
    }

    private void DoAfk(CCSPlayerController player)
    {
        if (!Config.AfkCommandEnabled)
        {
            player.PrintToChat($" {ChatColors.Purple}[IdleStopper] The !afk command is disabled on this server.");
            return;
        }

        _afk[player.Slot] = Config.AfkCommandSeconds;
        Reset(player.Slot);
        player.PrintToChat($" {ChatColors.Purple}[IdleStopper] Idle checks paused for you for {Pretty(Config.AfkCommandSeconds)}.");
        NotifyAdmins($"{player.PlayerName} used !afk ({Config.AfkCommandSeconds}s).");
    }

    // Buttons are only held for a few frames, so a once-a-second look misses taps.
    // Sample every frame instead, and treat aim movement as input too.
    private void OnTick()
    {
        foreach (var slot in _watch)
        {
            if (_active.Contains(slot))
                continue;

            var player = Utilities.GetPlayerFromSlot(slot);
            if (player is null || !player.IsValid)
                continue;

            if (player.Buttons != 0)
            {
                _active.Add(slot);
                continue;
            }

            var pawn = player.PlayerPawn.Value;
            var eyes = pawn is not null && pawn.IsValid ? pawn.EyeAngles : null;
            if (eyes is null)
                continue;

            var aim = (eyes.X, eyes.Y);
            if (_aim.TryGetValue(slot, out var last) && Moved(last, aim))
                _active.Add(slot);

            _aim[slot] = aim;
        }

        foreach (var (slot, html) in _center)
        {
            var player = Utilities.GetPlayerFromSlot(slot);
            if (player is not null && player.IsValid)
                player.PrintToCenterHtml(html, 1);
        }
    }

    private static bool Moved((float Pitch, float Yaw) a, (float Pitch, float Yaw) b)
    {
        return Math.Abs(Wrap(a.Pitch - b.Pitch)) + Math.Abs(Wrap(a.Yaw - b.Yaw)) > AimTolerance;
    }

    private static float Wrap(float degrees)
    {
        while (degrees > 180.0f) degrees -= 360.0f;
        while (degrees < -180.0f) degrees += 360.0f;
        return degrees;
    }

    private void Tick()
    {
        if (InWarmup())
        {
            if (_idle.Count > 0) _idle.Clear();
            _center.Clear();
            _watch.Clear();
            _active.Clear();
            return;
        }

        _watch.Clear();

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

            // Sampled by OnTick all through the last second.
            _watch.Add(slot);

            if (_active.Contains(slot))
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

            // Sound and shake share the same beat: once at notify, then every sound_interval_seconds.
            var onBeat = sinceNotify == 0 || (Config.SoundIntervalSeconds > 0 && sinceNotify % Config.SoundIntervalSeconds == 0);

            if (onBeat && Config.SoundEnabled)
                player.ExecuteClientCommand("play " + Config.Sound);

            if (onBeat && Config.ShakeEnabled)
                Shake(player);

            if (sinceNotify == 0)
            {
                // Chat mode only says it once, so the seconds here are the full countdown.
                if (!Config.CenterMessage)
                    player.PrintToChat($" {ChatColors.Purple}[IdleStopper] You are idle. You will be {Outcome()} in {left} seconds.{AfkHint()}");

                NotifyAdmins($"{player.PlayerName} is idle, {Outcome()} in {left}s.");
            }

            if (Config.CenterMessage)
                _center[slot] =
                    $"<font color='#ff4444'><b>YOU ARE IDLE</b></font><br>" +
                    $"<font color='#ffffff'>Press any key or you will be {Outcome()} in</font> " +
                    $"<font color='#ffcc00'><b>{left}</b></font>" +
                    (Config.AfkCommandEnabled ? $"<br><font color='#aaaaaa'>Type !afk to become immune for {Pretty(Config.AfkCommandSeconds)}</font>" : "");
        }

        _active.Clear();
    }

    private void Punish(CCSPlayerController player)
    {
        var name = player.PlayerName;

        // Grab this before the pawn goes away.
        if (Config.KeepLoadout && Config.ActionType != 0)
            Save(player);

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

    private static ulong KeyOf(CCSPlayerController player)
    {
        return player.AuthorizedSteamID?.SteamId64 ?? player.SteamID;
    }

    private void Save(CCSPlayerController player)
    {
        var key = KeyOf(player);
        if (key == 0)
            return;

        var money = player.InGameMoneyServices?.Account ?? 0;
        var guns = new List<Gun>();

        var weapons = player.PlayerPawn.Value?.WeaponServices?.MyWeapons;
        if (weapons is not null)
        {
            foreach (var handle in weapons)
            {
                var weapon = handle.Value;
                if (weapon is null || !weapon.IsValid)
                    continue;

                var name = weapon.DesignerName;
                if (string.IsNullOrEmpty(name))
                    continue;

                guns.Add(new Gun(
                    name,
                    weapon.AttributeManager?.Item?.ItemDefinitionIndex ?? 0,
                    weapon.FallbackPaintKit,
                    weapon.FallbackSeed,
                    weapon.FallbackWear,
                    weapon.FallbackStatTrak));
            }
        }

        _saved[key] = new Loadout(money, guns);
    }

    // Spawn is too early to hand out weapons, so wait a beat and re-resolve the player.
    private void RestoreLater(CCSPlayerController? player)
    {
        if (!Config.KeepLoadout || player is null || !player.IsValid || _saved.Count == 0)
            return;

        var key = KeyOf(player);
        if (key == 0 || !_saved.ContainsKey(key))
            return;

        var slot = player.Slot;
        AddTimer(0.3f, () =>
        {
            var target = Utilities.GetPlayerFromSlot(slot);
            if (target is null || !target.IsValid || KeyOf(target) != key)
                return;

            Restore(target, key);
        });
    }

    private void Restore(CCSPlayerController player, ulong key)
    {
        if (!_saved.TryGetValue(key, out var saved))
            return;

        // One shot. If anything below fails they just keep their spawn kit.
        _saved.Remove(key);

        if (!player.PawnIsAlive)
            return;

        var money = player.InGameMoneyServices;
        if (money is not null)
        {
            money.Account = saved.Money;
            Utilities.SetStateChanged(player, "CCSPlayerController", "m_pInGameMoneyServices");
        }

        if (saved.Guns.Count == 0)
            return;

        player.RemoveWeapons();

        foreach (var gun in saved.Guns)
        {
            var weapon = player.GiveNamedItem<CBasePlayerWeapon>(gun.Name);
            if (weapon is null || !weapon.IsValid)
                continue;

            if (gun.PaintKit == 0 && gun.StatTrak == 0)
                continue;

            // Put the skin back on the fresh weapon.
            weapon.FallbackPaintKit = gun.PaintKit;
            weapon.FallbackSeed = gun.Seed;
            weapon.FallbackWear = gun.Wear;
            weapon.FallbackStatTrak = gun.StatTrak;

            var item = weapon.AttributeManager?.Item;
            if (item is not null)
            {
                item.ItemDefinitionIndex = gun.DefIndex;
                item.Initialized = true;
                Utilities.SetStateChanged(weapon, "CEconEntity", "m_AttributeManager");
            }

            Utilities.SetStateChanged(weapon, "CEconEntity", "m_nFallbackPaintKit");
            Utilities.SetStateChanged(weapon, "CEconEntity", "m_nFallbackSeed");
            Utilities.SetStateChanged(weapon, "CEconEntity", "m_flFallbackWear");
            Utilities.SetStateChanged(weapon, "CEconEntity", "m_nFallbackStatTrak");
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
        msg.SetUInt("command", 0); // SHAKE_START
        msg.SetFloat("amplitude", 20f);
        msg.SetFloat("frequency", 60f);
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

    private sealed record Gun(string Name, ushort DefIndex, int PaintKit, int Seed, float Wear, int StatTrak);

    private sealed record Loadout(int Money, List<Gun> Guns);

    private string Outcome() => Config.ActionType switch
    {
        1 => "moved to spectator",
        2 => "kicked",
        _ => "warned again"
    };
}
