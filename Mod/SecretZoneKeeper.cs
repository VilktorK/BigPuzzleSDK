using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace BigPuzzleSDK {

// The game removes its unfinished secret zone (LandmarksNonChallenge/SecretZone) while it loads, but some Vanilla Item types
// (Laptop, Night Vision Goggles, Barrel, Corpse) are copied from it. SecretZoneController.Awake is skipped so the zone stays in the
// scene, and the zone is switched off so players never see or enter it: a switched-off object can still be copied.
internal static class SecretZoneKeeper {
    internal static void Install(Harmony harmony) {
        try {
            MethodInfo target = AccessTools.Method(typeof(SecretZoneController), "Awake");
            if (target == null) throw new MissingMethodException("SecretZoneController.Awake not found");
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(SecretZoneKeeper), nameof(KeepAndHide)));
        } catch (Exception ex) {
            Plugin.log.LogWarning("[SecretZone] couldn't patch SecretZoneController.Awake, so the Laptop, Night Vision Goggles, Barrel and Corpse items may have nothing to copy: " + ex.Message);
        }
    }

    private static bool KeepAndHide(SecretZoneController __instance) {
        try {
            __instance.gameObject.SetActive(false);
        } catch (Exception ex) {
            Plugin.log.LogWarning("[SecretZone] couldn't switch the zone off: " + ex.Message);
        }
        return false;   // never runs the game's own Awake (that is what removes the zone)
    }
}

}
