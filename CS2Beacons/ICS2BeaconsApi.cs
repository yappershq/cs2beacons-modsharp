using Sharp.Shared.Objects;

namespace CS2Beacons;

/// <summary>
///     Public API for CS2Beacons. Published to ModSharp's
///     <see cref="Sharp.Shared.Managers.ISharpModuleManager" /> under
///     <see cref="Identity" /> so other plugins can toggle/query beacons.
///
///     Consumer side:
///     <code>
///     var beacons = sharpModuleManager
///         .GetOptionalSharpModuleInterface&lt;ICS2BeaconsApi&gt;(ICS2BeaconsApi.Identity)?.Instance;
///     beacons?.ToggleBeacon(targetClient, callerClient);
///     </code>
/// </summary>
public interface ICS2BeaconsApi
{
    public const string Identity = nameof(ICS2BeaconsApi);

    /// <summary>
    ///     Toggles the beacon on the specified player. Spawns a team-colored
    ///     particle parented to the pawn when off, removes it when on.
    /// </summary>
    /// <param name="player">Target client (must be in-game and alive to spawn).</param>
    /// <param name="caller">Optional caller, used for toggle-message visibility.</param>
    void ToggleBeacon(IGameClient player, IGameClient? caller = null);

    /// <summary>
    ///     Returns true when the specified player currently has an active beacon.
    /// </summary>
    bool HasActiveBeacon(IGameClient player);
}
