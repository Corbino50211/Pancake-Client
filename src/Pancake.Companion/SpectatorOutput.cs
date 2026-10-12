using Il2CppSLZ.Marrow;
using Il2CppSLZ.Bonelab;
using UnityEngine;

namespace Pancake.Companion;

/// <summary>Retargets the native monitor camera; never changes the XR/headset camera.</summary>
internal sealed class SpectatorOutput : IDisposable
{
    private Camera? spectator;
    private SmoothFollower? follower;
    private Transform? previousTarget;
    private float previousFov;
    internal bool Bind(Transform target, float fov)
    {
        if (spectator != null && (!spectator.enabled || !spectator.gameObject.activeInHierarchy)) Dispose();
        if (spectator == null)
        {
            var go = GameObject.Find("GameplaySystems [0]/DisabledContainer/Spectator Camera/Spectator Camera");
            if (go != null) spectator = go.GetComponent<Camera>();
            if (spectator == null)
                spectator = UnityEngine.Object.FindObjectsOfType<Camera>().FirstOrDefault(c => c.name == "Spectator Camera");
            if (spectator == null || !spectator.gameObject.activeInHierarchy || !spectator.enabled) { spectator = null; return false; }
            follower = spectator.GetComponent<SmoothFollower>();
            if (follower == null) { spectator = null; return false; }
            previousTarget = follower.targetTransform; previousFov = spectator.fieldOfView;
        }
        follower!.targetTransform = target;
        spectator.fieldOfView = fov;
        return true;
    }
    public void Dispose()
    {
        if (follower != null) follower.targetTransform = previousTarget;
        if (spectator != null) spectator.fieldOfView = previousFov;
        spectator = null; follower = null; previousTarget = null;
    }
}
