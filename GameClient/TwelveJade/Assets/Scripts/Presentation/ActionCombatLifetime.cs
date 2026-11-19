using System;
using UnityEngine;

namespace TwelveJade.Presentation
{
    public sealed class ActionCombatLifetime : MonoBehaviour
    {
        Action release;
        public void Configure(Action callback) { release = callback; }
        void OnDisable() { var once = release; release = null; once?.Invoke(); }
        void OnDestroy() { release = null; }
    }
}
