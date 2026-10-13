using System;
using System.Collections.Generic;
using AL.H;
using Character;
using ILLGAMES.Unity.Component;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.EventSystems;

namespace AL_PovX;

public static class Controller
{
    public static float zoom = 0.5f;

    public static HScene hScene;

    public static bool backup = false;
    public static float backupFoV = 0f;
    public static Vector3 backupDir = Vector3.zero;
    public static Vector3 backupPos = Vector3.zero;
    public static Vector3 backupRot = Vector3.zero;

    public static bool cameraDidSet = false;
    public static bool didHideHead = false;

    public static float cameraAngleOffsetX = 0f;
    public static float cameraAngleOffsetY = 0f;

    public static Vector3 prevPosition = Vector3.zero;

    // Stabilizer state: the smoothed eye position and head rotation the camera follows.
    private static bool stabInit = false;
    private static Vector3 stabPosition = Vector3.zero;
    private static Quaternion stabRotation = Quaternion.identity;
    private static Vector3 stabLastTarget = Vector3.zero;
    private static Vector3 stabVelocity = Vector3.zero;

    public static int focus = 0;

    public static Human character = null;

    public static bool inScenePoV = false;

    private static readonly Dictionary<Mesh, Mesh> hideMeshToOrigDict = new(new Il2CppComparer<Mesh>());
    private static readonly Dictionary<SkinnedMeshRenderer, Mesh> characterSimpleMeshHideDict = new(new Il2CppComparer<SkinnedMeshRenderer>());

    public static bool PoVToggled
    {
        get => character != null;
        set
        {
            if (PoVToggled == value)
                return;

            if (value)
            {
                if (GetChaControls().Length != 0)
                {
                    FocusPreferMale();
                    var next = GetChaControl();
                    if (next == null)
                        return;
                    SetBackups();
                    inScenePoV = false;
                    SetChaControl(next);
                    Plugin.Log.LogInfo("PoV on");
                }
            }
            else
            {
                SetChaControl(null);
                RestoreBackups();
                SetCursorLock(false);
                Plugin.Log.LogInfo("PoV off");
            }
        }
    }

    private static Camera MainCamera => hScene == null ? null : hScene._mainCamera;

    public static void Reset()
    {
        RestoreCharacters();
        backup = false;
        focus = 0;
    }

    // Used when something throws mid-PoV: drop PoV without touching anything else that may be broken.
    public static void ForceOff()
    {
        try { PoVToggled = false; }
        catch { character = null; }
    }

    public static void RestoreCharacters()
    {
        try
        {
            PoVToggled = false;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Restore: {e.Message}");
            character = null;
        }

        foreach (var human in GetChaControls())
        {
            try { human.Face.objHeadBone.SetActive(true); }
            catch { }
        }

        foreach (var (rend, hideMesh) in characterSimpleMeshHideDict)
        {
            try
            {
                if (rend == null || hideMesh == null || !hideMeshToOrigDict.TryGetValue(hideMesh, out var orig))
                    continue;
                rend.sharedMesh = orig;
                orig.hideFlags = HideFlags.None;
            }
            catch { }
        }
        hideMeshToOrigDict.Clear();
        characterSimpleMeshHideDict.Clear();
    }

    public static void Update()
    {
        Update_PoV();
        cameraDidSet = false;
    }

    public static void SetFoV()
    {
        var cam = MainCamera;
        if (cam == null)
            return;
        cam.fieldOfView = CurrentFov();
    }

    public static void SetPosition(Vector3 position)
    {
        var cam = MainCamera;
        if (cam == null)
            return;
        cam.transform.position = position;
    }

    public static void SetBackups()
    {
        var cam = MainCamera;
        var camCtrl = hScene?._camCtrl;
        if (cam == null || camCtrl == null)
            return;

        if (backup)
            RestoreBackups();

        backup = true;
        backupFoV = camCtrl.CameraFov;
        backupDir = camCtrl.CameraDir;
        backupPos = camCtrl.CameraPos;
        backupRot = camCtrl.CameraRot;
    }

    public static void RestoreBackups()
    {
        var cam = MainCamera;
        var camCtrl = hScene?._camCtrl;
        if (!backup || cam == null || camCtrl == null)
            return;

        backup = false;
        cam.fieldOfView = backupFoV;
        camCtrl.CameraFov = backupFoV;
        camCtrl.CameraDir = backupDir;
        camCtrl.CameraPos = backupPos;
        camCtrl.CameraRot = backupRot;
    }

    public static bool ChaControlPredicate(Human human)
    {
        if (human == null)
            return false;
        try
        {
            var go = human.GameObject;
            return go != null && go.activeInHierarchy && human.Face?.objHeadBone != null;
        }
        catch
        {
            return false;
        }
    }

    public static void SetChaControl(Human next)
    {
        if (character != null && didHideHead)
        {
            didHideHead = false;
            try
            {
                character.Face.objHeadBone.SetActive(true);
                RestoreMonochromeHead(character);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Could not restore head: {e.Message}");
            }
        }

        character = next;
        stabInit = false;

        if (character != null)
        {
            prevPosition = GetDesiredPositionPoV(character);
            if (Plugin.HideHead.Value)
            {
                didHideHead = true;
                character.Face.objHeadBone.SetActive(false);
                HideMonochromeHead(character);
            }
        }
    }

    // Builds a copy of the silhouette body mesh without the vertices weighted to the head bone.
    private static Mesh CreateHideHeadMesh(SkinnedMeshRenderer rend, float threshold = 0.1f)
    {
        int headIdx = -1;
        var bones = rend.bones;
        for (int i = 0; i < bones.Length; i++)
        {
            if (bones[i] != null && bones[i].name == "cf_s_head")
            {
                headIdx = i;
                break;
            }
        }
        if (headIdx < 0)
        {
            Plugin.Log.LogWarning("Could not find head bone cf_s_head on the silhouette body");
            return null;
        }

        var mesh = rend.sharedMesh;
        Vector3[] vertices = mesh.vertices;
        Vector3[] normals = mesh.normals;
        Vector2[] uv = mesh.uv;
        int[] triangles = mesh.triangles;
        BoneWeight[] boneWeights = mesh.boneWeights;

        var newVertices = new List<Vector3>();
        var newNormals = new List<Vector3>();
        var newUv = new List<Vector2>();
        var newWeights = new List<BoneWeight>();
        var newTriangles = new List<int>();
        var remap = new Dictionary<int, int>();

        bool IsHead(BoneWeight w) =>
            (w.boneIndex0 == headIdx && w.weight0 > threshold) ||
            (w.boneIndex1 == headIdx && w.weight1 > threshold) ||
            (w.boneIndex2 == headIdx && w.weight2 > threshold) ||
            (w.boneIndex3 == headIdx && w.weight3 > threshold);

        int n = 0;
        for (int j = 0; j < vertices.Length; j++)
        {
            var w = boneWeights[j];
            if (IsHead(w))
                continue;
            remap[j] = n++;
            newVertices.Add(vertices[j]);
            newWeights.Add(w);
            if (normals.Length > 0)
                newNormals.Add(normals[j]);
            if (uv.Length > 0)
                newUv.Add(uv[j]);
        }

        for (int k = 0; k + 2 < triangles.Length; k += 3)
        {
            if (remap.TryGetValue(triangles[k], out var a) &&
                remap.TryGetValue(triangles[k + 1], out var b) &&
                remap.TryGetValue(triangles[k + 2], out var c))
            {
                newTriangles.Add(a);
                newTriangles.Add(b);
                newTriangles.Add(c);
            }
        }

        var result = new Mesh
        {
            name = mesh.name,
            vertices = newVertices.ToArray(),
            normals = newNormals.ToArray(),
            uv = newUv.ToArray(),
            triangles = newTriangles.ToArray(),
            boneWeights = newWeights.ToArray(),
            bindposes = mesh.bindposes,
            hideFlags = HideFlags.DontUnloadUnusedAsset,
        };
        result.RecalculateBounds();
        return result;
    }

    private static SkinnedMeshRenderer SimpleBody(Human cha)
    {
        var rend = cha.Body?.rendSimpleBody;
        return rend == null ? null : rend.TryCast<SkinnedMeshRenderer>();
    }

    private static void HideMonochromeHead(Human cha)
    {
        try
        {
            var rend = SimpleBody(cha);
            if (rend == null || rend.sharedMesh == null)
                return;

            if (!characterSimpleMeshHideDict.TryGetValue(rend, out var hideMesh) || hideMesh == null)
            {
                hideMesh = CreateHideHeadMesh(rend);
                if (hideMesh == null)
                    return;
                characterSimpleMeshHideDict[rend] = hideMesh;
                rend.sharedMesh.hideFlags = HideFlags.DontUnloadUnusedAsset;
                hideMeshToOrigDict[hideMesh] = rend.sharedMesh;
            }
            rend.sharedMesh = hideMesh;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Could not hide silhouette head: {e.Message}");
        }
    }

    private static void RestoreMonochromeHead(Human cha)
    {
        var rend = SimpleBody(cha);
        if (rend == null || rend.sharedMesh == null)
            return;
        if (hideMeshToOrigDict.TryGetValue(rend.sharedMesh, out var orig))
            rend.sharedMesh = orig;
    }

    public static Human GetChaControl()
    {
        var all = GetChaControls();
        int num = all.Length;
        if (num == 0)
            return null;

        focus %= num;
        for (int i = 0; i < num; i++)
        {
            var human = all[focus];
            if (ChaControlPredicate(human))
                return human;
            focus = (focus + 1) % num;
        }
        return null;
    }

    public static void RefreshChaControl()
    {
        if (PoVToggled)
            SetChaControl(GetChaControl());
    }

    public static Human[] GetChaControls()
    {
        var list = new List<Human>();
        if (hScene == null)
            return list.ToArray();
        try
        {
            var actors = hScene.Actors;
            if (actors == null)
                return list.ToArray();
            foreach (var actor in actors)
            {
                var human = actor?.Human;
                if (human != null)
                    list.Add(human);
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogDebug($"GetChaControls: {e.Message}");
        }
        return list.ToArray();
    }

    public static void FocusPreferMale()
    {
        focus = 0;
        var all = GetChaControls();
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i].Sex == 0)
            {
                focus = i;
                break;
            }
        }
    }

    public static void TogglePov()
    {
        PoVToggled = !PoVToggled;
    }

    public static void SwitchCharacter()
    {
        focus++;
        var next = GetChaControl();
        if (next != null)
            SetChaControl(next);
    }

    // True while the player is typing somewhere (a chat box, a name field...), so letters don't trigger hotkeys.
    private static bool IsTyping()
    {
        try
        {
            if (GUIUtility.keyboardControl != 0)
                return true;
            var selected = EventSystem.current?.currentSelectedGameObject;
            if (selected == null)
                return false;
            var tmp = selected.GetComponent<TMPro.TMP_InputField>();
            if (tmp != null && tmp.isFocused)
                return true;
            var legacy = selected.GetComponent<UnityEngine.UI.InputField>();
            return legacy != null && legacy.isFocused;
        }
        catch
        {
            return false;
        }
    }

    public static void Update_PoV()
    {
        bool typing = IsTyping();
        if (!typing && Input.GetKeyDown(Plugin.PovKey.Value))
            TogglePov();

        if (!PoVToggled)
            return;

        if (!ChaControlPredicate(character))
            PoVToggled = false;
        else if (!typing && Input.GetKeyDown(Plugin.CharaCycleKey.Value))
            SwitchCharacter();
        else
            MoveCameraPoV();
    }

    public static float Mod2(float value, float mod)
    {
        if (value < 0f)
            value = mod + value % mod;
        return value % mod;
    }

    private static float CurrentFov()
    {
        return (Plugin.FoVMax.Value - Plugin.FoVMin.Value) * zoom + Plugin.FoVMin.Value;
    }

    private static void PanCamera(float x, float y)
    {
        float maxX = Plugin.CameraMaxX.Value;
        float minX = Plugin.CameraMinX.Value;
        float spanY = Plugin.CameraSpanY.Value;
        cameraAngleOffsetX = Mathf.Clamp(cameraAngleOffsetX - x, -maxX, minX);
        cameraAngleOffsetY = Mathf.Clamp(cameraAngleOffsetY + y, -spanY, spanY);
    }

    private static bool PointerOverUI(int pointerId = -1)
    {
        var es = EventSystem.current;
        if (es == null)
            return false;
        return pointerId < 0 ? es.IsPointerOverGameObject() : es.IsPointerOverGameObject(pointerId);
    }

    private static void SetCursorLock(bool locked)
    {
        try
        {
            var cursor = GameCursor.Instance;
            if (cursor != null)
                cursor.SetCursorLock(locked);
            else
                Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        }
        catch
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        }
    }

    public static void MoveCameraPoV()
    {
        float sens = Plugin.Sensitivity.Value;
        bool cursorLock = false;

        if (Input.touchCount <= 0)
        {
            bool overUI = PointerOverUI();
            cursorLock = !overUI && (Input.GetKey(Plugin.CameraDragKey.Value) || Input.GetKey(Plugin.CameraZoomKey.Value));
            float mouseY = Input.GetAxis("Mouse Y") * sens;
            float mouseX = Input.GetAxis("Mouse X") * sens;
            if ((mouseY != 0f || mouseX != 0f) && !overUI)
            {
                if (Input.GetKey(Plugin.CameraDragKey.Value))
                    PanCamera(mouseY, mouseX);
                if (Input.GetKey(Plugin.CameraZoomKey.Value))
                    zoom = Math.Clamp(zoom + mouseX / 100f, 0f, 1f);
            }
        }
        else if (Input.touchCount == 1)
        {
            var touch = Input.GetTouch(0);
            if (touch.phase == TouchPhase.Moved && !PointerOverUI(touch.fingerId))
            {
                cursorLock = true;
                var d = touch.deltaPosition * 50f / Screen.height;
                PanCamera(d.y * sens, d.x * sens);
            }
        }
        else
        {
            var t0 = Input.GetTouch(0);
            var t1 = Input.GetTouch(1);
            if (t0.phase == TouchPhase.Moved || t1.phase == TouchPhase.Moved)
            {
                cursorLock = true;
                var now = t1.position - t0.position;
                var before = (t1.position - t1.deltaPosition) - (t0.position - t0.deltaPosition);
                var d = (t1.deltaPosition + t0.deltaPosition) / 2f * 50f / Screen.height;
                float pinch = now.magnitude - before.magnitude;
                PanCamera(d.y * sens, d.x * sens);
                zoom = Math.Clamp(zoom - pinch * 2f / Screen.height * sens, 0f, 1f);
            }
        }

        SetCursorLock(cursorLock);
    }

    public static Vector3 GetDesiredPositionPoV(Human human)
    {
        var head = human.Face.objHeadBone.transform;
        Vector3 eyes;
        try
        {
            var eyeObjs = human.Face.eyeLookCtrl.EyeLookScript.EyeObjs;
            eyes = Vector3.Lerp(eyeObjs[0].EyeTransform.position, eyeObjs[1].EyeTransform.position, 0.5f);
        }
        catch
        {
            // No eye controller: approximate the eyes from the head bone.
            eyes = head.position + head.up * 0.07f + head.forward * 0.06f;
        }
        return eyes
            + Plugin.OffsetX.Value * head.right
            + Plugin.OffsetY.Value * head.up
            + Plugin.OffsetZ.Value * head.forward;
    }

    private static float StrengthNorm => Mathf.Clamp01(Plugin.StabilizerStrength.Value / 100f);

    // Base smoothing time. Strength 0 -> ~0.03 s, 100 -> ~1.2 s.
    private static float StabilizerTime(float scale)
    {
        float s = StrengthNorm;
        return Mathf.Lerp(0.03f, 1.2f, s * s) * scale;
    }

    // The further the camera has fallen behind, the faster it catches up. The catch-up grows
    // smoothly with the lag (no hard edge), so there is no sudden tug.
    private static float CatchUp(float lag, float softLag)
    {
        float r = lag / Mathf.Max(softLag, 0.0001f);
        return 1f + 3f * r * r;
    }

    // Head rotation with the roll removed, so the horizon stays level. When looking almost
    // straight up or down "level" has no meaning (that is what made the camera flip around
    // in v0.2.1), so there it fades back to the head's own orientation.
    private static Quaternion LevelledHeadRotation(Quaternion head)
    {
        var fwd = head * Vector3.forward;
        var headUp = head * Vector3.up;
        float vertical = Mathf.Abs(fwd.y);
        float level = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.6f, 0.9f, vertical));
        var up = Vector3.Slerp(headUp, Vector3.up, level);
        if (Vector3.Cross(fwd, up).sqrMagnitude < 1e-4f)
            up = headUp;
        return Quaternion.LookRotation(fwd, up);
    }

    private static void UpdateStabilizer()
    {
        var targetPos = GetDesiredPositionPoV(character);
        var targetRot = LevelledHeadRotation(character.Face.objHeadBone.transform.rotation);

        // Only snap on a real teleport (posture change, character moved): the head itself
        // jumping far in one frame. Fast animation movement never does that.
        bool teleported = stabInit && (targetPos - stabLastTarget).sqrMagnitude > 0.3f * 0.3f;
        stabLastTarget = targetPos;
        if (!stabInit || teleported)
        {
            stabInit = true;
            stabPosition = targetPos;
            stabRotation = targetRot;
            stabVelocity = Vector3.zero;
            return;
        }

        float s = StrengthNorm;
        float dt = Time.deltaTime;

        // Position: critically damped spring (SmoothDamp) so the camera eases in and out of
        // movement instead of changing speed abruptly.
        float softLag = Mathf.Lerp(0.03f, 0.18f, s);
        float lag = (stabPosition - targetPos).magnitude;
        float posTime = StabilizerTime(0.6f) / CatchUp(lag, softLag);
        stabPosition = Vector3.SmoothDamp(stabPosition, targetPos, ref stabVelocity, posTime, Mathf.Infinity, dt);

        // Rotation: exponential follow with the same smooth catch-up.
        float softAngle = Mathf.Lerp(6f, 35f, s);
        float angle = Quaternion.Angle(stabRotation, targetRot);
        float rotTime = StabilizerTime(1f) / CatchUp(angle, softAngle);
        stabRotation = Quaternion.Slerp(stabRotation, targetRot, 1f - Mathf.Exp(-dt / Mathf.Max(rotTime, 0.0001f)));

        // Safety net far beyond the soft limits; normally never reached.
        float hardLag = softLag * 2.5f;
        var offset = stabPosition - targetPos;
        if (offset.sqrMagnitude > hardLag * hardLag)
            stabPosition = targetPos + offset.normalized * hardLag;
        float hardAngle = Mathf.Min(softAngle * 2.5f, 80f);
        if (Quaternion.Angle(stabRotation, targetRot) > hardAngle)
            stabRotation = Quaternion.RotateTowards(targetRot, stabRotation, hardAngle);
    }

    public static void SetPositionPoV()
    {
        if (Plugin.Stabilizer.Value)
        {
            prevPosition = stabPosition;
            SetPosition(stabPosition);
            return;
        }

        var pos = GetDesiredPositionPoV(character);
        float smooth = Plugin.CameraSmoothness.Value / 100f;
        if (smooth > 0f)
            pos = Vector3.Lerp(pos, prevPosition, smooth);
        prevPosition = pos;
        SetPosition(pos);
    }

    public static void SetRotationPoVScene()
    {
        var cam = MainCamera;
        if (cam == null)
            return;

        var headRot = character.Face.objHeadBone.transform.rotation;
        var t = cam.transform;
        if (Plugin.Stabilizer.Value)
        {
            t.rotation = stabRotation;
        }
        else if (Plugin.CameraNormalize.Value)
        {
            var e = headRot.eulerAngles;
            t.rotation = Quaternion.Euler(e.x, e.y, 0f);
        }
        else
        {
            t.rotation = headRot;
        }
        t.Rotate(cameraAngleOffsetX, cameraAngleOffsetY, 0f);

        if (Plugin.CameraHeadRotate.Value)
        {
            try
            {
                var bones = character.Face.neckLookCtrl.NeckLookScript.aBones;
                if (bones != null && bones.Length > 0)
                    bones[0].neckBone.rotation = t.rotation;
            }
            catch { }
        }
    }

    public static void CameraPoVScene()
    {
        if (character == null)
            return;
        if (!inScenePoV)
        {
            inScenePoV = true;
            cameraAngleOffsetX = cameraAngleOffsetY = 0f;
        }
        if (Plugin.Stabilizer.Value)
            UpdateStabilizer();
        else
            stabInit = false;
        SetFoV();
        SetRotationPoVScene();
        SetPositionPoV();
    }
}
