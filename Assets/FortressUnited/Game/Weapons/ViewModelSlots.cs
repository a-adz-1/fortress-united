using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FortressUnited.Game
{
    /// <summary>
    /// Switches between held view models: Q or the mouse wheel cycles,
    /// H hides whatever is held.
    /// </summary>
    public sealed class ViewModelSlots : MonoBehaviour
    {
        public readonly List<GameObject> Slots = new List<GameObject>();
        int current;
        bool hidden;

        public void Add(GameObject viewModel)
        {
            Slots.Add(viewModel);
            Show();
        }

        void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (Slots.Count == 0)
                return;
            int step = 0;
            if (kb != null && kb.qKey.wasPressedThisFrame) step = 1;
            if (mouse != null)
            {
                float wheel = mouse.scroll.ReadValue().y;
                if (wheel > 0) step = -1;
                else if (wheel < 0) step = 1;
            }
            if (step != 0)
            {
                current = (current + step + Slots.Count) % Slots.Count;
                hidden = false;
                Show();
            }
            if (kb != null && kb.hKey.wasPressedThisFrame)
            {
                hidden = !hidden;
                Show();
            }
        }

        void Show()
        {
            for (int i = 0; i < Slots.Count; i++)
                if (Slots[i] != null)
                    Slots[i].SetActive(!hidden && i == current);
        }
    }
}
