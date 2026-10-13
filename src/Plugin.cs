using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;

namespace AL_PovX;

// Port of AC_PovX (Aicomi) by y0soro, itself based on SVS_PovX, to AmanatsuLocation.
[BepInProcess("AmanatsuLocation")]
[BepInPlugin(GUID, "AL_PovX", Version)]
public class Plugin : BasePlugin
{
    public const string GUID = "AL_PovX";
    public const string Version = "0.2.3";

    private const string SECTION_GENERAL = "General";
    private const string SECTION_SCENE_POV = "Scene PoV";
    private const string SECTION_HOTKEYS = "Hotkeys";

    internal static new ManualLogSource Log;

    public static ConfigEntry<bool> HideHead { get; set; }
    public static ConfigEntry<float> Sensitivity { get; set; }
    public static ConfigEntry<float> FoVMax { get; set; }
    public static ConfigEntry<float> FoVMin { get; set; }
    public static ConfigEntry<float> OffsetX { get; set; }
    public static ConfigEntry<float> OffsetY { get; set; }
    public static ConfigEntry<float> OffsetZ { get; set; }
    public static ConfigEntry<float> CameraMinX { get; set; }
    public static ConfigEntry<float> CameraMaxX { get; set; }
    public static ConfigEntry<float> CameraSpanY { get; set; }
    public static ConfigEntry<bool> CameraNormalize { get; set; }
    public static ConfigEntry<int> CameraSmoothness { get; set; }
    public static ConfigEntry<bool> CameraHeadRotate { get; set; }
    public static ConfigEntry<bool> Stabilizer { get; set; }
    public static ConfigEntry<int> StabilizerStrength { get; set; }
    public static ConfigEntry<KeyCode> PovKey { get; set; }
    public static ConfigEntry<KeyCode> CharaCycleKey { get; set; }
    public static ConfigEntry<KeyCode> CameraDragKey { get; set; }
    public static ConfigEntry<KeyCode> CameraZoomKey { get; set; }

    private static void OnHideHeadChanged(object sender, EventArgs e) => Controller.RefreshChaControl();

    public override void Load()
    {
        Log = base.Log;

        HideHead = Config.Bind(SECTION_GENERAL, "Hide Head", true, "Should the head be invisible when in PoV mode?");
        Sensitivity = Config.Bind(SECTION_GENERAL, "Camera Sensitivity", 2f, (ConfigDescription)null);
        FoVMax = Config.Bind(SECTION_GENERAL, "Max Field of View", 70f, (ConfigDescription)null);
        FoVMin = Config.Bind(SECTION_GENERAL, "Min Field of View", 5f, (ConfigDescription)null);
        OffsetX = Config.Bind(SECTION_GENERAL, "Offset X", 0f, "Sideway offset from the character's eyes.");
        OffsetY = Config.Bind(SECTION_GENERAL, "Offset Y", 0f, "Vertical offset from the character's eyes.");
        OffsetZ = Config.Bind(SECTION_GENERAL, "Offset Z", 0.03f, "Forward offset from the character's eyes.");

        CameraMinX = Config.Bind(SECTION_SCENE_POV, "Min Camera Angle X", 80f, "Highest downward and leftward angle the camera can rotate.");
        CameraMaxX = Config.Bind(SECTION_SCENE_POV, "Max Camera Angle X", 80f, "Highest upward and rightward angle the camera can rotate.");
        CameraSpanY = Config.Bind(SECTION_SCENE_POV, "Camera Angle Span Y", 70f, "How far can the camera be rotated horizontally?");
        CameraNormalize = Config.Bind(SECTION_SCENE_POV, "Normalize Camera Z-Axis", false, "Stops the camera from tilting.");
        CameraSmoothness = Config.Bind(SECTION_SCENE_POV, "Camera Smoothness", 0,
            new ConfigDescription("The higher the number, the smoother the camera becomes.", new AcceptableValueRange<int>(0, 90)));
        CameraHeadRotate = Config.Bind(SECTION_SCENE_POV, "Rotate Head to Camera", false, "When enabled, the head rotates along with the camera.");

        Stabilizer = Config.Bind(SECTION_SCENE_POV, "Stabilizer", false,
            "Keeps the camera steady instead of following every head movement of the H animation. Mouse look still responds immediately.");
        StabilizerStrength = Config.Bind(SECTION_SCENE_POV, "Stabilizer Strength", 85,
            new ConfigDescription("How strongly the stabilizer damps movement. 0 = barely, 100 = almost locked in place.", new AcceptableValueRange<int>(0, 100)));

        PovKey = Config.Bind(SECTION_HOTKEYS, "PoV Toggle Key", KeyCode.N, (ConfigDescription)null);
        CharaCycleKey = Config.Bind(SECTION_HOTKEYS, "Character Cycle Key", KeyCode.M, "Switch between characters during PoV mode.");
        CameraDragKey = Config.Bind(SECTION_HOTKEYS, "Camera Drag Key", KeyCode.Mouse0, "During PoV mode, holding down this key will move the camera.");
        CameraZoomKey = Config.Bind(SECTION_HOTKEYS, "Camera Zoom Key", KeyCode.Mouse1, "During PoV mode, holding down this key will zoom the camera.");

        HideHead.SettingChanged += OnHideHeadChanged;

        Harmony.CreateAndPatchAll(typeof(Hooks), GUID);
        Log.LogInfo($"AL_PovX {Version} loaded. Toggle PoV in H with {PovKey.Value}.");
    }
}
