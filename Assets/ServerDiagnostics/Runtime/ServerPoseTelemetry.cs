using UnityEngine;
using UnityEngine.XR;

public sealed class ServerPoseTelemetry : MonoBehaviour
{
    public Transform Marker;
    private Quaternion reference = Quaternion.identity;
    private Quaternion rotation = Quaternion.identity;
    private float previousTime;
    private int frames;
    private bool tracked;

    private void Start()
    {
        Application.targetFrameRate = 60;
        previousTime = Time.realtimeSinceStartup;
        Application.onBeforeRender += ApplyHeadPose;
        Debug.Log("[ServerPoseCheck] Scene started. Tracking mode=3DoF. No TMP scene UI.");
    }
    private void ApplyHeadPose()
    {
        var device = InputDevices.GetDeviceAtXRNode(XRNode.Head);
        device.TryGetFeatureValue(CommonUsages.isTracked, out tracked);
        if (device.TryGetFeatureValue(CommonUsages.deviceRotation, out var value))
            rotation = value;
        transform.localPosition = Vector3.zero;
        transform.localRotation = reference * rotation;
    }
    private void Update()
    {
        ApplyHeadPose();
        frames++;
        if (Marker != null) Marker.Rotate(Vector3.up, 25 * Time.unscaledDeltaTime, Space.World);
        var elapsed = Time.realtimeSinceStartup - previousTime;
        if (elapsed >= 1f)
        {
            Debug.Log($"[ServerPoseCheck] heartbeat fps={frames / elapsed:F1} tracked={tracked} rotation={rotation.eulerAngles} frame={Time.frameCount}");
            frames = 0;
            previousTime = Time.realtimeSinceStartup;
        }
    }
    private void OnGUI()
    {
        GUI.Label(new Rect(15,15,700,45), "XREAL 3DoF pose check: rotating cube + stationary markers");
        if (GUI.Button(new Rect(15,65,220,65), "Recenter")) reference = Quaternion.Inverse(rotation);
    }
    private void OnApplicationPause(bool pause) { Debug.Log($"[ServerPoseCheck] paused={pause}"); }
    private void OnDestroy() { Application.onBeforeRender -= ApplyHeadPose; }
}
