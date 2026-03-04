using UnityEngine;
/// <summary>
/// Global configuration for the Wheel feature.
/// Holds zone rules, spin parameters, and reward scaling settings.
/// </summary>

[CreateAssetMenu(menuName = "VertigoDemo/Wheel Game Config", fileName = "so_game_config")]
public class WheelGameConfigSO : ScriptableObject
{
    // Keep this enum in a single place (here) to avoid Inspector/serialization mismatches.
    public enum RewardType
    {
        Silver,
        Gold,
        

        Chest,

        Hat,
        WeaponCard,
        Skin,

        Molotov,
        UpgradeCard
    }

    [Header("Zone Rules")]
    public int safeZoneEvery = 5;     // every 5th zone
    public int superZoneEvery = 30;   // every 30th zone

    [Header("Spin")]
    public float spinDuration = 1f;
    public int minFullRotations = 2;
    public int maxFullRotations = 4;

    [Header("Reward Scaling")]
    [Tooltip("Zone 1 => 1.0x, zone 2 => 1.0x + scalePerZone, ...")]
    public float scalePerZone = 0.10f;

    /// <summary>
    /// Returns the multiplier for a given zone.
    /// Zone 1 => 1.0x, Zone 2 => 1.0x + scalePerZone, ...
    /// </summary>
    public float GetZoneMultiplier(int zoneIndex)
    {
        zoneIndex = Mathf.Max(1, zoneIndex);
        return 1f + (zoneIndex - 1) * scalePerZone;
    }
}