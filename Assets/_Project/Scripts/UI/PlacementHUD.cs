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

        void Awake()
        {
            if (m_Placement == null) m_Placement = FindAnyObjectByType<ARPlacementController>();
            if (m_FloorConfirm == null) m_FloorConfirm = FindAnyObjectByType<FloorConfirm>();
            if (m_Modes == null) m_Modes = FindAnyObjectByType<AppModeController>();
        }

        void OnEnable()
        {
            if (m_ReseatButton != null) m_ReseatButton.onClick.AddListener(OnReseatPressed);
            if (m_UseFloorButton != null) m_UseFloorButton.onClick.AddListener(OnUseFloorPressed);
            if (m_RescanButton != null) m_RescanButton.onClick.AddListener(OnRescanPressed);
        }

        void OnDisable()
        {
            if (m_ReseatButton != null) m_ReseatButton.onClick.RemoveListener(OnReseatPressed);
            if (m_UseFloorButton != null) m_UseFloorButton.onClick.RemoveListener(OnUseFloorPressed);
            if (m_RescanButton != null) m_RescanButton.onClick.RemoveListener(OnRescanPressed);
        }

        void OnReseatPressed() => m_Placement.ReseatOnFloor();
        void OnUseFloorPressed() => m_FloorConfirm.Confirm();
        void OnRescanPressed() => m_FloorConfirm.Rescan();

        void Update()
        {
            if (m_Placement == null) return;

            SetVisible(m_ReseatButton, m_Placement.CanReseat);

            var offer = m_FloorConfirm != null && m_FloorConfirm.HasOffer &&
                        (m_Modes == null || m_Modes.Current == AppMode.Move);
            SetVisible(m_UseFloorButton, offer);
            SetVisible(m_RescanButton, offer);
        }

        static void SetVisible(Component c, bool visible)
        {
            if (c != null && c.gameObject.activeSelf != visible) c.gameObject.SetActive(visible);
        }
    }
}
