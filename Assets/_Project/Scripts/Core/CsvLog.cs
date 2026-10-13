using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace ArEnclosure.Core
{
    /// <summary>
    /// Append-only CSV in persistentDataPath with a fixed header. If the file
    /// on disk was written with different columns, it is renamed aside and a
    /// fresh file is started, so one CSV never holds two layouts.
    /// </summary>
    public class CsvLog
    {
        public static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

        readonly string m_Header;
        public string Path { get; }

        public CsvLog(string fileName, string header)
        {
            m_Header = header;
            Path = System.IO.Path.Combine(Application.persistentDataPath, fileName);
            SetAsideIfHeaderChanged(fileName);
        }

        public bool Append(IEnumerable<string> fields)
        {
            return AppendRows(new[] { string.Join(",", fields) });
        }

        public bool AppendRows(IEnumerable<string> rows)
        {
            try
            {
                var newFile = !File.Exists(Path);
                using (var writer = new StreamWriter(Path, true))
                {
                    if (newFile) writer.WriteLine(m_Header);
                    foreach (var row in rows) writer.WriteLine(row);
                }
                return true;
            }
            catch (IOException e)
            {
                Debug.LogWarning("CSV write failed (" + Path + "): " + e.Message);
                return false;
            }
        }

        /// <summary>Keeps commas and line breaks out of a free-text field.</summary>
        public static string Clean(string text)
        {
            return string.IsNullOrEmpty(text) ? "" : text.Replace(',', ';').Replace('\n', ' ').Replace('\r', ' ');
        }

        public static string Num(float? value, string format = "F4")
        {
            return value.HasValue ? value.Value.ToString(format, Ci) : "";
        }

        void SetAsideIfHeaderChanged(string fileName)
        {
            try
            {
                if (!File.Exists(Path)) return;
                string first;
                using (var reader = new StreamReader(Path)) first = reader.ReadLine();
                if (first == m_Header) return;

                var stamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss", Ci);
                var aside = System.IO.Path.GetFileNameWithoutExtension(fileName) + "_old_" + stamp + ".csv";
                File.Move(Path, System.IO.Path.Combine(Application.persistentDataPath, aside));
            }
            catch (IOException e)
            {
                Debug.LogWarning("CSV header check failed (" + Path + "): " + e.Message);
            }
        }
    }
}
