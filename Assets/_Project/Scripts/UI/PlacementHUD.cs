using ArEnclosure.App;
using ArEnclosure.Placement;
using UnityEngine;
using UnityEngine.UI;

namespace ArEnclosure.UI
{
    /// <summary>
    /// Placement buttons that only make sense in some states. Like the other
    /// HUDs it owns no state: it reads the placement controller and the floor
    /// confirmation and asks them to act.
    /// </summary>
    public class PlacementHUD : MonoBehaviour
    {
        [SerializeField] ARPlacementController m_Placement;
        [SerializeField] FloorConfirm m_FloorConfirm;
        [SerializeField] AppModeController m_Modes;
        [Tooltip("Shown only while the locked floor has drifted from the panel's base.")]
        [SerializeField] Button m_ReseatButton;
        [Tooltip("Shown in Move mode while a settled floor is offered.")]
        [SerializeField] Button m_UseFloorButton;
        [SerializeField] Button m_RescanButton;
        [Tooltip("Re-drops the floor ring at the screen centre. Shown with the other offer buttons.")]
        [SerializeField] Button m_MoveRingButton;

        [Header("Reset with confirm")]
        [Tooltip("First tap arms it, a second tap within the window resets. The scene button has no other listener.")]
        [SerializeField] Button m_ResetButton;
        [SerializeField] TMPro.TMP_Text m_ResetLabel;
        [SerializeField] float m_ResetConfirmSeconds = 2f;
        [SerializeField] string m_ResetConfirmText = "Tap again to reset";

        string m_ResetText;
        float m_ResetArmedUntil = -1f;

        void Awake()
        {
            if (m_Placement == null) m_Placement = FindAnyObjectByType<ARPlacementController>();
            if (m_FloorConfirm == null) m_FloorConfirm = FindAnyObjectByType<FloorConfirm>();
            if (m_Modes == null) m_Modes = FindAnyObjectByType<AppModeController>();
            if (m_ResetLabel != null) m_ResetText = m_ResetLabel.text;
        }

        void OnEnable()
        {
            if (m_ReseatButton != null) m_ReseatButton.onClick.AddListener(OnReseatPressed);
            if (m_UseFloorButton != null) m_UseFloorButton.onClick.AddListener(OnUseFloorPressed);
            if (m_RescanButton != null) m_RescanButton.onClick.AddListener(OnRescanPressed);
            if (m_MoveRingButton != null) m_MoveRingButton.onClick.AddListener(OnMoveRingPressed);
            if (m_ResetButton != null) m_ResetButton.onClick.AddListener(OnResetPressed);
        }

        void OnDisable()
        {
            if (m_ReseatButton != null) m_ReseatButton.onClick.RemoveListener(OnReseatPressed);
            if (m_UseFloorButton != null) m_UseFloorButton.onClick.RemoveListener(OnUseFloorPressed);
            if (m_RescanButton != null) m_RescanButton.onClick.RemoveListener(OnRescanPressed);
            if (m_MoveRingButton != null) m_MoveRingButton.onClick.RemoveListener(OnMoveRingPressed);
            if (m_ResetButton != null) m_ResetButton.onClick.RemoveListener(OnResetPressed);
        }

        void OnReseatPressed() => m_Placement.ReseatOnFloor();
        void OnUseFloorPressed() => m_FloorConfirm.Confirm();
        void OnRescanPressed() => m_FloorConfirm.Rescan();
        void OnMoveRingPressed() => m_FloorConfirm.MoveRingHere();

        /// <summary>
        /// Reset now restarts the AR session and clears obstacles, so one stray
        /// tap must not do it. The first tap only changes the label.
        /// </summary>
        void OnResetPressed()
        {
            if (Time.time < m_ResetArmedUntil)
            {
                m_ResetArmedUntil = -1f;
                if (m_ResetLabel != null) m_ResetLabel.text = m_ResetText;
                m_Placement.Clear();
                return;
            }

            m_ResetArmedUntil = Time.time + m_ResetConfirmSeconds;
            if (m_ResetLabel != null) m_ResetLabel.text = m_ResetConfirmText;
        }

        void Update()
        {
            if (m_Placement == null) return;

            SetVisible(m_ReseatButton, m_Placement.CanReseat);

            if (m_ResetArmedUntil > 0f && Time.time >= m_ResetArmedUntil)
            {
                m_ResetArmedUntil = -1f;
                if (m_ResetLabel != null) m_ResetLabel.text = m_ResetText;
            }

            var offer = m_FloorConfirm != null && m_FloorConfirm.HasOffer &&
                        (m_Modes == null || m_Modes.Current == AppMode.Move);
            SetVisible(m_UseFloorButton, offer);
            SetVisible(m_RescanButton, offer);
            SetVisible(m_MoveRingButton, offer);
        }

        static void SetVisible(Component c, bool visible)
        {
            if (c != null && c.gameObject.activeSelf != visible) c.gameObject.SetActive(visible);
        }
    }
}
