using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace EyeLean.Replay.UI
{
    /// <summary>
    /// Fires events while a UI Slider is being interacted with (mouse-down through
    /// release, including click-to-set and drag). Used by ReplayUI to know when to
    /// suspend the playback-driven slider auto-update so the user's input isn't
    /// snapped back to the playback head mid-interaction.
    /// </summary>
    public class SliderInteractionTracker : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        public event Action InteractionStarted;
        public event Action InteractionEnded;

        public void OnPointerDown(PointerEventData eventData)
        {
            InteractionStarted?.Invoke();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            InteractionEnded?.Invoke();
        }
    }
}
