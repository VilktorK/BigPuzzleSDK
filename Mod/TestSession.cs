using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace BigPuzzleSDK {

// Test sessions: Host > PUZZLE SDK lists the puzzles in the BigPuzzleSDK folder. Picking one and a lobby size hosts a
// game of that size from the "PuzzleSDK Test" save (made the first time), puts the creator at the puzzle's Debug
// Teleport, and adds a test player (TestPlayers) for every other player of the lobby, each at its Test Spawn. The
// command line --bpsdk-test <2|3|4> does the same for the first puzzle in the folder, without a click.
internal static class TestSession {
    private const string SaveName = "PuzzleSDK Test";
    private const string SavePassword = "test";
    private const string CommandLineFlag = "--bpsdk-test";
    private const float MenuTimeout = 10f;    // seconds a menu step waits for its screen
    private const float WorldTimeout = 60f;   // seconds from hosting until the puzzle is built

    // One click a player would make on the way to hosting, on the screen it needs
    private struct Step {
        public string name;                                  // the screen, for the error when it doesn't show
        public Func<MainMenuManager, MonoBehaviour> screen;
        public Action<MainMenuManager> run;
    }

    private static readonly Step[] Steps = {
        new Step { name = "Host Game screen", screen = m => m.hostMenuSelect, run = SelectSave },
        new Step { name = "game settings screen", screen = m => m.hostMenuConfirm, run = NameNewSave },
        new Step { name = "game settings screen", screen = m => m.hostMenuConfirm, run = m => m.hostMenuConfirm.ActionStart() },
        new Step { name = "player count screen", screen = m => m.playerCountMenu, run = m => m.playerCountMenu.SetPlayerCount((PlayerCount)size) },
        new Step { name = "player count screen", screen = m => m.playerCountMenu, run = m => m.playerCountMenu.GoToLoadingMenu() },
        new Step { name = "loading screen", screen = m => m.loadingMenu, run = m => NetworkMinder.StartHost() },
    };

    private static PuzzlePack target;    // the puzzle of the session being started (null: none)
    private static int size;             // its lobby size
    private static int step;             // the next of Steps; Steps.Length: waiting for the puzzle to be built
    private static float stepSince;      // when the current step began waiting
    private static int lastStepFrame;    // one step per frame, like a player clicking through
    private static bool newSave;         // there was no "PuzzleSDK Test" save: the settings screen gets its name and password
    private static int commandLineSize;  // --bpsdk-test: the lobby size to start once the main menu is up (0: none)

    internal static void Install() {
        CreatorMenu.AddSource(Entries);
        string[] args = Environment.GetCommandLineArgs();
        int at = Array.IndexOf(args, CommandLineFlag);
        if (at < 0) return;
        string value = at + 1 < args.Length ? args[at + 1] : "";
        if (value == "2" || value == "3" || value == "4") commandLineSize = int.Parse(value);
        else Plugin.log.LogError($"[Puzzle] {CommandLineFlag} takes 2, 3 or 4.");
    }

    internal static void Tick() {
        if (commandLineSize != 0) StartFromCommandLine();
        if (target == null) return;
        if (step < Steps.Length) RunStep(); else WaitForPuzzle();
    }

    // One entry per puzzle in the BigPuzzleSDK folder; picking one asks for the lobby size
    private static IEnumerable<CreatorMenu.Entry> Entries() {
        foreach (PuzzlePack pack in PuzzleLibrary.packs) {
            if (!pack.dev) continue;
            if (HasDebugTeleport(pack)) yield return new CreatorMenu.Entry(pack.name, () => CreatorMenu.OpenPage(pack.name, Sizes(pack)));
            else yield return new CreatorMenu.Entry($"{pack.name}: set Debug Teleport on its Puzzle SDK", null);
        }
    }

    private static List<CreatorMenu.Entry> Sizes(PuzzlePack pack) {
        var sizes = new List<CreatorMenu.Entry>();
        for (int players = 2; players <= 4; players++) {
            int lobby = players;
            sizes.Add(new CreatorMenu.Entry(lobby + " players", () => Start(pack, lobby)));
        }
        return sizes;
    }

    private static bool HasDebugTeleport(PuzzlePack pack) {
        PuzzleFieldData field = PuzzleLibrary.SdkField(pack, "debugTeleport");
        return field != null && !string.IsNullOrEmpty(field.value);
    }

    private static void StartFromCommandLine() {
        MainMenuManager menu = MainMenu.Current;
        if (menu == null || !(menu.titleMenu.gameObject.activeInHierarchy || menu.micCheckMenu.gameObject.activeInHierarchy)) return;
        int players = commandLineSize;
        commandLineSize = 0;
        PuzzlePack pack = PuzzleLibrary.packs.Find(p => p.dev && HasDebugTeleport(p));
        if (pack == null) {
            Plugin.log.LogError($"[Puzzle] {CommandLineFlag}: no puzzle in the BigPuzzleSDK folder has a Debug Teleport on its Puzzle SDK.");
            return;
        }
        menu.titleMenu.GoToHostMenu();
        Start(pack, players);
    }

    private static void Start(PuzzlePack pack, int players) {
        target = pack;
        size = players;
        step = 0;
        stepSince = Time.unscaledTime;
        lastStepFrame = Time.frameCount;
        CreatorMenu.Close();
        Plugin.log.LogInfo($"[Puzzle] Test session: hosting '{pack.name}' for {players} players");
    }

    private static void RunStep() {
        if (Time.frameCount == lastStepFrame) return;
        Step current = Steps[step];
        MainMenuManager menu = MainMenu.Current;
        MonoBehaviour shown = menu != null ? current.screen(menu) : null;
        if (shown == null || !shown.gameObject.activeInHierarchy) {
            if (Time.unscaledTime - stepSince > MenuTimeout) Fail($"the {current.name} did not show within {MenuTimeout:0} s");
            return;
        }
        current.run(menu);
        lastStepFrame = Time.frameCount;
        stepSince = Time.unscaledTime;
        step++;
    }

    // The "PuzzleSDK Test" save, made the first time
    private static void SelectSave(MainMenuManager menu) {
        var saves = SaveManager.GetAllSaveDatasInFolder();
        for (int i = 0; i < saves.Count; i++) {
            if (saves[i].slotName != SaveName) continue;
            newSave = false;
            menu.hostMenuSelect.ActionSelectSaveData(saves[i]);
            return;
        }
        newSave = true;
        menu.hostMenuSelect.StartNewGame();
    }

    private static void NameNewSave(MainMenuManager menu) {
        if (!newSave) return;
        menu.hostMenuConfirm.gameNameField.text = SaveName;
        menu.hostMenuConfirm.passwordField.text = SavePassword;
    }

    // Hosting: once the puzzle is built, the creator goes to its Debug Teleport and the test players to their spawns
    private static void WaitForPuzzle() {
        PuzzleSDK puzzle = PuzzleSDK.all.Find(p => p != null && p.packId == target.id);
        NetworkIdentity me = NetworkClient.localPlayer;
        if (puzzle == null || me == null) {
            if (Time.unscaledTime - stepSince > WorldTimeout)
                Fail(me == null ? $"the world did not load within {WorldTimeout:0} s" : $"the puzzle was not built within {WorldTimeout:0} s (see the [Puzzle] lines above)");
            return;
        }
        Transform start = puzzle.debugTeleport;
        Plugin.TeleportLocal(start);
        for (int i = 0; i < size - 1; i++) {
            Transform spawn = i < puzzle.testSpawns.Count ? puzzle.testSpawns[i] : null;
            if (spawn != null) TestPlayers.Spawn(spawn.position, Yaw(spawn));
            else TestPlayers.Spawn(start.position + start.right * (TestPlayers.SpawnDistance * (i + 1)), Yaw(start));   // in a row beside the creator
        }
        Plugin.log.LogInfo($"[Puzzle] Test session ready: '{target.name}', {size} players");
        target = null;
    }

    private static Quaternion Yaw(Transform t) { return Quaternion.Euler(0f, t.eulerAngles.y, 0f); }

    private static void Fail(string reason) {
        Plugin.log.LogError($"[Puzzle] Test session for '{target.name}' stopped: {reason}.");
        target = null;
    }
}

} // namespace BigPuzzleSDK
