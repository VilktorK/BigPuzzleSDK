using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace BigPuzzleSDK {

// Runtime (mod) version of the SDK's PuzzleStartSwitch: copies a vanilla knob switch into place.
// Flipping it away from its default position starts the puzzle; flipping it back stops it.
// A puzzle can have several; the ones that toggle the puzzle state all stay in the same position,
// and what happens on start and stop is set up on PuzzleSDK. With togglePuzzleState off it is a
// plain switch: it only fires onTurnedOn / onTurnedOff (which fire either way).
public class PuzzleStartSwitch : MonoBehaviour {
    public PuzzleStartSwitch(IntPtr ptr) : base(ptr) { }

    // Where the vanilla template sits (found from the game; see Templates in Plugin.cs)
    private const string ExactPath = GameRefs.StartSwitch.ExactPath;

    private const string CopyName = "VanillaStartSwitch";
    private const string PreferredPath = GameRefs.StartSwitch.PreferredPath;

    // Missing from packs made by older SDK builds, so it stays true for them (the loader only sets fields a pack has)
    private bool togglePuzzleState = true;
    private PuzzleEvent onTurnedOn;
    private PuzzleEvent onTurnedOff;
    private bool savesOnReload = false;   // remember the position when the lobby is restarted
    private bool defaultState = false;    // starts flipped on (the puzzle starts at load); a saved position takes priority. Missing from older packs: off
    internal bool SavesOnReload { get { return savesOnReload; } }
    internal bool Settled { get { return defaultKnown; } }   // its default position is known, so 'on' can be trusted
    internal bool TogglesPuzzle { get { return togglePuzzleState; } }
    public bool IsOn => on;

    internal bool on;   // true while the switch is flipped away from its default position

    internal static readonly List<PuzzleStartSwitch> all = new List<PuzzleStartSwitch>();

    private TrackedPeckState knobState;   // the game's record of this knob's position
    private float settleUntil;            // while another switch is moving this one, don't react

    private readonly List<PeckSwitch> switches = new List<PeckSwitch>();
    private readonly List<TrackedPeckState> states = new List<TrackedPeckState>();
    private List<ushort> ticketIds = new List<ushort>();   // this copy's network ids (see Tickets in Plugin.cs)
    private GameObject knobOn;        // 'knobState1' visual
    private GameObject knobOff;       // 'knobState0' visual
    private bool defaultKnown;
    private float readDefaultAt;
    private bool catchUp;             // this PC loaded with the knob already flipped (joined late): its first flip event runs here, not from the host

    // The knob's own default position is state 1 (the game's); flipped is the other value. The copy always starts from 1 on every PC,
    // whatever position the vanilla knob it was copied from was left in, so every PC means the same by "flipped".
    private const int DefaultValue = 1;

    // Called by the loader while the puzzle is still inactive
    internal void SetupSwitch(int index) {
        var log = Plugin.log;

        GameObject template = Templates.Find(ExactPath);
        if (template == null) {
            // Not at its known path: search every loaded object instead (slow)
            foreach (UnityEngine.Object obj in Resources.FindObjectsOfTypeAll(Il2CppType.Of<PeckSwitch>())) {
                PeckSwitch candidate = obj.TryCast<PeckSwitch>();
                if (candidate == null) continue;
                Transform knob = candidate.transform.parent;
                if (knob == null || !knob.name.StartsWith("BasicKnob")) continue;
                string path = PathOf(knob);
                if (path.Contains(CopyName)) continue;   // skip our own copies
                if (template == null) template = knob.gameObject;
                if (path.EndsWith(PreferredPath)) { template = knob.gameObject; break; }
            }
            Templates.FoundBySearch(this, null, ExactPath, template);
        }
        if (template == null) {
            log.LogWarning($"[PuzzleStartSwitch] No vanilla knob switch is loaded, so '{name}' has nothing to copy.");
            return;
        }

        Vector3 templateScale = template.transform.lossyScale;
        GameObject copy = Templates.Clone(template, transform);
        copy.name = CopyName;
        copy.transform.localPosition = Vector3.zero;
        copy.transform.localRotation = Quaternion.identity;
        copy.SetActive(true);

        Vector3 parentScale = transform.lossyScale;
        copy.transform.localScale = new Vector3(
            Mathf.Approximately(parentScale.x, 0f) ? templateScale.x : templateScale.x / parentScale.x,
            Mathf.Approximately(parentScale.y, 0f) ? templateScale.y : templateScale.y / parentScale.y,
            Mathf.Approximately(parentScale.z, 0f) ? templateScale.z : templateScale.z / parentScale.z);

        foreach (Component t in copy.GetComponentsInChildren(Il2CppType.Of<Transform>(), true)) {
            if (t.name == "knobState1") knobOn = t.gameObject;
            if (t.name == "knobState0") knobOff = t.gameObject;
        }

        // The game switches off renderers of far-away objects; the copy inherits that
        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<Renderer>(), true)) {
            Renderer r = c != null ? c.TryCast<Renderer>() : null;
            if (r != null) r.enabled = true;
        }

        // Hide the creator's placeholder
        Renderer placeholder = GetComponent<Renderer>();
        if (placeholder != null) placeholder.enabled = false;
        Collider placeholderCollider = GetComponent<Collider>();
        if (placeholderCollider != null) placeholderCollider.enabled = false;

        // Everything that carries a network id must get its own, or the copy would flip the
        // vanilla switch it was copied from.
        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<PeckSwitch>(), true)) {
            PeckSwitch sw = c != null ? c.TryCast<PeckSwitch>() : null;
            if (sw == null) continue;
            switches.Add(sw);
            TrackedPeckState target = sw.trackedStateSystem;
            if (knobState == null && target != null && sw.GetComponent<CastableTarget>() != null) knobState = target;
        }
        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<TrackedPeckState>(), true)) {
            TrackedPeckState st = c != null ? c.TryCast<TrackedPeckState>() : null;
            if (st == null) continue;
            states.Add(st);
        }
        if (knobState == null && states.Count > 0) knobState = states[0];
        if (knobState != null) { knobState.hasInitialState = true; knobState.initialState = DefaultValue; }
        ticketIds = Tickets.Allocate(switches.Count + states.Count);
        AssignTickets(false);

        if (knobOn == null) log.LogWarning($"[PuzzleStartSwitch] The copy for '{name}' has no 'knobState1', so flips cannot be detected.");
    }

    private static string PathOf(Transform t, Transform stopAt = null) {
        string path = t.name;
        for (Transform p = t.parent; p != null && p != stopAt; p = p.parent) path = p.name + "/" + path;
        return path;
    }

    private void AssignTickets(bool register) {
        int slot = 0;
        foreach (PeckSwitch sw in switches) {
            if (register) Register(sw.Cast<LobbyNetworking.ITicketed>(), sw.ticket, ticketIds[slot]);
            sw.useTicket = true;
            sw.ticket = ticketIds[slot];
            sw.shellReference = new SeaShell.ShellReference(ticketIds[slot]);
            slot++;
        }
        foreach (TrackedPeckState st in states) {
            if (register) Register(st.Cast<LobbyNetworking.ITicketed>(), st.ticket, ticketIds[slot]);
            st.ticket = ticketIds[slot];
            st.shellReference = new SeaShell.ShellReference(ticketIds[slot]);
            slot++;
        }
    }

    private static void Register(LobbyNetworking.ITicketed item, ushort current, ushort wanted) {
        var office = LobbyNetworking.TicketOffice.instance;
        if (office == null) return;
        try {
            var tickets = office.tickets;
            if (current != wanted && tickets.ContainsKey(current) && tickets[current].Pointer == item.Pointer)
                tickets.Remove(current);
            tickets[wanted] = item;
        } catch (Exception ex) {
            Plugin.log.LogWarning($"[PuzzleStartSwitch] id registration failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void OnDestroy() { all.Remove(this); }

    private void Start() {
        all.Add(this);
        AssignTickets(true);
        readDefaultAt = Time.time + 0.5f;   // let the game put the knob in its default position first
    }

    // Flipped = the knob's state is not its default value (read from the state itself, not from which knob picture is showing,
    // so it is right on every PC, players who join later included)
    private bool ReadOn() {
        if (PeckValue.TryRead(knobState, out int value)) return value != DefaultValue;
        return knobOn != null && !knobOn.activeSelf;   // fallback: knobState1 showing = default position
    }

    private void Update() {
        if (knobState == null && knobOn == null) return;

        if (!defaultKnown) {
            if (Time.time < readDefaultAt) return;
            defaultKnown = true;
            // Where it starts: the saved position if there is one, else Default State. Only the host decides; the others follow its value.
            bool wantOn = defaultState;
            if (savesOnReload && PuzzleSDK.IsHost) { bool? saved = PuzzleSave.SavedStartSwitch(this); if (saved.HasValue) wantOn = saved.Value; }
            // On: the host moves the knob. Every PC then sees it move like a player's flip, so a switch that toggles
            // the puzzle state starts the puzzle (and fires On Turned On) everywhere.
            if (PuzzleSDK.IsHost && wantOn) { SetKnob(true); return; }
            // Off, or a player who joined while it was on: like PuzzleSwitch, the event for the position it loaded in fires once on this PC
            if (!ReadOn()) { if (onTurnedOff != null) onTurnedOff.InvokeLocal(); }
            else catchUp = !PuzzleSDK.IsHost;   // the flip below (this PC missed the host's event) runs its events here
            return;
        }

        bool nowOn = ReadOn();
        if (Time.time < settleUntil) return;   // being moved by another switch
        if (nowOn == on) { settleUntil = 0f; return; }

        if (settleUntil > 0f) {
            // Another switch tried to move this one and it did not follow
            settleUntil = 0f;
            Plugin.log.LogWarning($"[PuzzleStartSwitch] '{name}' did not follow the other switch (it is {(nowOn ? "on" : "off")})");
            on = nowOn;
            return;
        }
        on = nowOn;
        FireEvents();
        if (!togglePuzzleState) return;   // a plain switch: nothing else to start, stop or keep in step

        // A player flipped this one: move every other start switch in the puzzle to match
        int myRoot = transform.root.GetInstanceID();
        int moved = 0;
        foreach (PuzzleStartSwitch other in all.ToArray()) {
            if (other == null || other.Pointer == Pointer || !other.togglePuzzleState || other.transform.root.GetInstanceID() != myRoot) continue;
            other.Follow(on);
            moved++;
        }

        PuzzleSDK puzzle = FindPuzzle();
        if (puzzle == null) { Plugin.log.LogWarning($"[PuzzleStartSwitch] '{name}' found no PuzzleSDK in its puzzle"); return; }
        if (on) puzzle.RunStart(); else puzzle.RunStop();
    }

    // The host decides when it happens; the other players run it when the host's bump arrives
    private void FireEvents() {
        if (catchUp) {   // joined while it was on: the host's event was before this player arrived, so run it here
            catchUp = false;
            if (on) { if (onTurnedOn != null) onTurnedOn.InvokeLocal(); }
            else { if (onTurnedOff != null) onTurnedOff.InvokeLocal(); }
            return;
        }
        if (on) { if (onTurnedOn != null) onTurnedOn.Invoke(); }
        else { if (onTurnedOff != null) onTurnedOff.Invoke(); }
    }

    // Moves this switch to match one a player flipped, without starting or stopping anything
    private void Follow(bool wantOn, bool fire = true) {
        if (!defaultKnown || on == wantOn) return;
        on = wantOn;
        if (fire) FireEvents();
        settleUntil = Time.time + 0.75f;
        SetKnob(wantOn);
    }

    // Moves the knob in the world (host only: the other players receive it from the host)
    private void SetKnob(bool wantOn) {
        if (knobState == null) return;
        try {
            int defaultValue = knobState.hasInitialState ? knobState.initialState : 0;
            int onValue = defaultValue == 0 ? 1 : 0;
            if (PuzzleSDK.IsHost) knobState.SetState(wantOn ? onValue : defaultValue);
        } catch (Exception ex) {
            Plugin.log.LogWarning($"[PuzzleStartSwitch] '{name}' could not move: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // Whether a script can flip this switch yet (its default position has been read)
    internal bool CanFlip { get { return defaultKnown && knobState != null; } }

    // A script started or stopped the puzzle (host only): moves this knob in the world, which the game sends to every
    // other player, and whose own PC then starts or stops the puzzle. Does not start or stop anything itself.
    internal void FlipFromScript(bool wantOn, bool callFunc = true) {
        if (!CanFlip) return;
        Follow(wantOn, callFunc);
    }

    // Host only: a script flips this switch. One that toggles the puzzle state starts / stops the puzzle (moving the others too);
    // a plain one just moves. callFunc: also fire onTurnedOn / onTurnedOff.
    public void TurnOn(bool callFunc = true) { Script(true, callFunc); }
    public void TurnOff(bool callFunc = true) { Script(false, callFunc); }

    private void Script(bool wantOn, bool callFunc) {
        if (!PuzzleSDK.IsHost || !CanFlip) return;
        if (togglePuzzleState) {
            PuzzleSDK puzzle = FindPuzzle();
            if (puzzle != null) { puzzle.SetRunning(wantOn, callFunc); return; }
        }
        Follow(wantOn, callFunc);
    }

    private PuzzleSDK FindPuzzle() {
        int myRoot = transform.root.GetInstanceID();
        foreach (PuzzleSDK puzzle in PuzzleSDK.all)
            if (puzzle != null && puzzle.transform.root.GetInstanceID() == myRoot) return puzzle;
        return null;
    }
}

} // namespace BigPuzzleSDK
