using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace ArEnclosure.Core
{
    public struct ProbeHit
    {
        public TrackableId Id;
        public float Y;
        public float Distance;
    }

    public struct ProbeRecord
    {
        public string Source;
        public float Time;
        public ProbeHit[] Hits;
        /// <summary>Index into Hits that the caller used, or -1 if none.</summary>
        public int Chosen;
    }

    /// <summary>
    /// Diagnostic tap on every AR raycast that moves or measures something.
    /// Callers report the full hit list and the hit they used. It changes no
    /// behaviour. It exists so the plane readout can show which plane won.
    /// </summary>
    public static class RaycastProbe
    {
        public static event Action<ProbeRecord> Recorded;

        public static bool HasRecord { get; private set; }
        public static ProbeRecord Last { get; private set; }

        public static void Record(string source, List<ARRaycastHit> hits, int chosen)
        {
            var count = hits != null ? hits.Count : 0;
            var copy = new ProbeHit[count];
            for (var i = 0; i < count; i++)
            {
                copy[i] = new ProbeHit
                {
                    Id = hits[i].trackableId,
                    Y = hits[i].pose.position.y,
                    Distance = hits[i].distance
                };
            }

            Last = new ProbeRecord { Source = source, Time = UnityEngine.Time.time, Hits = copy, Chosen = chosen };
            HasRecord = true;
            Recorded?.Invoke(Last);
        }

        /// <summary>
        /// A tap resolved against the locked floor's mathematical plane, not a
        /// plane outline. Logged as a single hit on the locked plane's id, or
        /// as no hit when refused.
        /// </summary>
        public static void RecordFloor(string source, TrackableId floorId, bool hit, Vector3 point, float distance)
        {
            var hits = hit
                ? new[] { new ProbeHit { Id = floorId, Y = point.y, Distance = distance } }
                : new ProbeHit[0];

            Last = new ProbeRecord { Source = source, Time = UnityEngine.Time.time, Hits = hits, Chosen = hit ? 0 : -1 };
            HasRecord = true;
            Recorded?.Invoke(Last);
        }

        public static string ShortId(TrackableId id)
        {
            // Both halves folded together: on ARCore either half alone can
            // repeat its low bits between planes.
            var h = id.subId1 ^ (id.subId1 >> 32) ^ id.subId2 ^ (id.subId2 >> 32);
            h ^= h >> 16;
            return (h & 0xFFFF).ToString("x4");
        }
    }
}
