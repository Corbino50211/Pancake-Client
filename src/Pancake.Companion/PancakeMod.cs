using System.Text.Json;
using BoneLib;
using BoneLib.BoneMenu;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(Pancake.Companion.PancakeMod), "Pancake Client Companion", "0.2.0", "Corbino50211")]
[assembly: MelonGame("Stress Level Zero", "BONELAB")]
[assembly: MelonAdditionalDependencies("BoneLib")]

namespace Pancake.Companion;

public sealed class PancakeMod : MelonMod
{
    private ClientLink? link;
    private FunctionElement? status;
    private Page? scenePage;
    private string sceneSignature = "";
    private PhysicalCamera? prop;
    private bool autoSpawn = true;
    private float nextSpawnAttempt;
    private Camera? camera;
    private RenderTexture? target;
    private Texture2D? pixels;
    private string mode = "Handheld";
    private float fov = 70, nextFrame;
    private bool enabled, stationaryPlaced, previousConnected;
    private readonly Color accent = new(0.92f, 0.72f, 0.23f);

    public override void OnInitializeMelon()
    {
        link = new ClientLink();
        LoggerInstance.Msg("Pancake companion loaded. Open Pancake Client on the same Windows account.");
    }
    private void BuildMenu()
    {
        if (Page.Root == null || status != null) return;
        var page = Page.Root.CreatePage("Pancake Client", accent);
        status = page.CreateFunction("Desktop disconnected", Color.white, () => { });
        var cameras = page.CreatePage("Camera", accent);
        foreach (var view in new[] { "Handheld", "First person", "Right hand", "Stationary", "Follow", "Out front" })
        {
            var selected = view;
            cameras.CreateFunction(selected, accent, () => StartCamera(selected, fov));
        }
        cameras.CreateFunction("Spawn / recall handheld camera", accent, () => { StartCamera("Handheld", fov); if (EnsureCamera()) prop?.Recall(); });
        cameras.CreateFloat("Field of view", Color.white, fov, 5, 30, 110, value => { fov = value; if (camera != null) camera.fieldOfView = fov; });
        cameras.CreateFunction("Place stationary camera here", accent, () => { stationaryPlaced = false; StartCamera("Stationary", fov); });
        cameras.CreateFunction("Save screenshot", accent, () => Send(new { type = "screenshot" }));
        cameras.CreateFunction("Stop camera", Color.white, StopCamera);
        var obs = page.CreatePage("OBS", accent);
        obs.CreateFunction("Start recording", accent, () => Obs("StartRecord"));
        obs.CreateFunction("Stop recording", Color.white, () => Obs("StopRecord"));
        obs.CreateFunction("Mute microphone", Color.white, () => Obs("Mute"));
        obs.CreateFunction("Unmute microphone", Color.white, () => Obs("Unmute"));
        scenePage = obs.CreatePage("Scenes", accent);
        scenePage.CreateFunction("Connect OBS in Pancake Client", Color.white, () => { });
        var stream = obs.CreatePage("Streaming", accent);
        stream.CreateFunction("Requires desktop opt-in", Color.white, () => { });
        stream.CreateFunction("Start streaming", accent, () => Obs("StartStream"));
        stream.CreateFunction("Stop streaming", Color.white, () => Obs("StopStream"));
    }
    private void Send(object message)
    {
        if (link?.Connected != true) { if (status != null) status.ElementName = "Open Pancake Client on your PC"; return; }
        link.Send(message);
    }
    private void Obs(string action) => Send(new { type = "obs", action });
    public override void OnUpdate()
    {
        BuildMenu();
        if (link == null) return;
        if (autoSpawn && Player.Head != null && Time.unscaledTime >= nextSpawnAttempt)
        {
            nextSpawnAttempt = Time.unscaledTime + 3;
            try { if (EnsureCamera()) { autoSpawn = false; enabled = true; } }
            catch (Exception ex) { LoggerInstance.Warning("Handheld camera spawn: " + ex.Message); }
        }
        if (previousConnected && !link.Connected)
        {
            if (status != null) status.ElementName = "Desktop disconnected";
        }
        if (!previousConnected && link.Connected) Send(new { type = "cameraState", mode, fov, enabled });
        previousConnected = link.Connected;
        for (var i = 0; i < 16 && link.TryReceive(out var message); i++)
        {
            try
            {
                switch (message.GetProperty("type").GetString())
                {
                    case "camera":
                        if (!message.GetProperty("enabled").GetBoolean()) StopCamera();
                        else StartCamera(message.GetProperty("mode").GetString() ?? "First person", message.GetProperty("fov").GetSingle());
                        break;
                    case "cameraRecall":
                        StartCamera("Handheld", fov); if (EnsureCamera()) prop?.Recall(); break;
                    case "obsStatus":
                        prop?.SetLive(message.TryGetProperty("live", out var live) && live.GetBoolean());
                        if (status != null) status.ElementName = message.GetProperty("text").GetString() ?? "OBS";
                        if (scenePage != null && message.TryGetProperty("scenes", out var scenes))
                        {
                            var names = scenes.EnumerateArray().Select(x => x.GetString()!).ToArray();
                            var signature = JsonSerializer.Serialize(names);
                            if (signature != sceneSignature)
                            {
                                sceneSignature = signature; scenePage.RemoveAll();
                                foreach (var name in names)
                                {
                                    var sceneName = name;
                                    scenePage.CreateFunction(sceneName, Color.white, () => Send(new { type = "obs", action = "Scene", scene = sceneName }));
                                }
                            }
                        }
                        break;
                }
            }
            catch (Exception ex) { LoggerInstance.Warning("Pancake command: " + ex.Message); }
        }
    }
    private void StartCamera(string requestedMode, float requestedFov)
    {
        if (requestedMode is not ("Handheld" or "First person" or "Right hand" or "Stationary" or "Follow" or "Out front")) return;
        if (mode != requestedMode) stationaryPlaced = false;
        mode = requestedMode;
        fov = float.IsFinite(requestedFov) ? Mathf.Clamp(requestedFov, 30, 110) : 70;
        autoSpawn = false;
        enabled = true;
        Send(new { type = "cameraState", mode, fov, enabled = true });
    }
    private bool EnsureCamera()
    {
        if (camera != null && target != null && pixels != null && prop != null) return true;
        ReleaseCamera();
        var original = Camera.main;
        if (original == null) original = UnityEngine.Object.FindObjectsOfType<Camera>().FirstOrDefault(c => c.enabled && c.gameObject.activeInHierarchy && c != camera);
        if (original == null || Player.Head == null) return false;
        var go = new GameObject("Pancake Camera");
        camera = go.AddComponent<Camera>();
        camera.CopyFrom(original);
        camera.enabled = true;
        camera.stereoTargetEye = StereoTargetEyeMask.None;
        camera.fieldOfView = fov;
        camera.aspect = 16f / 9f;
        camera.rect = new Rect(0, 0, 1, 1);
        camera.nearClipPlane = .03f;
        camera.allowHDR = false;
        target = new RenderTexture(640, 360, 24, RenderTextureFormat.ARGB32);
        target.Create(); camera.targetTexture = target;
        pixels = new Texture2D(640, 360, TextureFormat.RGB24, false);
        try { prop = new PhysicalCamera(target); }
        catch { ReleaseCamera(); throw; }
        return true;
    }
    public override void OnLateUpdate()
    {
        if (!enabled) return;
        try
        {
            if (!EnsureCamera() || Player.Head == null) return;
            var head = Player.Head;
            camera!.fieldOfView = fov;
            switch (mode)
            {
                case "Handheld":
                    if (prop == null) return;
                    camera.transform.SetPositionAndRotation(prop.Lens.position + prop.Root.transform.forward*.02f, prop.Root.transform.rotation);
                    break;
                case "Out front":
                    camera.transform.position = head.position + head.forward*2.5f;
                    camera.transform.LookAt(head.position);
                    break;
                case "Right hand":
                    if (Player.RightHand == null) return;
                    camera.transform.SetPositionAndRotation(Player.RightHand.transform.position, Player.RightHand.transform.rotation);
                    break;
                case "Stationary":
                    if (!stationaryPlaced) { camera.transform.SetPositionAndRotation(head.position, head.rotation); stationaryPlaced = true; }
                    break;
                case "Follow":
                    var desired = head.position - head.forward * 2.5f + Vector3.up * 0.5f;
                    if (!stationaryPlaced) { camera.transform.position = desired; stationaryPlaced = true; }
                    camera.transform.position = Vector3.Lerp(camera.transform.position, desired, 0.25f);
                    camera.transform.LookAt(head.position + head.forward * 0.5f);
                    break;
                default:
                    camera.transform.SetPositionAndRotation(head.position, head.rotation);
                    break;
            }
            if (link?.Connected != true || Time.unscaledTime < nextFrame) return;
            nextFrame = Time.unscaledTime + .1f;
            var previous = RenderTexture.active;
            try
            {
                RenderTexture.active = target;
                if (link?.Connected != true) return;
                pixels!.ReadPixels(new Rect(0, 0, 640, 360), 0, 0);
                pixels.Apply(false);
                byte[] jpeg = ImageConversion.EncodeToJPG(pixels, 65);
                link!.Frame(Convert.ToBase64String(jpeg));
            }
            finally { RenderTexture.active = previous; }
        }
        catch (Exception ex)
        {
            StopCamera();
            LoggerInstance.Warning("Camera stopped: " + ex.Message);
            Send(new { type = "status", text = "Camera stopped: " + ex.Message });
        }
    }
    private void ReleaseCamera()
    {
        prop?.Dispose(); prop = null;
        if (camera != null) UnityEngine.Object.Destroy(camera.gameObject);
        if (target != null) { target.Release(); UnityEngine.Object.Destroy(target); }
        if (pixels != null) UnityEngine.Object.Destroy(pixels);
        camera = null; target = null; pixels = null; stationaryPlaced = false;
    }
    private void StopCamera()
    {
        enabled = false; ReleaseCamera();
        link?.Send(new { type = "cameraStopped" });
    }
    public override void OnSceneWasUnloaded(int buildIndex, string sceneName) { StopCamera(); autoSpawn = true; mode = "Handheld"; nextSpawnAttempt = Time.unscaledTime + 3; }
    public override void OnApplicationQuit() { StopCamera(); link?.Dispose(); }
}
