using System;
using UnityEngine;

namespace ArEnclosure.App
{
    public enum AppMode
    {
        Move,
        Measure,
        Obstacle
    }

    /// <summary>
    /// Owns the current interaction mode. UI asks for a change through
    /// RequestMode and never sets the mode itself, so every tool reads one
    /// source of truth and only one of them reacts to a touch at a time.
    /// </summary>
    public class AppModeController : MonoBehaviour
    {
        [SerializeField] AppMode m_InitialMode = AppMode.Move;

        public event Action<AppMode> ModeChanged;

        public AppMode Current { get; private set; }

        void Awake()
        {
            Current = m_InitialMode;
        }

        public void RequestMode(AppMode mode)
        {
            if (mode == Current) return;
            Current = mode;
            ModeChanged?.Invoke(mode);
        }

        /// <summary>Int overload so a uGUI Button can call it from the inspector.</summary>
        public void RequestMode(int mode)
        {
            if (!Enum.IsDefined(typeof(AppMode), mode)) return;
            RequestMode((AppMode)mode);
        }
    }
}
