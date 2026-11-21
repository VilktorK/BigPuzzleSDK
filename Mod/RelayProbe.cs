using System;
using System.Collections.Generic;
using System.Reflection;
using Il2CppInterop.Runtime;
using Mirror;
using UnityEngine;

namespace BigPuzzleSDK {

// TEMPORARY check of the Pose Relay versions per lobby size (--bpsdk-probe-relay with --bpsdk-test N): the start
// switch, the reader's first press, five right locks and the win. Delete after use.
internal static class RelayProbe {
    private static int step;
    private static float next;
    private static readonly List<Action> plan = new List<Action>();
    private static int n;
    private static PuzzleSDK sdk;
    private static Transform root, version;
    private static object relay;
    private static Type relayType;
    private static PlayerCharacter creator, poser, reader;
    private static CastableTarget switchTarget, buttonTarget;
    private static readonly List<PuzzleDoor> doors = new List<PuzzleDoor>();

    internal static void Tick() {
        if (step < 0 || Time.unscaledTime < next) return;
        if (step == 0) {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--bpsdk-probe-relay") < 0) { step = -1; return; }
            Build();
            step = 1;
        }
        try {
            if (step - 1 >= plan.Count) { step = -1; return; }
            Action action = plan[step - 1];
            step++;
            action();
        } catch (Exception ex) {
            Log($"PROBE FAIL at step {step - 1}: {ex}");
            step = -1;
        }
    }

    private static void Wait(float seconds) { next = Time.unscaledTime + seconds; }
    private static void Expect(bool ok, string what) { if (!ok) throw new Exception("expected " + what); }
    private static Vector3 V(float x, float y, float z) { return new Vector3(x, y, z); }

    private static void Build() {
        int size = int.Parse(Environment.GetCommandLineArgs()[Array.IndexOf(Environment.GetCommandLineArgs(), "--bpsdk-test") + 1]);
        plan.Add(() => {
            if (PlayerCharacter.allPlayerCharacters == null || PlayerCharacter.allPlayerCharacters.Count < size) { step--; Wait(0.5f); return; }
            n = size;
            Wait(3f);
        });
        plan.Add(() => { Setup(); State("ready"); Place(creator, V(-3.2f, 0.1f, 11.5f), -90f); Wait(1f); });
        plan.Add(() => { Click(creator, switchTarget, "start switch"); Wait(3f); });
        plan.Add(() => {
            State("after the switch");
            Expect(sdk.IsRunning && doors.TrueForAll(d => !d.IsOpen) && !Field<bool>("running"), "running, every door closed, relay waiting");
            if (poser != creator) Place(creator, V(0f, 3.1f, -2.5f), 0f);
            Place(poser, V(0f, 0.1f, 10.5f), 90f);
            TestPlayers.Select(n - 1);
            Wait(0.5f);
        });
        plan.Add(() => {
            // The reader stands at their button, by the window of the top tier, facing the window
            bool sideA = n % 2 == 1;
            float floor = 3f * (n - 1), gap = sideA ? 8f : 0f, inward = sideA ? 1f : -1f;
            Place(reader, V(2.6f, floor + 0.1f, gap + inward * 0.8f), sideA ? 180f : 0f);
            Wait(1f);
        });
        plan.Add(() => { Click(reader, buttonTarget, "button (start)"); Wait(1.2f); });
        plan.Add(() => { State("after the first press"); Expect(Field<bool>("running") && CardImage() > 0, "relay running and a pose shown"); Wait(0.1f); });
        for (int i = 1; i <= 5; i++) {
            int round = i;
            plan.Add(() => { PoseForShownImage(); Wait(0.3f); });
            plan.Add(() => { Click(reader, buttonTarget, "button (lock " + round + ")"); Wait(1.2f); });
            plan.Add(() => { State("after lock " + round); Expect(Field<int>("round") == round, "round " + round); Wait(0.1f); });
        }
        plan.Add(() => { Expect(sdk.IsWon, "won"); Wait(3.5f); });
        plan.Add(() => {
            State("3.5 s after the win");
            Expect(!sdk.IsRunning && doors.TrueForAll(d => d.IsOpen), "stopped, every door open");
            Log($"PROBE DONE: {n} players");
        });
    }

    private static void Setup() {
        sdk = PuzzleSDK.all.Find(p => p != null && p.transform.root.name.Contains("PoseRelay"));
        if (sdk == null) throw new Exception("no PoseRelay PuzzleSDK");
        root = sdk.transform;
        version = root.Find(n + " Players");
        if (version == null || !version.gameObject.activeInHierarchy) throw new Exception($"'{n} Players' is missing or hidden");
        for (int m = 2; m <= 4; m++) {
            Transform other = root.Find(m + " Players");
            Log($"version '{m} Players': {(other.gameObject.activeInHierarchy ? "shown" : "hidden")}");
            Expect(other.gameObject.activeInHierarchy == (m == n), $"only '{n} Players' shown");
        }
        foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies()) {
            Type t = a.GetType("PoseRelay");
            if (t != null) { relayType = t; break; }
        }
        foreach (MonoBehaviour mb in version.GetComponentsInChildren<MonoBehaviour>(true))
            if (mb.GetIl2CppType().Name == "PoseRelay")
                relay = typeof(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase).GetMethod("TryCast").MakeGenericMethod(relayType).Invoke(mb, null);
        if (relay == null) throw new Exception("no PoseRelay in the version");

        var tests = new Dictionary<string, PlayerCharacter>();
        foreach (PlayerCharacter pc in PlayerCharacter.allPlayerCharacters) {
            string name = pc.playerNetworking.username;
            if (name.StartsWith("Test ")) tests[name] = pc; else creator = pc;
        }
        reader = tests["Test " + (n - 1)];
        poser = n == 2 ? creator : tests["Test 1"];

        int switches = 0, vices = 0;
        foreach (PuzzleStartSwitch s in PuzzleStartSwitch.all) {
            if (s == null || s.transform.root != root.root) continue;
            switches++;
            switchTarget = FirstTarget(s.transform);
        }
        foreach (PuzzleGourdVice v in PuzzleGourdVice.all) if (v != null && v.transform.root == root.root) vices++;
        Log($"set up: {switches} start switch, {vices} gourd vice");
        Expect(switches == 1 && vices == 1 && switchTarget.transform.IsChildOf(version), "one start switch and one vice, in the version");
        foreach (Component c in version.GetComponentsInChildren(Il2CppType.Of<PuzzleDoor>(), false)) doors.Add(c.TryCast<PuzzleDoor>());
        foreach (Component c in version.GetComponentsInChildren(Il2CppType.Of<PuzzlePokeButton>(), false)) buttonTarget = FirstTarget(c.transform);
        Expect(doors.Count == n && buttonTarget != null, $"{n} doors and a button");
    }

    private static CastableTarget FirstTarget(Transform under) {
        foreach (Component c in under.GetComponentsInChildren(Il2CppType.Of<CastableTarget>(), true)) return c.TryCast<CastableTarget>();
        return null;
    }

    private static void Place(PlayerCharacter pc, Vector3 local, float yaw) {
        PlayerCommands.Place(pc, version.TransformPoint(local), version.rotation * Quaternion.Euler(0f, yaw, 0f));
    }

    private static void Click(PlayerCharacter pc, CastableTarget target, string label) {
        bool local = NetworkClient.localPlayer != null && NetworkClient.localPlayer.gameObject == pc.gameObject;
        Log($"click {label} by {pc.playerNetworking.username} (local {local}, safe {pc.decisions.IsSafeToUseCastableTarget(target, false)})");
        Expect(local, "the clicking body to be the local player");
        pc.decisions.UseCastableTarget(target);
    }

    private static T Field<T>(string name) { return (T)relayType.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(relay); }
    private static int CardImage() { return Field<PuzzleDisplay>("card").Image; }

    private static void State(string label) {
        var times = new List<string>();
        foreach (PuzzleTimer timer in Field<PuzzleTimer[]>("timers")) times.Add((timer.IsRunning ? "run " : "stop ") + timer.TimeLeft.ToString("0.0"));
        var progress = new List<string>();
        foreach (PuzzleProgressBar bar in Field<PuzzleProgressBar[]>("bars")) progress.Add(bar.Progress + "/" + bar.Total);
        var open = new List<string>();
        foreach (PuzzleDoor d in doors) open.Add(d.transform.parent.name + (d.IsOpen ? " open" : " closed"));
        Log($"{label}: sdkRunning={sdk.IsRunning} won={sdk.IsWon} doors=[{string.Join(", ", open)}] relayRunning={Field<bool>("running")} round={Field<int>("round")} card image {CardImage()} timers=[{string.Join(" ", times)}] bars=[{string.Join(" ", progress)}]");
    }

    // The poser holds the shown image's pose, their own arms as drawn
    private static void PoseForShownImage() {
        int image = Field<int[]>("images")[Field<int>("round")];
        object pose = ((Array)relayType.GetField("Poses", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null)).GetValue(image - 1);
        Type poseType = pose.GetType();
        var left = (PuzzleArm)poseType.GetField("Item1").GetValue(pose);
        var right = (PuzzleArm)poseType.GetField("Item2").GetValue(pose);
        var crouch = (bool)poseType.GetField("Item3").GetValue(pose);
        PlayerNetworking net = poser.playerNetworking;
        net.NetworkleftArmWaving = left == PuzzleArm.Up;
        net.NetworkleftArmPointing = left == PuzzleArm.Out;
        net.NetworkrightArmWaving = right == PuzzleArm.Up;
        net.NetworkrightArmPointing = right == PuzzleArm.Out;
        net.NetworktrueCrouchness = crouch ? 1f : 0f;
        Log($"posed {poser.playerNetworking.username} for image {image}: left {left}, right {right}, crouch {crouch}");
    }

    private static void Log(string message) { Plugin.log.LogInfo("[RelayProbe] " + message); }
}

} // namespace BigPuzzleSDK
