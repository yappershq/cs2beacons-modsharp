using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sharp.Modules.AdminManager.Shared;
using Sharp.Modules.LocalizerManager.Shared;
using Sharp.Shared;
using Sharp.Shared.Enums;
using Sharp.Shared.GameEntities;
using Sharp.Shared.GameEvents;
using Sharp.Shared.Listeners;
using Sharp.Shared.Managers;
using Sharp.Shared.Objects;
using Sharp.Shared.Types;
using Sharp.Shared.Units;

namespace CS2Beacons;

/// <summary>
///     CS2Beacons — admin <c>beacon</c> command toggles a pulsing, beeping particle
///     beacon on a target player. Ported from the CounterStrikeSharp plugin
///     <c>yappersHQ/cs2beacons</c> to ModSharp.
///
///     - Spawns an <c>info_particle_system</c> (team-colored vpcf), teleports it to the
///       pawn origin + height offset, parents it to the pawn, and Starts it.
///     - A single repeating timer beeps every active beacon on an interval.
///     - Per-slot <see cref="EntityIndex" /> tracking; cleared on death, disconnect,
///       and round restart.
///     - Publishes <see cref="ICS2BeaconsApi" /> for other plugins.
/// </summary>
internal sealed class CS2BeaconsPlugin
    : IModSharpModule, ICS2BeaconsApi, IGameListener, IEventListener, IClientListener, IEntityListener
{
    public string DisplayName   => "CS2Beacons";
    public string DisplayAuthor => "yappersHQ";

    private const string ModuleIdentity = nameof(CS2Beacons);

    /// <summary>Locale file name (without extension) under <c>{sharp}/locales/</c>.</summary>
    private const string LocaleName = "cs2beacons";

    // Locale keys (declared in .assets/locales/cs2beacons.json).
    private const string KeyUsage           = "Beacon_Usage";
    private const string KeyTargetsEmpty    = "Beacon_TargetsEmpty";
    private const string KeyMultipleTargets = "Beacon_MultipleTargets";
    private const string KeyNoPermission    = "Beacon_NoPermission";
    private const string KeyToggled         = "Beacon_Toggled";

    private readonly ILogger<CS2BeaconsPlugin> _logger;
    private readonly IModSharp                 _modSharp;
    private readonly IClientManager            _clientManager;
    private readonly IEntityManager            _entityManager;
    private readonly IEventManager             _eventManager;
    private readonly ISharpModuleManager       _sharpModuleManager;
    private readonly string                    _sharpPath;

    private CS2BeaconsConfig _config = new();

    private IAdminManager?     _adminManager;
    private ILocalizerManager? _localizer;

    /// <summary>Per-slot particle entity index. <see cref="EntityIndex.InvalidIndex" /> = no beacon.</summary>
    private readonly EntityIndex[] _beacons = new EntityIndex[64];

    /// <summary>Handle of the repeating beep timer, when running.</summary>
    private Guid? _beepTimer;

    public CS2BeaconsPlugin(
        ISharedSystem   sharedSystem,
        string?         dllPath,
        string?         sharpPath,
        Version?        version,
        IConfiguration? coreConfiguration,
        bool            hotReload)
    {
        _logger             = sharedSystem.GetLoggerFactory().CreateLogger<CS2BeaconsPlugin>();
        _modSharp           = sharedSystem.GetModSharp();
        _clientManager      = sharedSystem.GetClientManager();
        _entityManager      = sharedSystem.GetEntityManager();
        _eventManager       = sharedSystem.GetEventManager();
        _sharpModuleManager = sharedSystem.GetSharpModuleManager();
        _sharpPath          = sharpPath ?? "";

        Array.Fill(_beacons, EntityIndex.InvalidIndex);
    }

    #region Lifecycle

    public bool Init()
        => true;

    public void PostInit()
    {
        _config = CS2BeaconsConfig.Load(_sharpPath, _logger);

        _eventManager.HookEvent("player_death");
        _eventManager.InstallEventListener(this);
        _modSharp.InstallGameListener(this);
        _clientManager.InstallClientListener(this);
        _entityManager.InstallEntityListener(this);

        // Virtual command — registered as "beacon" (no css_ prefix in ModSharp).
        _clientManager.InstallCommandCallback("beacon", OnBeaconCommand);

        // Publish the shared API so other plugins can resolve it.
        _sharpModuleManager.RegisterSharpModuleInterface<ICS2BeaconsApi>(this, ICS2BeaconsApi.Identity, this);
    }

    public void OnAllModulesLoaded()
    {
        // Resolve AdminManager + LocalizerManager here — publishers finish PostInit before any OAM fires.
        _adminManager = _sharpModuleManager
            .GetOptionalSharpModuleInterface<IAdminManager>(IAdminManager.Identity)?.Instance;

        _localizer = _sharpModuleManager
            .GetOptionalSharpModuleInterface<ILocalizerManager>(ILocalizerManager.Identity)?.Instance;

        if (_localizer is null)
        {
            _logger.LogWarning(
                "LocalizerManager not found — beacon messages will fall back to plain text. Is the LocalizerManager module loaded?");
        }
        else
        {
            _localizer.LoadLocaleFile(LocaleName, suppressDuplicationWarnings: true);
        }

        if (_adminManager is null)
        {
            _logger.LogWarning(
                "AdminManager not found — beacon command will be admin-gated but no permission can resolve. Is the AdminManager module loaded?");
        }
        else
        {
            // Register our custom permission, else even a root "*" admin cannot resolve it.
            _adminManager.MountAdminManifest(ModuleIdentity, () => new AdminTableManifest(
                new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    [ModuleIdentity] = new(StringComparer.OrdinalIgnoreCase)
                    {
                        _config.BeaconCommandPermission,
                    },
                },
                [],
                []));
        }

        _logger.LogInformation("CS2Beacons module loaded");
    }

    public void OnLibraryConnected(string name)
    {
        if (name != ILocalizerManager.Identity && name != "LocalizerManager")
            return;

        _localizer = _sharpModuleManager
            .GetOptionalSharpModuleInterface<ILocalizerManager>(ILocalizerManager.Identity)?.Instance;

        _localizer?.LoadLocaleFile(LocaleName, suppressDuplicationWarnings: true);
    }

    public void OnLibraryDisconnect(string name)
    {
        if (name == ILocalizerManager.Identity || name == "LocalizerManager")
            _localizer = null;
    }

    /// <summary>
    ///     Sends a localized, color-processed line to a single client's chat.
    ///     Falls back to the raw key when LocalizerManager is unavailable.
    /// </summary>
    private void PrintLocalized(IGameClient client, string key, params object?[] args)
    {
        if (_localizer is not { } mgr)
        {
            client.Print(HudPrintChannel.Chat, key);
            return;
        }

        mgr.For(client)
            .Localized(key, args)
            .Prefix(null)
            .Transform(ChatFormat.ProcessColorCodes)
            .Print();
    }

    public void Shutdown()
    {
        _clientManager.RemoveCommandCallback("beacon", OnBeaconCommand);
        _clientManager.RemoveClientListener(this);
        _eventManager.RemoveEventListener(this);
        _modSharp.RemoveGameListener(this);
        _entityManager.RemoveEntityListener(this);

        ClearAllBeacons();
    }

    #endregion

    #region IGameListener

    int IGameListener.ListenerVersion  => IGameListener.ApiVersion;
    int IGameListener.ListenerPriority => 0;

    void IGameListener.OnResourcePrecache()
    {
        _modSharp.PrecacheResource(_config.BeaconSoundevtsPath);
        _modSharp.PrecacheResource(_config.BeaconParticleCt);
        _modSharp.PrecacheResource(_config.BeaconParticleT);
    }

    void IGameListener.OnRoundRestart()
        => ClearAllBeacons();

    void IGameListener.OnGameShutdown()
        => ClearAllBeacons();

    #endregion

    #region IEventListener — player_death cleanup

    int IEventListener.ListenerVersion  => IEventListener.ApiVersion;
    int IEventListener.ListenerPriority => 0;

    void IEventListener.FireGameEvent(IGameEvent @event)
    {
        if (@event.Name != "player_death")
            return;

        if (@event.GetPlayerController("userid") is not { } controller)
            return;

        if (controller.GetGameClient() is not { } client)
            return;

        RemoveBeacon(client);
    }

    #endregion

    #region IClientListener — disconnect cleanup

    int IClientListener.ListenerVersion  => IClientListener.ApiVersion;
    int IClientListener.ListenerPriority => 0;

    void IClientListener.OnClientConnected(IGameClient client)                                          { }
    void IClientListener.OnClientPutInServer(IGameClient client)                                        { }
    void IClientListener.OnClientPostAdminCheck(IGameClient client)                                     { }
    void IClientListener.OnClientSettingChanged(IGameClient client)                                     { }
    void IClientListener.OnAdminCacheReload()                                                           { }
    void IClientListener.OnClientDisconnecting(IGameClient client, NetworkDisconnectionReason reason)   { }

    void IClientListener.OnClientDisconnected(IGameClient client, NetworkDisconnectionReason reason)
        => RemoveBeacon(client);

    #endregion

    #region IEntityListener — drop stale indices when the engine deletes a beacon

    int IEntityListener.ListenerVersion  => IEntityListener.ApiVersion;
    int IEntityListener.ListenerPriority => 0;

    void IEntityListener.OnEntityCreated(IBaseEntity entity)                 { }
    void IEntityListener.OnEntitySpawned(IBaseEntity entity)                 { }
    void IEntityListener.OnEntityFollowed(IBaseEntity entity, IBaseEntity? owner) { }

    void IEntityListener.OnEntityDeleted(IBaseEntity entity)
    {
        var index = entity.Index;

        for (var slot = 0; slot < _beacons.Length; slot++)
        {
            if (_beacons[slot] != index)
                continue;

            _beacons[slot] = EntityIndex.InvalidIndex;
            StopBeepTimerIfIdle();
            return;
        }
    }

    #endregion

    #region Command

    private ECommandAction OnBeaconCommand(IGameClient client, StringCommand command)
    {
        if (!client.IsInGame)
            return ECommandAction.Stopped;

        if (!HasPermission(client))
        {
            PrintLocalized(client, KeyNoPermission);
            return ECommandAction.Stopped;
        }

        // ArgCount excludes the command name; need at least one argument.
        if (command.ArgCount < 1)
        {
            PrintLocalized(client, KeyUsage);
            return ECommandAction.Stopped;
        }

        var arg     = command.GetArg(1);
        var targets = ResolveTargets(arg).ToList();

        if (targets.Count == 0)
        {
            PrintLocalized(client, KeyTargetsEmpty);
            return ECommandAction.Stopped;
        }

        // Partial-name matches must be unambiguous; @-selectors may hit many.
        if (!arg.StartsWith('@') && targets.Count != 1)
        {
            PrintLocalized(client, KeyMultipleTargets);
            return ECommandAction.Stopped;
        }

        foreach (var target in targets)
            ToggleBeacon(target, client);

        return ECommandAction.Stopped;
    }

    /// <summary>
    ///     Resolves alive targets. Supports <c>@all</c>, <c>@ct</c>, <c>@t</c>
    ///     and case-insensitive partial-name matching.
    /// </summary>
    private IEnumerable<IGameClient> ResolveTargets(string arg)
    {
        var alive = _clientManager.GetGameClients(true)
            .Where(c => c.GetPlayerController()?.GetPawn() is { IsAlive: true });

        if (arg.StartsWith('@'))
        {
            return arg.ToLowerInvariant() switch
            {
                "@all" => alive,
                "@ct"  => alive.Where(c => c.GetPlayerController()?.GetPawn()?.Team == CStrikeTeam.CT),
                "@t"   => alive.Where(c => c.GetPlayerController()?.GetPawn()?.Team == CStrikeTeam.TE),
                _      => Enumerable.Empty<IGameClient>(),
            };
        }

        return alive.Where(c =>
            c.Name.Contains(arg, StringComparison.OrdinalIgnoreCase));
    }

    private bool HasPermission(IGameClient client)
    {
        if (_adminManager is null)
            return false;

        var admin = _adminManager.GetAdmin(client.SteamId);
        return admin is not null && admin.HasPermission(_config.BeaconCommandPermission);
    }

    #endregion

    #region ICS2BeaconsApi

    public void ToggleBeacon(IGameClient player, IGameClient? caller = null)
    {
        if (!player.IsInGame)
            return;

        var slot = (int)player.Slot.AsPrimitive();

        if (_beacons[slot] != EntityIndex.InvalidIndex)
        {
            RemoveBeaconAtSlot(slot);
        }
        else if (!SpawnBeaconOnPlayer(player))
        {
            return; // not alive / no pawn — don't broadcast a toggle that didn't happen
        }

        SendToggleMessage(player.Name.Trim(), caller);
    }

    public bool HasActiveBeacon(IGameClient player)
        => _beacons[(int)player.Slot.AsPrimitive()] != EntityIndex.InvalidIndex;

    /// <summary>
    ///     Broadcasts the localized "beacon toggled" message per configured visibility,
    ///     rendering each recipient in their own Steam culture. <paramref name="targetName" />
    ///     is the player the beacon was toggled on. Falls back to a plain string if the
    ///     LocalizerManager is unavailable.
    /// </summary>
    private void SendToggleMessage(string targetName, IGameClient? caller)
    {
        switch (_config.BeaconToggleVisibility.ToLowerInvariant())
        {
            case "everyone":
                foreach (var c in _clientManager.GetGameClients(true))
                {
                    if (c.IsInGame)
                        PrintLocalized(c, KeyToggled, targetName);
                }

                break;

            case "admins":
                foreach (var c in _clientManager.GetGameClients(true))
                {
                    if (c.IsInGame && HasPermission(c))
                        PrintLocalized(c, KeyToggled, targetName);
                }

                break;

            case "caller":
                if (caller is { IsInGame: true })
                    PrintLocalized(caller, KeyToggled, targetName);

                break;
        }
    }

    #endregion

    #region Beacon spawn / removal

    private bool SpawnBeaconOnPlayer(IGameClient player)
    {
        if (player.GetPlayerController() is not { IsValidEntity: true } controller)
            return false;

        if (controller.GetPawn() is not { IsValidEntity: true, IsAlive: true } pawn)
            return false;

        var particle = _entityManager.CreateEntityByName<IBaseParticle>("info_particle_system");

        if (particle is not { })
        {
            _logger.LogWarning("Failed to create beacon particle entity");
            return false;
        }

        var effect = pawn.Team == CStrikeTeam.CT
            ? _config.BeaconParticleCt
            : _config.BeaconParticleT;

        particle.StartActive = false;

        var origin = pawn.GetAbsOrigin();
        particle.Teleport(new Vector(origin.X, origin.Y, origin.Z + _config.BeaconSpawnHeight));
        particle.DispatchSpawn(new Dictionary<string, KeyValuesVariantValueItem>
        {
            ["effect_name"] = effect,
        });

        // Parent to the pawn so the beacon follows the player.
        particle.AcceptInput("SetParent", pawn, particle, "!activator");
        particle.AcceptInput("Start");

        _beacons[(int)player.Slot.AsPrimitive()] = particle.Index;

        EnsureBeepTimer();
        return true;
    }

    private void RemoveBeacon(IGameClient client)
        => RemoveBeaconAtSlot((int)client.Slot.AsPrimitive());

    private void RemoveBeaconAtSlot(int slot)
    {
        var index = _beacons[slot];

        if (index == EntityIndex.InvalidIndex)
            return;

        _beacons[slot] = EntityIndex.InvalidIndex;

        if (_entityManager.FindEntityByIndex(index) is { IsValidEntity: true } entity
            && entity.GetAbsPtr() != nint.Zero)
            entity.Kill();

        StopBeepTimerIfIdle();
    }

    private void ClearAllBeacons()
    {
        for (var slot = 0; slot < _beacons.Length; slot++)
        {
            var index = _beacons[slot];

            if (index == EntityIndex.InvalidIndex)
                continue;

            _beacons[slot] = EntityIndex.InvalidIndex;

            if (_entityManager.FindEntityByIndex(index) is { IsValidEntity: true } entity
                && entity.GetAbsPtr() != nint.Zero)
                entity.Kill();
        }

        StopBeepTimer();
    }

    #endregion

    #region Beep timer

    private bool AnyActiveBeacon()
    {
        foreach (var index in _beacons)
        {
            if (index != EntityIndex.InvalidIndex)
                return true;
        }

        return false;
    }

    private void EnsureBeepTimer()
    {
        if (_beepTimer is not null)
            return;

        _beepTimer = _modSharp.PushTimer(
            BeepBeacons,
            _config.BeaconBeepInterval,
            GameTimerFlags.Repeatable | GameTimerFlags.StopOnRoundEnd | GameTimerFlags.StopOnMapEnd);
    }

    private void StopBeepTimerIfIdle()
    {
        if (!AnyActiveBeacon())
            StopBeepTimer();
    }

    private void StopBeepTimer()
    {
        if (_beepTimer is { } id)
            _modSharp.StopTimer(id);

        _beepTimer = null;
    }

    private void BeepBeacons()
    {
        foreach (var index in _beacons)
        {
            if (index == EntityIndex.InvalidIndex)
                continue;

            if (_entityManager.FindEntityByIndex(index) is { IsValidEntity: true } entity
                && entity.GetAbsPtr() != nint.Zero)
                entity.EmitSound(_config.BeaconSoundeventName);
        }
    }

    #endregion
}
