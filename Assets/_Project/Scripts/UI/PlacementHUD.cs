using ArEnclosure.Placement;
using UnityEngine;
using UnityEngine.UI;

namespace ArEnclosure.UI
{
    /// <summary>
    /// Placement buttons that only make sense in some states. Like the other
    /// HUDs it owns no state: it reads the placement controller and asks it
    /// to act.
    /// </summary>
    public class PlacementHUD : MonoBehaviour
    {
        [SerializeField] ARPlacementController m_Placement;
        [Tooltip("Shown only while the locked floor has drifted from the panel's base.")]
        [SerializeField] Button m_ReseatButton;

        void Awake()
        {
            if (m_Placement == null) m_Placement = FindAnyObjectByType<ARPlacementController>();
        }

        void OnEnable()
        {
            if (m_ReseatButton != null) m_ReseatButton.onClick.AddListener(OnReseatPressed);
        }

        void OnDisable()
        {
            if (m_ReseatButton != null) m_ReseatButton.onClick.RemoveListener(OnReseatPressed);
        }

        void OnReseatPressed() => m_Placement.ReseatOnFloor();

        void Update()
        {
            if (m_Placement == null || m_ReseatButton == null) return;
            var show = m_Placement.CanReseat;
            if (m_ReseatButton.gameObject.activeSelf != show) m_ReseatButton.gameObject.SetActive(show);
        }
    }
}
