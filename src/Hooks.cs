using System;
using AL.H;
using HarmonyLib;
using ILLGAMES.Unity;
using ILLGAMES.Unity.Animations;
using UnityEngine;

namespace AL_PovX;

internal static class Hooks
{
    // Runs right before each character's neck/eye LateUpdate. The first one each frame
    // places the camera; the PoV character's own neck controller is skipped so the
    // head follows the animation (or the camera, with "Rotate Head to Camera").
    [HarmonyPrefix]
    [HarmonyPatch(typeof(NeckLookControllerVer2), nameof(NeckLookControllerVer2.LateUpdate))]
    public static bool Prefix_NeckLookControllerVer2_LateUpdate(NeckLookControllerVer2 __instance)
    {
        if (!Controller.PoVToggled)
            return true;

        bool runOriginal;
        try
        {
            runOriginal = __instance.Pointer != Controller.character.Face.neckLookCtrl.Pointer;
        }
        catch
        {
            runOriginal = true;
        }

        if (!Controller.cameraDidSet)
        {
            Controller.cameraDidSet = true;
            Controller.CameraPoVScene();
        }
        return runOriginal;
    }

    // Keep the game's orbit camera from overriding the PoV camera.
    [HarmonyPrefix]
    [HarmonyPatch(typeof(CameraControllerEX), nameof(CameraControllerEX.LateUpdate))]
    public static bool Prefix_CameraControllerEX_LateUpdate()
    {
        return !Controller.PoVToggled;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(HScene), nameof(HScene.Update))]
    public static void HScene_Post_Update(HScene __instance)
    {
        if (Controller.hScene == null || Controller.hScene.Pointer != __instance.Pointer)
            Controller.hScene = __instance;

        try
        {
            Controller.Update();
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"PoV update failed, turning PoV off: {e}");
            Controller.ForceOff();
        }
    }

    // Fallback for frames where no neck controller ran (e.g. the only visible one is hidden).
    [HarmonyPostfix]
    [HarmonyPatch(typeof(HScene), nameof(HScene.LateUpdate))]
    public static void HScene_Post_LateUpdate()
    {
        if (Controller.PoVToggled && !Controller.cameraDidSet)
        {
            Controller.cameraDidSet = true;
            try
            {
                Controller.CameraPoVScene();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"PoV camera failed, turning PoV off: {e}");
                Controller.ForceOff();
            }
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(HScene), nameof(HScene.Initialize))]
    public static void HScene_Post_Init(HScene __instance)
    {
        Plugin.Log.LogDebug("Initialize PovX with HScene");
        Controller.Reset();
        Controller.hScene = __instance;
    }

    // No patch on HScene.OnEnd: it returns UniTask (a struct), and patching it is the likely cause of the game
    // closing to desktop when H ends. Dispose below restores the characters instead.

    [HarmonyPrefix]
    [HarmonyPatch(typeof(HScene), nameof(HScene.Dispose))]
    public static void HScene_Pre_Dispose()
    {
        Controller.RestoreCharacters();
        Controller.hScene = null;
    }
}
