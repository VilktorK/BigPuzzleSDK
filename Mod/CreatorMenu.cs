using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BigPuzzleSDK {

// The creator menu: a PUZZLE SDK card on the Host Game screen, under START A NEW GAME, that opens pages of creator
// tools. A tool adds its entries with AddSource (TestSession lists the puzzles being tested), and an entry can open a
// page of its own with OpenPage. The card and the pages are copies of the Host Game screen's own objects, so they look,
// sound and navigate like the game. Shown only while a puzzle is in the BigPuzzleSDK folder (see Plugin.DevFolder).
internal static class CreatorMenu {
    internal struct Entry {
        public string label;
        public Action onClick;   // null: the entry is shown, but can't be clicked
        public Entry(string label, Action onClick) { this.label = label; this.onClick = onClick; }
    }

    private const string MenuTitle = "PUZZLE SDK";

    private class Page { public string title; public List<Entry> entries; }

    private static readonly List<Func<IEnumerable<Entry>>> sources = new List<Func<IEnumerable<Entry>>>();
    private static readonly List<Page> pages = new List<Page>();   // the open pages, the one shown last

    private static GameObject holder;    // inactive: copies are made under it, so no game script wakes on them early
    private static GameObject hostCard;  // the PUZZLE SDK card on the Host Game screen
    private static GameObject screen;    // the creator menu's screen, shared by every page
    private static bool broken;          // the Host Game screen isn't laid out as GameRefs.HostMenu says: the menu stays off

    // Adds entries to the menu's first page. The source is asked again each time the menu opens.
    internal static void AddSource(Func<IEnumerable<Entry>> source) { sources.Add(source); }

    // Every frame: puts the PUZZLE SDK card on the Host Game screen while it is shown
    internal static void Tick() {
        if (broken || !PuzzleLibrary.HasDevPuzzles) return;
        MainMenuManager menu = MainMenu.Current;
        if (menu == null) return;
        HostMenuSelect host = menu.hostMenuSelect;
        if (host == null || !host.gameObject.activeInHierarchy) return;
        try {
            Transform list = Find(host.transform, GameRefs.HostMenu.List);
            if (hostCard == null || hostCard.transform.parent != list) hostCard = MakeCard(host, list, MenuTitle, OpenMenu);
            if (hostCard.transform.GetSiblingIndex() != 1) hostCard.transform.SetSiblingIndex(1);   // right under START A NEW GAME
        } catch (Exception ex) {
            broken = true;
            Plugin.log.LogError($"[Creator menu] The Puzzle SDK menu is off: {ex.Message}");
        }
    }

    // Shows a page of entries, over the current one. Back returns to the current one.
    internal static void OpenPage(string title, List<Entry> entries) {
        pages.Add(new Page { title = title, entries = entries });
        Show();
    }

    // Closes every page and shows the Host Game screen again
    internal static void Close() {
        pages.Clear();
        if (screen == null || !screen.activeSelf) return;
        screen.SetActive(false);
        MainMenu.Current.hostMenuSelect.gameObject.SetActive(true);
    }

    private static void OpenMenu() {
        var entries = new List<Entry>();
        foreach (Func<IEnumerable<Entry>> source in sources) entries.AddRange(source());
        pages.Clear();
        OpenPage(MenuTitle, entries);
    }

    private static void Back() {
        pages.RemoveAt(pages.Count - 1);
        if (pages.Count > 0) Show(); else Close();
    }

    private static void Show() {
        HostMenuSelect host = MainMenu.Current.hostMenuSelect;
        GameObject shown = Screen(host);
        Page page = pages[pages.Count - 1];
        Find(shown.transform, GameRefs.HostMenu.Title).GetComponent<TextMeshProUGUI>().text = page.title;
        Transform list = Find(shown.transform, GameRefs.HostMenu.List);
        for (int i = list.childCount - 1; i >= 0; i--) {
            GameObject old = list.GetChild(i).gameObject;
            old.SetActive(false);
            UnityEngine.Object.Destroy(old);   // at the end of the frame: it may hold the button being clicked
        }
        GameObject first = null;   // the first card that can be clicked
        foreach (Entry entry in page.entries) {
            GameObject card = MakeCard(host, list, entry.label, entry.onClick);
            if (first == null && entry.onClick != null) first = Find(card.transform, GameRefs.HostMenu.NewGameButton).gameObject;
        }
        host.gameObject.SetActive(false);
        shown.SetActive(true);
        // keyboard and gamepad go on from here
        EventSystem.current.SetSelectedGameObject(first ?? Find(shown.transform, GameRefs.HostMenu.BackButton).gameObject);
    }

    // The creator menu's screen: a copy of the Host Game screen with its title, back button and an empty column of
    // cards. It is one of the game's menus, so the game hides it when it changes menus, and Escape on it is Back.
    private static GameObject Screen(HostMenuSelect host) {
        if (screen != null) return screen;   // made again after the main menu is loaded again
        GameObject made = Copy(host.gameObject);
        made.name = "PuzzleSDK Creator Menu";
        UnityEngine.Object.DestroyImmediate(made.GetComponent<HostMenuSelect>());
        foreach (EventTrigger trigger in made.GetComponentsInChildren<EventTrigger>(true)) UnityEngine.Object.DestroyImmediate(trigger);
        UnityEngine.Object.DestroyImmediate(Find(made.transform, GameRefs.HostMenu.Title).GetComponent<LocalizedText>());

        Transform back = Find(made.transform, GameRefs.HostMenu.BackButton);
        Transform bottomLeft = Find(made.transform, GameRefs.HostMenu.BottomLeft);
        for (int i = bottomLeft.childCount - 1; i >= 0; i--)
            if (bottomLeft.GetChild(i) != back) UnityEngine.Object.DestroyImmediate(bottomLeft.GetChild(i).gameObject);   // the join code
        Rebind(back.GetComponent<Button>().onClick, Back);
        MenuScreenBack escape = made.GetComponent<MenuScreenBack>();
        Rebind(escape.backAction, Back);

        Transform list = Find(made.transform, GameRefs.HostMenu.List);
        for (int i = list.childCount - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(list.GetChild(i).gameObject);

        made.SetActive(false);
        made.transform.SetParent(host.transform.parent, false);
        MainMenu.Current.menus.Add(escape);
        screen = made;
        return screen;
    }

    // A copy of the START A NEW GAME card with its own label and click
    private static GameObject MakeCard(HostMenuSelect host, Transform parent, string label, Action onClick) {
        GameObject card = Copy(Find(host.transform, GameRefs.HostMenu.NewGameCard).gameObject);
        card.name = "PuzzleSDK " + label;
        UnityEngine.Object.DestroyImmediate(Find(card.transform, GameRefs.HostMenu.OldNewGameButton).gameObject);
        foreach (LocalizedText text in card.GetComponentsInChildren<LocalizedText>(true)) UnityEngine.Object.DestroyImmediate(text);   // it would set the game's label again
        foreach (EventTrigger trigger in card.GetComponentsInChildren<EventTrigger>(true)) UnityEngine.Object.DestroyImmediate(trigger);

        Transform buttonObject = Find(card.transform, GameRefs.HostMenu.NewGameButton);
        buttonObject.GetComponentInChildren<TextMeshProUGUI>(true).text = label;
        Button button = buttonObject.GetComponent<Button>();
        Rebind(button.onClick, onClick);
        button.interactable = onClick != null;
        ManagedButton managed = buttonObject.GetComponent<ManagedButton>();
        Rebind(managed.onLeft, null);    // they move the selection on the Host Game screen
        Rebind(managed.onRight, null);

        card.transform.SetParent(parent, false);
        return card;
    }

    // A copy made inside the inactive holder: no game script on it wakes before it is moved out
    private static GameObject Copy(GameObject original) {
        if (holder == null) {
            holder = new GameObject("BigPuzzleSDK Creator Menu Holder");
            holder.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(holder);
        }
        return UnityEngine.Object.Instantiate(original, holder.transform);
    }

    // A copy still calls the game's own methods: those are switched off, and the action (if any) is called instead
    private static void Rebind(UnityEvent unityEvent, Action action) {
        int count = unityEvent.GetPersistentEventCount();
        for (int i = 0; i < count; i++) unityEvent.SetPersistentListenerState(i, UnityEventCallState.Off);
        unityEvent.RemoveAllListeners();
        if (action != null) unityEvent.AddListener(action);
    }

    private static Transform Find(Transform root, string path) {
        Transform found = root.Find(path);
        if (found == null) throw new InvalidOperationException($"'{path}' is missing under '{root.name}'. A Big Walk update moved it: fix GameRefs.HostMenu.");
        return found;
    }
}

} // namespace BigPuzzleSDK
