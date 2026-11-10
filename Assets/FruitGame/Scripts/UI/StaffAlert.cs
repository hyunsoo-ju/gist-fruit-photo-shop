using System;
using UnityEngine;

namespace FruitGame
{
    /// <summary>
    /// A visitor's call for help (Docs/DESIGN.md §15, M6 6.11): the keypad's 0 raises it, and it stays up on the staff
    /// screen until staff clear it (a click on the banner, or H with the staff panel's keys on). It's silent: the booth
    /// has one audio output, and the visitor shouldn't hear an alarm meant for staff.
    /// </summary>
    public static class StaffAlert
    {
        public static bool Active { get; private set; }
        public static float Since { get; private set; }
        public static event Action Changed;

        public static void Raise()
        {
            if (Active) return;
            Active = true;
            Since = Time.unscaledTime;
            Debug.Log("[FruitGame] A visitor pressed 0 to call staff.");
            Changed?.Invoke();
        }

        public static void Clear()
        {
            if (!Active) return;
            Active = false;
            Changed?.Invoke();
        }

        public static string Waiting
        {
            get
            {
                int s = Mathf.FloorToInt(Time.unscaledTime - Since);
                return $"{s / 60}:{s % 60:00}";
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Active = false; Changed = null; }
    }
}
