using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace LightGreen
{
    /// <summary>
    /// Whether a VR headset is being drawn to right now (for example by SBGVR, or any other mod that
    /// starts Unity's XR), checked once a frame from Unity's XR display subsystems. Needs nothing from
    /// a VR mod, so LightGreen works the same with or without one, and uses no VR mod's code.
    /// </summary>
    internal static class Vr
    {
        private static readonly List<XRDisplaySubsystem> Displays = new List<XRDisplaySubsystem>();
        private static int _checkedFrame = -1;
        private static bool _running;
        private static bool _loggedRunning;

        internal static bool IsRunning
        {
            get
            {
                if (_checkedFrame == Time.frameCount) return _running;
                _checkedFrame = Time.frameCount;

                SubsystemManager.GetSubsystems(Displays);
                _running = false;
                foreach (XRDisplaySubsystem display in Displays)
                {
                    if (display != null && display.running)
                    {
                        _running = true;
                        break;
                    }
                }

                if (_running != _loggedRunning)
                {
                    _loggedRunning = _running;
                    Plugin.Log?.LogInfo(_running ? "VR is running: lens effects off, ice glints halved." : "VR stopped.");
                }
                return _running;
            }
        }

        /// <summary>Checks once a frame, so the log line appears when VR starts or stops.</summary>
        internal static void Poll() => _ = IsRunning;
    }
}
