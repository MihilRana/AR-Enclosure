using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace ArEnclosure.Measurement
{
    /// <summary>
    /// Appends one row per measurement to a CSV in persistentDataPath. This
    /// file is what gets compared against a tape measure, so run ids carry on
    /// from the last row on disk and never restart when the app restarts.
    /// </summary>
    public class MeasurementLog
    {
        const string k_Header =
            "run_id,timestamp,mode,point_count,points,distance_m,camera_distance_m,tracking_state";

        static readonly CultureInfo k_Ci = CultureInfo.InvariantCulture;

        public string Path { get; }
        public int LastRunId { get; private set; }

        public MeasurementLog(string fileName)
        {
            Path = System.IO.Path.Combine(Application.persistentDataPath, fileName);
            LastRunId = ReadLastRunId(Path);
        }

        public int NextRunId => LastRunId + 1;

        /// <summary>Writes the row and returns true if it reached the disk.</summary>
        public bool Append(int runId, string mode, Vector3[] points, float metres,
            float cameraDistance, string trackingState)
        {
            try
            {
                var newFile = !File.Exists(Path);
                using (var writer = new StreamWriter(Path, true))
                {
                    if (newFile) writer.WriteLine(k_Header);

                    writer.WriteLine(string.Join(",", new[]
                    {
                        runId.ToString(k_Ci),
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", k_Ci),
                        mode,
                        points.Length.ToString(k_Ci),
                        FormatPoints(points),
                        metres.ToString("F4", k_Ci),
                        cameraDistance.ToString("F3", k_Ci),
                        trackingState
                    }));
                }

                LastRunId = Mathf.Max(LastRunId, runId);
                return true;
            }
            catch (IOException e)
            {
                Debug.LogWarning("Measurement log write failed: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// x;y;z per point, points split by |, so commas stay column breaks.
        /// </summary>
        static string FormatPoints(Vector3[] points)
        {
            var sb = new StringBuilder();
            for (var i = 0; i < points.Length; i++)
            {
                if (i > 0) sb.Append('|');
                var p = points[i];
                sb.Append(p.x.ToString("F4", k_Ci)).Append(';')
                  .Append(p.y.ToString("F4", k_Ci)).Append(';')
                  .Append(p.z.ToString("F4", k_Ci));
            }
            return sb.ToString();
        }

        static int ReadLastRunId(string path)
        {
            try
            {
                if (!File.Exists(path)) return 0;

                var last = 0;
                foreach (var line in File.ReadLines(path))
                {
                    var comma = line.IndexOf(',');
                    if (comma <= 0) continue;
                    if (int.TryParse(line.Substring(0, comma), NumberStyles.Integer, k_Ci, out var id))
                        last = Mathf.Max(last, id);
                }
                return last;
            }
            catch (IOException e)
            {
                Debug.LogWarning("Measurement log read failed: " + e.Message);
                return 0;
            }
        }
    }
}
