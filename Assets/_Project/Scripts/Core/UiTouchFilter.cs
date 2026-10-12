using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.EnhancedTouch;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using InputTouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace ArEnclosure.Core
{
    /// <summary>
    /// Decides once per frame which fingers belong to the UI. A finger that
    /// begins over any UI element stays blocked for its whole gesture, until
    /// it lifts, so a button press can never also become an AR tap, a drag
    /// or half of a twist.
    ///
    /// Uses EventSystem.RaycastAll at the touch position rather than
    /// IsPointerOverGameObject, which is unreliable with the Input System UI
    /// module because its pointer ids do not match touch finger indices.
    /// </summary>
    public static class UiTouchFilter
    {
        static readonly HashSet<int> s_Blocked = new HashSet<int>();
        static readonly HashSet<int> s_Seen = new HashSet<int>();
        static readonly List<int> s_Stale = new List<int>();
        static readonly List<RaycastResult> s_Results = new List<RaycastResult>();
        static PointerEventData s_Pointer;
        static EventSystem s_PointerOwner;
        static int s_RefreshedFrame = -1;

        /// <summary>True if this touch began over UI. Safe to call many times a frame.</summary>
        public static bool IsBlocked(ETouch touch)
        {
            Refresh();
            return s_Blocked.Contains(touch.finger.index);
        }

        public static bool IsOverUI(Vector2 screenPosition)
        {
            var eventSystem = EventSystem.current;
            if (eventSystem == null) return false;

            if (s_Pointer == null || s_PointerOwner != eventSystem)
            {
                s_Pointer = new PointerEventData(eventSystem);
                s_PointerOwner = eventSystem;
            }

            s_Pointer.Reset();
            s_Pointer.position = screenPosition;
            s_Results.Clear();
            eventSystem.RaycastAll(s_Pointer, s_Results);
            return s_Results.Count > 0;
        }

        /// <summary>
        /// Call at the top of every consumer's Update so a Began frame is never
        /// missed, even while that consumer is idle. Runs once per frame.
        /// </summary>
        public static void Refresh()
        {
            if (s_RefreshedFrame == Time.frameCount) return;
            s_RefreshedFrame = Time.frameCount;

            if (!EnhancedTouchSupport.enabled)
            {
                s_Blocked.Clear();
                return;
            }

            s_Seen.Clear();
            var touches = ETouch.activeTouches;
            for (var i = 0; i < touches.Count; i++)
            {
                var touch = touches[i];
                var finger = touch.finger.index;
                s_Seen.Add(finger);

                // Finger indices are reused, so each new touch decides afresh.
                if (touch.phase == InputTouchPhase.Began)
                {
                    if (IsOverUI(touch.screenPosition)) s_Blocked.Add(finger);
                    else s_Blocked.Remove(finger);
                }
            }

            // A finger that has gone from the active list has lifted.
            s_Stale.Clear();
            foreach (var finger in s_Blocked)
                if (!s_Seen.Contains(finger)) s_Stale.Add(finger);
            foreach (var finger in s_Stale)
                s_Blocked.Remove(finger);
        }
    }
}
