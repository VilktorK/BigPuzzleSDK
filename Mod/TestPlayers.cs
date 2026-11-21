using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace BigPuzzleSDK {

// Extra bodies for testing a puzzle alone. The host spawns test players and takes control of any body with a key, so
// a puzzle made for several people can be tried by one. A body that is not controlled keeps its gestures and its
// crouch, so a pose can be held while the host stands somewhere else. They work only in a game this PC hosts.
//   Ctrl+F4            spawn a test player in front of the body you control (a puzzle must be in the BigPuzzleSDK folder)
//   Ctrl+Shift+F4      remove the last test player
//   F1 to F4           take control of body 1 to 4. Body 1 is your own, body 2 is Test 1, and so on. No Ctrl. They work
//                      only while a test player exists, so normal play never sees them.
internal static class TestPlayers {
    internal const float SpawnDistance = 1.5f;   // meters between a new test player and the body it is spawned next to
    private const int MaxBodies = 4;   // a Big Walk lobby holds 4 players, one key each
    private static readonly KeyCode[] BodyKeys = { KeyCode.F1, KeyCode.F2, KeyCode.F3, KeyCode.F4 };

    // [0] is the host's own body, then the test bodies in the order they were spawned
    private static readonly List<GameObject> bodies = new List<GameObject>();
    private static int controlled;   // the index in bodies of the body this PC controls
    private static int spawned;      // how many test bodies were spawned so far (they are numbered)

    internal static void HandleKeys(bool ctrl) {
        if (bodies.Count > 0 && !NetworkServer.active) {   // the game ended: the bodies are gone
            bodies.Clear();
            controlled = 0;
            spawned = 0;
        }
        if (!ctrl) {
            if (bodies.Count < 2) return;
            for (int i = 0; i < BodyKeys.Length; i++) {
                if (!UnityEngine.Input.GetKeyDown(BodyKeys[i])) continue;
                Select(i);
                return;
            }
            return;
        }
        if (!PuzzleLibrary.HasDevPuzzles || !UnityEngine.Input.GetKeyDown(KeyCode.F4)) return;
        if (!NetworkServer.active) {
            Plugin.log.LogInfo("[Test players] Only the host can spawn or swap test players.");
            return;
        }
        bool shift = UnityEngine.Input.GetKey(KeyCode.LeftShift) || UnityEngine.Input.GetKey(KeyCode.RightShift);
        if (shift) RemoveLast();
        else SpawnInFront();
    }

    // Spawns a test player where the body you control stands, a step ahead of it
    private static void SpawnInFront() {
        if (bodies.Count >= MaxBodies) {
            Plugin.log.LogInfo("[Test players] A Big Walk lobby holds 4 players. Ctrl+Shift+F4 removes a test player.");
            return;
        }
        NetworkIdentity me = NetworkClient.localPlayer;
        if (me == null) {
            Plugin.log.LogWarning("[Test players] There is no player of yours to spawn a test player next to yet.");
            return;
        }
        Quaternion facing = Quaternion.Euler(0f, me.transform.eulerAngles.y, 0f);
        Spawn(me.transform.position + facing * Vector3.forward * SpawnDistance, facing);
    }

    // Host: a new test player, standing at the position and facing the way given
    internal static void Spawn(Vector3 position, Quaternion rotation) {
        if (bodies.Count == 0) bodies.Add(NetworkClient.localPlayer.gameObject);
        GameObject body = UnityEngine.Object.Instantiate(NetworkManager.singleton.playerPrefab, position, rotation);
        spawned++;
        string name = "Test " + spawned;
        body.name = name;
        body.GetComponent<PlayerCharacter>().playerNetworking.Networkusername = name;
        // The game's PlayerNetworking.Start logs one NullReferenceException for each test player, a frame after
        // this. The test player works without whatever it missed.
        NetworkServer.Spawn(body);
        bodies.Add(body);
        Plugin.log.LogInfo($"[Test players] Spawned '{name}' ({bodies.Count} bodies). F{bodies.Count} takes control of it.");
    }

    private static void RemoveLast() {
        if (bodies.Count < 2) {
            Plugin.log.LogInfo("[Test players] There are no test players to remove.");
            return;
        }
        int last = bodies.Count - 1;
        if (controlled == last && !Control(0)) return;   // not the body you are in
        GameObject body = bodies[last];
        bodies.RemoveAt(last);
        string name = body.name;
        NetworkServer.Destroy(body);
        Plugin.log.LogInfo($"[Test players] Removed '{name}' ({bodies.Count} bodies).");
    }

    // Takes control of body 'index' (0 is your own, 1 is Test 1, ...), as F1 to F4 do
    internal static void Select(int index) {
        if (index >= bodies.Count) {
            Plugin.log.LogInfo($"[Test players] There is no body {index + 1}: there are {bodies.Count}. F1 is your own body.");
            return;
        }
        if (index != controlled && !Control(index)) return;
        LogBodies();
    }

    // The game's own switch of a player's body moves this PC's controls to it. The game sets up the camera rig, the
    // sound listener and the footstep scheduler only in the body this PC started in, so the rig and the listener
    // move along here.
    private static bool Control(int index) {
        if (!NetworkServer.ReplacePlayerForConnection(NetworkServer.localConnection, bodies[index], false)) {
            Plugin.log.LogError($"[Test players] The game refused to give you '{bodies[index].name}'.");
            return false;
        }
        PlayerCharacter from = bodies[controlled].GetComponent<PlayerCharacter>();
        PlayerCharacter to = bodies[index].GetComponent<PlayerCharacter>();
        PlayerCameraReferences rig = from.cameraMinder.playerCameraReferences;
        rig.transform.SetParent(to.cameraTransform, false);
        to.cameraMinder.playerCameraReferences = rig;
        from.cameraMinder.listenerMover.enabled = false;
        to.cameraMinder.listenerMover.enabled = true;
        // The scheduler logs an error every frame while its body is not the local player, so a test body walks
        // silently while you control it
        bodies[0].GetComponentInChildren<FootstepSoundJobScheduler>(true).enabled = index == 0;
        controlled = index;
        return true;
    }

    // One line per body: what a puzzle script would read from it
    private static void LogBodies() {
        for (int i = 0; i < bodies.Count; i++) {
            string you = i == controlled ? " (you)" : "";
            PuzzlePlayer player = PlayerOf(bodies[i]);
            if (player == null)
                Plugin.log.LogInfo($"[Test players] {bodies[i].name}: not in the player list yet{you}");
            else
                Plugin.log.LogInfo($"[Test players] {player.name}: left {player.leftArm}, right {player.rightArm}, crouch {player.isCrouching}{you}");
        }
    }

    private static PuzzlePlayer PlayerOf(GameObject body) {
        foreach (PuzzlePlayer player in PlayerRoster.View)
            if (player.Character != null && player.Character.gameObject == body) return player;
        return null;
    }
}

} // namespace BigPuzzleSDK
