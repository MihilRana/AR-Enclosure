using UnityEngine;

namespace ArEnclosure.UI
{
    /// <summary>
    /// The one colour palette for HUD text. Scene colours (panels, buttons,
    /// gap lines, ring, obstacles) use the same values, set in the scene.
    /// Status colours stay readable on the dark panels and on bright tiles.
    /// </summary>
    public static class UiPalette
    {
        public const string PassHex = "#22C55E";
        public const string FailHex = "#EF4444";
        public const string WarnHex = "#F59E0B";
        public const string MutedHex = "#A1A1AA";
        public const string PromptHex = "#FCD34D";

        public static readonly Color Pass = new Color32(0x22, 0xC5, 0x5E, 0xFF);
        public static readonly Color Fail = new Color32(0xEF, 0x44, 0x44, 0xFF);
        public static readonly Color Warn = new Color32(0xF5, 0x9E, 0x0B, 0xFF);
        public static readonly Color Muted = new Color32(0xA1, 0xA1, 0xAA, 0xFF);
    }
}
