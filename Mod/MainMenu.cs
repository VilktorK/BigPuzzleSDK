using HarmonyLib;

namespace BigPuzzleSDK {

// The game's main menu, for the SDK's menu tools (CreatorMenu, TestSession). The game's own MainMenuManager.instance
// throws until the main menu has woken up, so the SDK keeps the one that woke up last. Null while there is none.
internal static class MainMenu {
    internal static MainMenuManager Current { get; private set; }

    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.Awake))]
    private static class AwakePatch {
        private static void Postfix(MainMenuManager __instance) { Current = __instance; }
    }
}

} // namespace BigPuzzleSDK
