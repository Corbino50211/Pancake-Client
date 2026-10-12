using BoneLib;
using Il2CppSLZ.Marrow;
using UnityEngine;

namespace Pancake.Companion;

/// <summary>Runtime-built prop using BONELAB's native host and BoxGrip components.</summary>
internal sealed class PhysicalCamera : IDisposable
{
    internal GameObject Root { get; }
    private readonly Rigidbody body;
    private readonly List<Material> materials = new();
    private readonly HashSet<Hand> hands = new();
    private readonly Renderer liveLight;
    internal Transform Lens { get; }
    internal PhysicalCamera(RenderTexture feed)
    {
        Root = new GameObject("Pancake Handheld Camera"); Root.SetActive(false);
        var interactable = LayerMask.NameToLayer("Interactable");
        if (interactable >= 0) Root.layer = interactable;
        try
        {
        body = Root.AddComponent<Rigidbody>(); body.mass = 0.8f; body.useGravity = false;
        body.constraints = RigidbodyConstraints.FreezeAll;
        Root.AddComponent<InteractableHost>();
        Part("Body", PrimitiveType.Cube, Vector3.zero, new Vector3(.52f,.25f,.055f), new Color(.055f,.075f,.065f));
        Part("Gold frame", PrimitiveType.Cube, new Vector3(0,0,-.031f), new Vector3(.415f,.235f,.012f), new Color(.91f,.69f,.23f));
        var screen = Part("Live view", PrimitiveType.Cube, new Vector3(0,0,-.039f), new Vector3(.39f,.21f,.008f), Color.white, true);
        var screenMaterial = screen.GetComponent<Renderer>().sharedMaterial;
        if (screenMaterial.HasProperty("_BaseMap")) screenMaterial.SetTexture("_BaseMap", feed);
        if (screenMaterial.HasProperty("_MainTex")) screenMaterial.SetTexture("_MainTex", feed);
        if (screenMaterial.HasProperty("_EmissionMap")) { screenMaterial.SetTexture("_EmissionMap", feed); screenMaterial.SetFloat("_Emission", 1); screenMaterial.SetColor("_EmissionColor", Color.white); }
        Part("Lens ring", PrimitiveType.Sphere, new Vector3(.12f,0,.041f), new Vector3(.10f,.10f,.045f), new Color(.91f,.69f,.23f));
        Lens = Part("Lens", PrimitiveType.Sphere, new Vector3(.12f,0,.065f), new Vector3(.075f,.075f,.025f), new Color(.025f,.045f,.04f)).transform;
        liveLight = Part("OBS live light", PrimitiveType.Sphere, new Vector3(.20f,.085f,.04f), Vector3.one*.018f, new Color(.15f,.18f,.15f), true).GetComponent<Renderer>();
        var poses = Resources.FindObjectsOfTypeAll<HandPose>();
        foreach (var x in new[] { -.235f, .235f })
        {
            var handle = Part("Grab handle", PrimitiveType.Cube, new Vector3(x,0,-.008f), new Vector3(.04f,.19f,.065f), new Color(.29f,.43f,.27f));
            handle.layer = LayerMask.NameToLayer("Interactable");
            var col = handle.AddComponent<BoxCollider>(); col.size = Vector3.one;
            var grip = handle.AddComponent<BoxGrip>();
            grip._boxCollider = col; grip.isThrowable = false; grip.gripOptions = InteractionOptions.MultipleHands;
            grip.priority = 1; grip.minBreakForce = float.PositiveInfinity; grip.maxBreakForce = float.PositiveInfinity;
            grip.defaultGripDistance = .15f; grip.gripDistanceSqr = .0225f; grip.rotationLimit = 180;
            grip.handleAmplifyCurve = new AnimationCurve(new Keyframe(-180,0),new Keyframe(180,0));
            grip.sandwichHandPose = poses.FirstOrDefault(p => p.name == "BoxSandwichGrip");
            grip.edgeHandPose = poses.FirstOrDefault(p => p.name == "BoxEdgeGrip");
            grip.faceHandPose = poses.FirstOrDefault(p => p.name == "BoxFaceGrip");
            grip.sandwichSize = .04f; grip.edgePadding = .015f; grip.edgeHandPoseRadius = .04f; grip.faceHandPoseRadius = .1f;
            grip.canBeSandwichedGrabbed = true; grip.canBeEdgeGrabbed = true; grip.canBeFaceGrabbed = true;
            grip.enabledFaces = (BoxGrip.Faces)(-1); grip.enabledEdges = (BoxGrip.Edges)(-1);
        }
        Hooking.OnGripAttached += Attached; Hooking.OnGripDetached += Detached;
        Recall(); Root.SetActive(true);
        }
        catch { Dispose(); throw; }
    }
    private GameObject Part(string name, PrimitiveType type, Vector3 position, Vector3 scale, Color color, bool unlit = false)
    {
        var go = GameObject.CreatePrimitive(type); go.name = name; go.transform.SetParent(Root.transform, false);
        go.transform.localPosition = position; go.transform.localScale = scale;
        var collider = go.GetComponent<Collider>(); if (collider != null) { collider.enabled = false; UnityEngine.Object.Destroy(collider); }
        go.layer = Root.layer;
        var shader = Shader.Find(unlit ? "SLZ/SLZ Unlit" : "SLZ/LitMAS/LitMAS Standard");
        Material material;
        if (shader != null && shader.isSupported) material = new Material(shader);
        else
        {
            var native = Resources.FindObjectsOfTypeAll<Material>().FirstOrDefault(m => m.shader != null && m.shader.isSupported && m.shader.name.StartsWith("SLZ/LitMAS/", StringComparison.Ordinal) && m.renderQueue < 2500);
            if (native == null) throw new InvalidOperationException("No supported SLZ material is loaded yet. Recall the camera after the level loads.");
            material = new Material(native);
        }
        if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", Texture2D.whiteTexture);
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 0);
        if (material.HasProperty("_BlendSrc")) material.SetFloat("_BlendSrc", 1);
        if (material.HasProperty("_BlendDst")) material.SetFloat("_BlendDst", 0);
        if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 1);
        material.renderQueue = 2000; materials.Add(material);
        go.GetComponent<Renderer>().sharedMaterial = material; return go;
    }
    private bool Owns(Grip grip) => Root != null && grip != null && grip.transform.IsChildOf(Root.transform);
    private void Attached(Grip grip, Hand hand)
    {
        if (!Owns(grip)) return;
        hands.Add(hand); body.constraints = RigidbodyConstraints.None;
    }
    private void Detached(Grip grip, Hand hand)
    {
        if (!Owns(grip)) return;
        hands.Remove(hand);
        if (hands.Count == 0) { body.velocity = Vector3.zero; body.angularVelocity = Vector3.zero; body.constraints = RigidbodyConstraints.FreezeAll; }
    }
    internal void SetLive(bool live)
    {
        if (liveLight == null) return;
        var color = live ? Color.red : new Color(.15f,.18f,.15f);
        var material = liveLight.sharedMaterial;
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
    }
    internal void Recall()
    {
        if (Player.Head == null || hands.Count > 0) return;
        Root.transform.SetPositionAndRotation(Player.Head.position + Player.Head.forward*.75f - Vector3.up*.12f, Player.Head.rotation);
        body.velocity = Vector3.zero; body.angularVelocity = Vector3.zero;
    }
    public void Dispose()
    {
        Hooking.OnGripAttached -= Attached; Hooking.OnGripDetached -= Detached;
        if (Root != null) UnityEngine.Object.Destroy(Root);
        foreach (var material in materials) if (material != null) UnityEngine.Object.Destroy(material);
    }
}
