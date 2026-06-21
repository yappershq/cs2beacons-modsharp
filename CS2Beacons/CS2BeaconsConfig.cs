using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace CS2Beacons;

/// <summary>
///     Plugin configuration. Loaded from
///     <c>&lt;sharp&gt;/configs/cs2beacons.json</c>; written with defaults on first run.
/// </summary>
public sealed class CS2BeaconsConfig
{
    [JsonPropertyName("BeaconParticleCt")]
    public string BeaconParticleCt { get; set; } = "particles/player_beacon_ct.vpcf";

    [JsonPropertyName("BeaconParticleT")]
    public string BeaconParticleT { get; set; } = "particles/player_beacon_t.vpcf";

    [JsonPropertyName("BeaconSoundevtsPath")]
    public string BeaconSoundevtsPath { get; set; } = "soundevents/soundevents_jailbreak.vsndevts";

    [JsonPropertyName("BeaconSoundeventName")]
    public string BeaconSoundeventName { get; set; } = "beacon.blip1";

    /// <summary>Admin permission required to use the beacon command.</summary>
    [JsonPropertyName("BeaconCommandPermission")]
    public string BeaconCommandPermission { get; set; } = "@cs2beacons/beacon";

    /// <summary>Interval, in seconds, between beacon beeps (≈70 ticks @ 64-tick).</summary>
    [JsonPropertyName("BeaconBeepInterval")]
    public float BeaconBeepInterval { get; set; } = 1.09375f;

    /// <summary>Z offset applied to the particle relative to the pawn origin.</summary>
    [JsonPropertyName("BeaconSpawnHeight")]
    public float BeaconSpawnHeight { get; set; } = 10f;

    // User-facing strings (usage / no-permission / targets-empty / multiple-targets /
    // toggle broadcast) now live in .assets/locales/cs2beacons.json and are served via
    // the ILocalizerManager (per-client culture). They were removed from this config.

    /// <summary>Who sees the toggle message: "everyone", "admins", or "caller".</summary>
    [JsonPropertyName("BeaconToggleVisibility")]
    public string BeaconToggleVisibility { get; set; } = "everyone";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented      = true,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    ///     Loads the config from <c>&lt;sharpPath&gt;/configs/cs2beacons.json</c>.
    ///     Writes a default file if none exists. Never throws — falls back to defaults.
    /// </summary>
    public static CS2BeaconsConfig Load(string sharpPath, ILogger logger)
    {
        var dir  = Path.Combine(sharpPath, "configs");
        var path = Path.Combine(dir, "cs2beacons.json");

        try
        {
            if (File.Exists(path))
            {
                var json   = File.ReadAllText(path);
                var parsed = JsonSerializer.Deserialize<CS2BeaconsConfig>(json, SerializerOptions);

                if (parsed is not null)
                    return parsed;

                logger.LogWarning("cs2beacons.json deserialized to null — using defaults");
            }

            var fresh = new CS2BeaconsConfig();
            Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonSerializer.Serialize(fresh, SerializerOptions));
            logger.LogInformation("Wrote default config to {Path}", path);
            return fresh;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load cs2beacons.json — using defaults");
            return new CS2BeaconsConfig();
        }
    }
}
