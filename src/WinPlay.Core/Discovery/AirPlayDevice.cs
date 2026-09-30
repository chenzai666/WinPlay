// SPDX-License-Identifier: GPL-3.0-or-later
using System.Net;

namespace WinPlay.Core.Discovery;

public enum AirPlayDeviceSubtype
{
    Unknown,
    AppleTv,
    HomePod,
    ThirdPartySpeaker,
}

/// <summary>
/// One physical AirPlay receiver, merged from its `_airplay._tcp` and `_raop._tcp`
/// advertisements (correlated by MAC-style device ID).
/// </summary>
public sealed class AirPlayDevice
{
    /// <summary>Normalized MAC-style ID (uppercase, no separators), e.g. "AABBCCDDEEFF".</summary>
    public required string DeviceId { get; init; }

    /// <summary>Human-readable name from the service instance / TXT.</summary>
    public required string Name { get; init; }

    public string? Model { get; init; }
    public ulong RawFeatures { get; init; }
    public AirPlayFeatures Features => (AirPlayFeatures)RawFeatures;
    public ulong StatusFlags { get; init; }

    public string? Hostname { get; init; }
    public IReadOnlyList<IPAddress> Addresses { get; init; } = [];
    public int? AirPlayPort { get; init; }
    public int? RaopPort { get; init; }

    // Grouping fields (§3.2) — drive the Control Center–style collapse.
    public string? GroupId { get; init; }              // gid
    public string? GroupPublicName { get; init; }      // gpn
    public bool IsGroupLeader { get; init; }           // igl
    public bool GroupContainsLeader { get; init; }     // gcgl
    public string? ParentGroupId { get; init; }        // pgid
    public bool ParentGroupContainsLeader { get; init; } // pgcgl
    public string? TightSyncId { get; init; }          // tsid — stereo-pair signal

    public string? PublicKey { get; init; }            // pk (Ed25519, hex)
    public string? PairingIdentity { get; init; }      // pi
    public string? SourceVersion { get; init; }        // srcvers

    public IReadOnlyDictionary<string, string> AirPlayTxt { get; init; } =
        new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> RaopTxt { get; init; } =
        new Dictionary<string, string>();

    /// <summary>
    /// Device subtype rule, verbatim from openairplay/airplay-spec service_discovery.md:
    /// AppleTV = model prefix "AppleTV"; HomePod = model prefix "AudioAccessory";
    /// ThirdPartySpeaker = HasUnifiedAdvertiserInfo or SupportsUnifiedPairSetupAndMFi set;
    /// Unknown = otherwise.
    /// </summary>
    public AirPlayDeviceSubtype Subtype
    {
        get
        {
            if (Model is not null)
            {
                if (Model.StartsWith("AppleTV", StringComparison.Ordinal)) return AirPlayDeviceSubtype.AppleTv;
                if (Model.StartsWith("AudioAccessory", StringComparison.Ordinal)) return AirPlayDeviceSubtype.HomePod;
            }
            if (Features.HasFlag(AirPlayFeatures.HasUnifiedAdvertiserInfo)
                || Features.HasFlag(AirPlayFeatures.SupportsUnifiedPairSetupAndMfi))
                return AirPlayDeviceSubtype.ThirdPartySpeaker;
            return AirPlayDeviceSubtype.Unknown;
        }
    }

    /// <summary>
    /// Mirroring is only ever offered to Apple TV / AirPlay 2 TVs — never to
    /// AudioAccessory* (HomePod) models, which are audio-only.
    /// </summary>
    public bool IsMirroringCandidate =>
        Subtype == AirPlayDeviceSubtype.AppleTv
        || (Subtype != AirPlayDeviceSubtype.HomePod && Features.HasFlag(AirPlayFeatures.SupportsAirPlayScreen));

    public bool SupportsAudio =>
        Features.HasFlag(AirPlayFeatures.SupportsAirPlayAudio) || RaopPort is not null;

    /// <summary>
    /// A standalone HomePod on audioOS 27 keeps its own PTP grandmaster and will not
    /// play audio anchored to ours. Stereo pairs and grouped members slave instead,
    /// so they must still receive our clock.
    /// </summary>
    public bool FollowsOwnClock
    {
        get
        {
            if (Subtype != AirPlayDeviceSubtype.HomePod) return false;
            if (!string.IsNullOrEmpty(TightSyncId) || !string.IsNullOrEmpty(ParentGroupId))
                return false;
            string? os = null;
            if (!AirPlayTxt.TryGetValue("osvers", out os))
                RaopTxt.TryGetValue("osvers", out os);
            if (string.IsNullOrEmpty(os)) return false;
            int cut = 0;
            while (cut < os.Length && char.IsDigit(os[cut])) cut++;
            return cut > 0 && int.TryParse(os[..cut], out int major) && major >= 27;
        }
    }

    public static string NormalizeDeviceId(string raw) =>
        raw.Replace(":", "").Replace("-", "").Trim().ToUpperInvariant();
}
