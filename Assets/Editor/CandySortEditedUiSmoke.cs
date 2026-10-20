// Explicit, editor-only regression probe for the user's authored Candy Sort UI.
// Never saves a scene, changes PlayerPrefs, destroys UI, or runs on ordinary imports/Play.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CandySort;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class CandySortEditedUiSmoke
{
    private const string ScenePath = "Assets/Scenes/CandySortGame.unity";
    private const string RequestPath = "Temp/CandySortEditedUiSmoke.request";
    private const string OutputPath = "Temp/CandySortVerification";
    private const string ReportPath = OutputPath + "/edited-ui-report.txt";
    private const string StateKey = "CandySortEditedUiSmoke.State";
    private const string SnapshotKey = "CandySortEditedUiSmoke.Snapshot";
    private const string ErrorKey = "CandySortEditedUiSmoke.Error";
    private const string DeadlineKey = "CandySortEditedUiSmoke.Deadline";
    private static readonly BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly int[] Levels = { 1, 2, 3, 4, 5, 4, 3, 2, 1 };
    private static readonly string[] AuthoredReferences =
    {
        "sceneCamera", "root", "plateLayer", "tempLayer", "board", "bubbleLayer", "modalLayer", "effectLayer",
        "levelLabel", "difficultyLabel", "progressLabel", "hintLabel", "queueLabel", "progressFill",
        "helpButton", "restartButton", "undoButton"
    };
    private static CandySortGameController controller;
    private static AuthoredSnapshot authored;
    private static Dictionary<string, int> transformIds, imageIds;
    private static Dictionary<string, object> controllerReferences;
    private static int stage, phase;
    private static double nextTick, screenshotDeadline;
    private static string screenshot;
    private static string beforeMove;
    private static int moveBubble, moveIndex, trayBefore;
    private static CandyKind moveKind;

    [Serializable] private sealed class AuthoredSnapshot
    {
        public List<RectState> rects = new List<RectState>();
        public List<PlateArt> plates = new List<PlateArt>();
    }

    [Serializable] private sealed class RectState
    {
        public string path;
        public Vector2 anchorMin, anchorMax, pivot, size;
        public Vector3 position, scale;
        public Quaternion rotation;
        public int sibling;
    }

    [Serializable] private sealed class PlateArt
    {
        public string path, dishPath, dishSprite, lockPath, lockSprite;
    }

    static CandySortEditedUiSmoke()
    {
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += PlayModeChanged;
        Application.logMessageReceived += OnLog;
    }

    [MenuItem("Candy Sort/Run Edited UI Regression Smoke Test")]
    public static void RequestSmoke()
    {
        // Also callable through -executeMethod CandySortEditedUiSmoke.RequestSmoke.
        Directory.CreateDirectory("Temp");
        File.WriteAllText(RequestPath, "run");
    }

    private static void Tick()
    {
        string state = SessionState.GetString(StateKey, "");
        try
        {
            if (state == "")
            {
                if (EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(RequestPath)) return;
                File.Delete(RequestPath); // One-shot, like CandySortEditor; a static constructor is not a request.
                Directory.CreateDirectory(OutputPath);
                File.WriteAllText(ReportPath, "Candy Sort edited UI: actual Play Mode regression smoke\n");
                Check(!EditorApplication.isPlayingOrWillChangePlaymode, "Stop Play mode before requesting this test.");
                Check(SceneManager.GetActiveScene().path == ScenePath, "Open the customized CandySortGame scene first.");
                Check(!Enumerable.Range(0, SceneManager.sceneCount).Any(i => SceneManager.GetSceneAt(i).isDirty),
                    "Save or discard pending scene edits before requesting this test (the test never saves).");
                Check(!SessionState.GetBool("CandySortSmokeActive", false) && !File.Exists("Temp/CandySortSmoke.request"),
                    "Do not run the existing CandySortEditor smoke at the same time.");
                controller = FindController();
                authored = CaptureAuthored();
                SessionState.SetString(SnapshotKey, JsonUtility.ToJson(authored));
                SessionState.SetString(ErrorKey, "");
                SessionState.SetFloat(DeadlineKey, (float)EditorApplication.timeSinceStartup + 150);
                SessionState.SetString(StateKey, "entering");
                // No serialized edit-mode settings are touched. Runtime copies are changed on entry.
                EditorWindow.GetWindow(typeof(Editor).Assembly.GetType("UnityEditor.GameView")).Focus();
                EditorApplication.isPlaying = true;
                return;
            }
            if (state == "stopping")
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.isPlaying = false;
                else ResetSession();
                return;
            }
            Check(EditorApplication.timeSinceStartup < SessionState.GetFloat(DeadlineKey, 0), "Smoke timed out.");
            string logged = SessionState.GetString(ErrorKey, "");
            Check(logged.Length == 0, "Controller/NullReference exception in Unity log:\n" + logged);
            if (EditorApplication.isCompiling) throw new InvalidOperationException("Compilation interrupted the smoke.");
            if (state == "entering") return; // EnteredPlayMode restores state after the normal domain reload.
            Check(EditorApplication.isPlaying, "Play mode stopped before the smoke completed.");
            Check(authored != null && transformIds != null, "An unexpected domain reload interrupted the smoke.");
            if (EditorApplication.timeSinceStartup < nextTick) return;
            Check(!EditorApplication.isPaused, "Play mode is paused (including Error Pause).");
            if (screenshot != null)
            {
                if (!File.Exists(screenshot) || new FileInfo(screenshot).Length == 0)
                {
                    Check(EditorApplication.timeSinceStartup < screenshotDeadline, "Screenshot was not written: " + screenshot);
                    EditorApplication.QueuePlayerLoopUpdate();
                    return;
                }
                Pass("Screenshot: " + screenshot);
                screenshot = null;
            }
            RunStep();
        }
        catch (Exception exception)
        {
            // A rejected request must not stop a Play session the user started themselves.
            Finish(false, exception.ToString(), SessionState.GetString(StateKey, "") != "");
        }
    }

    private static void PlayModeChanged(PlayModeStateChange change)
    {
        string state = SessionState.GetString(StateKey, "");
        if (state == "") return;
        try
        {
            if (change == PlayModeStateChange.EnteredPlayMode && state == "entering")
            {
                controller = FindController();
                // Only the live Play copy is modified; no SerializedObject, SetDirty or scene save.
                Set(controller, "rememberProgress", false);
                Set(controller, "startingLevel", 1);
                authored = JsonUtility.FromJson<AuthoredSnapshot>(SessionState.GetString(SnapshotKey, ""));
                transformIds = CurrentRects().ToDictionary(PathOf, r => r.GetInstanceID());
                imageIds = CurrentImages().ToDictionary(i => PathOf(i.transform), i => i.GetInstanceID());
                controllerReferences = AuthoredReferences.ToDictionary(name => name, name => Get(controller, name));
                stage = phase = 0;
                screenshot = null;
                nextTick = EditorApplication.timeSinceStartup + .5;
                SessionState.SetString(StateKey, "running");
                VerifyRetained();
            }
            else if (change == PlayModeStateChange.EnteredEditMode)
            {
                if (state != "stopping") File.AppendAllText(ReportPath, "FAIL: Play mode exited before completion.\n");
                ResetSession();
            }
        }
        catch (Exception exception) { Finish(false, exception.ToString(), true); }
    }

    private static void OnLog(string message, string stack, LogType type)
    {
        if (SessionState.GetString(StateKey, "") == "" || SessionState.GetString(StateKey, "") == "stopping") return;
        if (type != LogType.Exception && type != LogType.Error && type != LogType.Assert) return;
        // NREs sometimes arrive without a useful managed stack; fail those too rather than silently pass.
        if (!message.Contains("NullReferenceException") && !message.Contains("CandySortGameController") &&
            !stack.Contains("CandySortGameController")) return;
        if (SessionState.GetString(ErrorKey, "").Length == 0) SessionState.SetString(ErrorKey, message + "\n" + stack);
    }

    private static void RunStep()
    {
        Check(Get(controller, "model") != null, "Controller Start initialized its model.");
        Check(!(bool)Get(controller, "rememberProgress"), "Progress persistence stays disabled at runtime.");
        if (phase == 0)
        {
            // The new member is accessed by reflection so this test still compiles before the runtime patch.
            Field(Views("plateViews").GetValue(0), "LockIcon");
            Load(Levels[stage]);
            phase = 1;
            nextTick = EditorApplication.timeSinceStartup + .4;
        }
        else if (phase == 1)
        {
            VerifyStage(Levels[stage]);
            if (stage < 5 && (Levels[stage] == 1 || Levels[stage] == 3 || Levels[stage] == 5)) Capture(Levels[stage]);
            stage++;
            phase = stage < Levels.Length ? 0 : 2;
            nextTick = EditorApplication.timeSinceStartup + .3;
        }
        else if (phase == 2)
        {
            ProbeRecovery();
            ProbeOptionalAndLegacy();
            Load(4); // Three plates / four flavors guarantees a genuinely unmatched tray candidate.
            beforeMove = ModelSignature();
            trayBefore = Model.Temp.Count(k => k.HasValue);
            var candidate = Model.Bubbles.SelectMany(b => b.Candies.Select((kind, index) => new { b.Id, Index = index, Kind = kind }))
                .First(c => !Model.Plates.Any(p => p.Active && p.Kind == c.Kind));
            moveBubble = candidate.Id;
            moveIndex = candidate.Index;
            moveKind = candidate.Kind;
            object bubble = ((IDictionary)Get(controller, "bubbles"))[moveBubble];
            var slots = (IDictionary)Get(controller, "candySlots");
            int originalSlot = (int)((IList)slots[moveBubble])[moveIndex];
            Button button = ((IList)Get(bubble, "Buttons"))[originalSlot] as Button;
            Check(button != null && button.gameObject.activeSelf, "Unmatched candy has its existing bound Button.");
            // Invoke the actual bound controller action, not model.TryTake or a fabricated tray state.
            button.onClick.Invoke();
            phase = 3;
            nextTick = EditorApplication.timeSinceStartup + .1;
        }
        else if (phase == 3)
        {
            if ((bool)Get(controller, "busy")) { nextTick = EditorApplication.timeSinceStartup + .1; return; }
            Check(Model.Temp.Count(k => k.HasValue) == trayBefore + 1 && Model.Temp.Any(k => k == moveKind) && Model.CanUndo,
                "Bound candy action routes one unmatched candy to the tray and creates Undo history.");
            VerifyStage(4);
            Check(((Button)Get(controller, "undoButton")).interactable, "Controller enabled Undo after the tray move.");
            ((Button)Get(controller, "undoButton")).onClick.Invoke();
            Check(ModelSignature() == beforeMove, "Bound Undo restores exact model candies, plates, tray, orders and counters.");
            VerifyStage(4);
            Pass("Actual controller candy binding, tray occupancy/art and exact Undo.");
            Load(1);
            VerifyStage(1);
            phase = 4;
            nextTick = EditorApplication.timeSinceStartup + .3; // Catch asynchronous controller logs before reporting success.
        }
        else Finish(true, "Levels 1/2/3/4/5/4/3/2/1; authored locks/dishes/layout/identities; reference recovery; array resize; optional texts; legacy rim/locks; tray and Undo.", true);
    }

    private static AuthoredSnapshot CaptureAuthored()
    {
        var result = new AuthoredSnapshot();
        foreach (RectTransform rect in CurrentRects())
            result.rects.Add(new RectState { path = PathOf(rect), anchorMin = rect.anchorMin, anchorMax = rect.anchorMax,
                pivot = rect.pivot, size = rect.sizeDelta, position = rect.anchoredPosition3D, scale = rect.localScale,
                rotation = rect.localRotation, sibling = rect.GetSiblingIndex() });
        Check(result.rects.Select(r => r.path).Distinct().Count() == result.rects.Count, "Authored plate/tray paths are unambiguous.");
        Array views = Views("plateViews");
        Check(views.Length == 4 && Views("tempViews").Length == 5, "Authored plate/tray capacities are 4/5.");
        for (int i = 0; i < 4; i++)
        {
            object view = views.GetValue(i);
            var rect = (RectTransform)Get(view, "Rect");
            var dish = (Image)Get(view, "Dish");
            Image icon = rect.GetComponentsInChildren<Image>(true).SingleOrDefault(image => image.name == "Image");
            Check(dish != null && dish.sprite != null, "Authored Dish exists for plate " + (i + 1));
            if (i < 2) Check(dish.sprite.name == "erasebg-transformed_" + (i + 2), "Customized plate 1/2 Dish sprite matches the fixture.");
            else
            {
                Check(Get(view, "Unlock") == null || Get(view, "Unlock") is Graphic,
                    "Plate " + (i + 1) + " accepts a missing legacy Unlock Text or the authored TMP Graphic.");
                Check(icon != null && icon.sprite != null && icon.sprite.name == "erasebg-transformed_4",
                    "Plate " + (i + 1) + " has its authored lock Image / erasebg-transformed_4.");
            }
            result.plates.Add(new PlateArt { path = PathOf(rect), dishPath = PathOf(dish.transform), dishSprite = SpriteKey(dish.sprite),
                lockPath = icon == null ? "" : PathOf(icon.transform), lockSprite = icon == null ? "" : SpriteKey(icon.sprite) });
        }
        return result;
    }

    private static void VerifyRetained()
    {
        var scene = controller.gameObject.scene;
        Check(controllerReferences.All(pair => ReferenceEquals(Get(controller, pair.Key), pair.Value)),
            "Authored controller roots, layers, labels and buttons retain their references.");
        Check(Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None).Count(x => x.gameObject.scene == scene) == 1 &&
            Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None).Count(x => x.gameObject.scene == scene) == 1 &&
            Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None).Count(x => x.gameObject.scene == scene) == 1,
            "Exactly one authored Canvas, Camera and EventSystem remains.");
        RectTransform[] current = CurrentRects();
        Check(current.Length == authored.rects.Count && current.Length == transformIds.Count, "No duplicate plate/tray hierarchy or view was created.");
        foreach (RectState saved in authored.rects)
        {
            RectTransform rect = current.SingleOrDefault(r => PathOf(r) == saved.path);
            Check(rect != null && rect.GetInstanceID() == transformIds[saved.path], "Authored transform identity retained: " + saved.path);
            Check(Near(rect.anchorMin, saved.anchorMin) && Near(rect.anchorMax, saved.anchorMax) && Near(rect.pivot, saved.pivot) &&
                Near(rect.sizeDelta, saved.size) && Vector3.Distance(rect.anchoredPosition3D, saved.position) < .01f &&
                Vector3.Distance(rect.localScale, saved.scale) < .0001f && Quaternion.Angle(rect.localRotation, saved.rotation) < .01f &&
                rect.GetSiblingIndex() == saved.sibling, "Authored local layout retained: " + saved.path);
        }
        Image[] images = CurrentImages();
        Check(images.Length == imageIds.Count && images.All(i => imageIds.TryGetValue(PathOf(i.transform), out int id) && id == i.GetInstanceID()),
            "Every authored plate/tray Image component retains its identity.");
        foreach (PlateArt art in authored.plates)
        {
            Check(SpriteKey(At(art.dishPath).GetComponent<Image>().sprite) == art.dishSprite, "Authored Dish sprite retained: " + art.path);
            if (art.lockPath.Length > 0)
                Check(SpriteKey(At(art.lockPath).GetComponent<Image>().sprite) == art.lockSprite, "Authored lock sprite retained: " + art.path);
        }
    }

    private static void VerifyStage(int level)
    {
        Check(Model.Level == level && Model.ActivePlates == (level < 3 ? 2 : level < 5 ? 3 : 4), "Expected level and plate unlock threshold: " + level);
        VerifyRetained();
        Array plates = Views("plateViews");
        Check(plates.Length == 4 && Views("tempViews").Length == 5, "View arrays retain model capacities.");
        for (int i = 0; i < 4; i++)
        {
            object view = plates.GetValue(i);
            CandySortModel.Plate plate = Model.Plates[i];
            PlateArt art = authored.plates[i];
            Check((Object)Get(view, "Rect") == At(art.path) && (Object)Get(view, "Dish") == At(art.dishPath).GetComponent<Image>(),
                "Plate view binds the authored root/Dish: " + (i + 1));
            var icon = (Image)Get(view, "LockIcon");
            Check(icon == (art.lockPath.Length == 0 ? null : At(art.lockPath).GetComponent<Image>()), "LockIcon binds the authored Image: " + (i + 1));
            if (icon != null) Visible(icon, !plate.Active, "LockIcon " + (i + 1));
            OptionalVisible(view, "Locked", !plate.Active && icon == null);
            // TMP captions may accompany an authored icon; both legacy/TMP captions must disappear on unlock.
            if (icon == null || plate.Active) OptionalVisible(view, "Unlock", !plate.Active);
            bool serving = plate.Active && plate.Kind.HasValue;
            var dish = (Image)Get(view, "Dish");
            Visible(dish, serving, "Dish " + (i + 1));
            bool legacy = dish.sprite == CandySortArt.Circle || dish.sprite == CandySortArt.Ring;
            var ring = (Image)Get(view, "Ring");
            if (ring != null) Visible(ring, serving && legacy, "Legacy rim " + (i + 1));
            Visible((Image)Get(view, "Sample"), serving, "Flavor sample " + (i + 1));
            Visible((Text)Get(view, "Count"), serving, "Count " + (i + 1));
            OptionalVisible(view, "Served", plate.Active && !plate.Kind.HasValue);
            var candies = (Image[])Get(view, "Candies");
            Check(candies.Length == 3 && candies.All(c => c != null), "Three required Candy Images bound for plate " + (i + 1));
            for (int c = 0; c < 3; c++)
            {
                Visible(candies[c], serving && c < plate.Count, "Plate candy " + i + "/" + c);
                if (serving && c < plate.Count) Check(candies[c].sprite == CandySortArt.Candy(plate.Kind.Value), "Plate candy art matches its model flavor.");
            }
            if (i >= 2 && Get(view, "Unlock") is Graphic unlock)
                Check(unlock.transform.IsChildOf((RectTransform)Get(view, "Rect")), "Recovered TMP/legacy Unlock stays on its authored plate.");
        }
        Array temps = Views("tempViews");
        for (int i = 0; i < 5; i++)
        {
            object view = temps.GetValue(i);
            var candy = (Button)Get(view, "Candy");
            Visible(candy, Model.Temp[i].HasValue, "Tray candy " + i);
            var background = ((RectTransform)Get(view, "Rect")).GetComponent<Image>();
            bool bakedSlot = background != null && background.sprite != null && background.sprite != CandySortArt.Rounded;
            OptionalVisible(view, "Empty", !Model.Temp[i].HasValue && !bakedSlot);
            if (bakedSlot)
            {
                Transform inset = ((RectTransform)Get(view, "Rect")).Find("Slot inset");
                if (inset != null) Check(!inset.gameObject.activeSelf, "Baked slot art does not duplicate the old inset.");
            }
            if (Model.Temp[i].HasValue) Check(candy.GetComponent<Image>().sprite == CandySortArt.Candy(Model.Temp[i].Value), "Occupied tray uses the model candy artwork.");
        }
        Pass("Stage " + level + ": locks, custom dishes, legacy rims, tray and authored identities/layout.");
    }

    private static void ProbeRecovery()
    {
        // Clear references only, never remove components/GameObjects or touch the serialized scene asset.
        Array plates = Views("plateViews"), temps = Views("tempViews");
        object p0 = plates.GetValue(0), p1 = plates.GetValue(1), t0 = temps.GetValue(0), t1 = temps.GetValue(1);
        Image[] candies = (Image[])Get(p0, "Candies");
        Object dish = (Object)Get(p0, "Dish"), candy = (Object)Get(t0, "Candy");
        Set(p0, "Dish", null);
        Set(p0, "Sample", null);
        Set(p0, "Count", null);
        Set(p0, "Candies", new[] { candies[0] });
        Set(p1, "Rect", null);
        Set(t0, "Candy", null);
        Set(t1, "Rect", null);
        Set(plates.GetValue(2), "LockIcon", null);
        Invoke("InitializeViews");
        Check(ReferenceEquals(Views("plateViews").GetValue(0), p0) && ReferenceEquals(Views("plateViews").GetValue(1), p1) &&
            ReferenceEquals(Views("tempViews").GetValue(0), t0) && ReferenceEquals(Views("tempViews").GetValue(1), t1), "Partial view records are repaired in place.");
        Check((Object)Get(p0, "Dish") == dish && (Object)Get(t0, "Candy") == candy, "Missing Dish and tray Candy recover the existing components.");
        Check(((Image[])Get(p0, "Candies")).SequenceEqual(candies), "Candy array resize preserves existing entries and recovers missing Images.");
        Load(1);
        VerifyStage(1);
        foreach (int extra in new[] { -1, 2 })
        {
            foreach (string name in new[] { "plateViews", "tempViews" })
            {
                Array current = Views(name);
                Array resized = Array.CreateInstance(current.GetType().GetElementType(), current.Length + extra);
                Array.Copy(current, resized, Math.Min(current.Length, resized.Length));
                Set(controller, name, resized);
                Invoke("InitializeViews");
                Array recovered = Views(name);
                Check(recovered.Length == current.Length, "Required capacity restored for " + name);
                for (int i = 0; i < Math.Min(current.Length, resized.Length); i++)
                    Check(ReferenceEquals(current.GetValue(i), recovered.GetValue(i)), "Array resize preserves current view entry: " + name + "[" + i + "]");
                Load(1);
                VerifyStage(1);
            }
        }
        // Lost view entries must rebind roots, not append newly generated plates/slots.
        Views("plateViews").SetValue(null, 2);
        Views("tempViews").SetValue(null, 3);
        Invoke("InitializeViews");
        Load(3);
        VerifyStage(3);
        Invoke("InitializeViews"); // Idempotence: bindings must not accumulate duplicate listeners/views.
        Load(5);
        VerifyStage(5);
        Pass("In-memory required-reference, partial-view, missing-entry and short/long array recovery without UI destruction.");
    }

    private static void ProbeOptionalAndLegacy()
    {
        var saved = new List<Tuple<object, string, object>>();
        try
        {
            foreach (object view in Views("plateViews"))
                foreach (string name in new[] { "Locked", "Unlock", "Served", "Ring", "LockIcon" })
                { saved.Add(Tuple.Create(view, name, Get(view, name))); Set(view, name, null); }
            foreach (object view in Views("tempViews"))
            { saved.Add(Tuple.Create(view, "Empty", Get(view, "Empty"))); Set(view, "Empty", null); }
            foreach (int level in new[] { 1, 3, 5, 1 })
            {
                Load(level); // Deliberately do not InitializeViews first: Render must tolerate null optional texts.
                VerifyRetained();
                Check(saved.All(item => Get(item.Item1, item.Item2) == null), "Null optional Text/Graphic/rim/icon references stay safe during Render.");
            }
        }
        finally
        {
            foreach (var item in saved) Set(item.Item1, item.Item2, item.Item3);
            Load(1);
        }
        object first = Views("plateViews").GetValue(0);
        var dish = (Image)Get(first, "Dish");
        Sprite customDish = dish.sprite;
        try
        {
            foreach (Sprite legacy in new[] { CandySortArt.Circle, CandySortArt.Ring })
            {
                dish.sprite = legacy;
                Invoke("RenderPlates");
                Visible((Image)Get(first, "Ring"), true, "Circle/Ring dish retains its old rim");
            }
        }
        finally { dish.sprite = customDish; Invoke("RenderPlates"); }
        // Plate 1 has both old texts and no LockIcon. Change only its runtime Active flag for this render-only probe.
        PropertyInfo active = typeof(CandySortModel.Plate).GetProperty("Active");
        CandySortModel.Plate plate = Model.Plates[0];
        try
        {
            Check(Get(first, "LockIcon") == null && Get(first, "Locked") != null && Get(first, "Unlock") != null, "Legacy text fallback fixture exists.");
            active.GetSetMethod(true).Invoke(plate, new object[] { false });
            Invoke("RenderPlates");
            OptionalVisible(first, "Locked", true);
            OptionalVisible(first, "Unlock", true);
        }
        finally { active.GetSetMethod(true).Invoke(plate, new object[] { true }); Invoke("RenderPlates"); }
        VerifyStage(1);
        Pass("Optional Locked/Unlock/Served/Empty nulls, Circle/Ring rim compatibility and old Locked/Unlock fallback.");
    }

    private static RectTransform[] CurrentRects()
    {
        // Safe-area fitting intentionally moves/scales the outer root; compare authored LOCAL plate/tray layout instead.
        return new[] { "Candy plates", "Temporary tray" }.SelectMany(name =>
        {
            RectTransform layer = controller.GetComponentsInChildren<RectTransform>(true).Single(r => r.name == name);
            return layer.GetComponentsInChildren<RectTransform>(true);
        }).ToArray();
    }
    private static Image[] CurrentImages() { return CurrentRects().SelectMany(r => r.GetComponents<Image>()).ToArray(); }
    private static string PathOf(Transform target) { return AnimationUtility.CalculateTransformPath(target, controller.transform); }
    private static Transform At(string path) { return controller.transform.Find(path); }
    private static string SpriteKey(Sprite sprite)
    {
        if (sprite == null) return "<null>";
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sprite, out string guid, out long id);
        return guid + ":" + id + ":" + sprite.name;
    }
    private static bool Near(Vector2 a, Vector2 b) { return Vector2.Distance(a, b) < .01f; }
    private static void Visible(Component component, bool expected, string name)
    {
        Check(component != null && component.gameObject.activeSelf == expected && (!expected || component.gameObject.activeInHierarchy),
            name + " visibility should be " + expected);
        if (expected && component is Graphic graphic) Check(graphic.enabled && graphic.color.a > 0, name + " must be visibly enabled.");
    }
    private static void OptionalVisible(object view, string name, bool expected)
    { var component = (Component)Get(view, name); if (component != null) Visible(component, expected, name); }
    private static CandySortGameController FindController()
    {
        var all = Object.FindObjectsByType<CandySortGameController>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(c => c.gameObject.scene == SceneManager.GetActiveScene()).ToArray();
        Check(all.Length == 1, "Exactly one authored CandySortGameController is required.");
        return all[0];
    }
    private static CandySortModel Model { get { return (CandySortModel)Get(controller, "model"); } }
    private static Array Views(string name) { return (Array)Get(controller, name); }
    private static FieldInfo Field(object target, string name)
    {
        Check(target != null, "Missing reflection target for " + name);
        FieldInfo field = target.GetType().GetField(name, Fields);
        Check(field != null, "Required regression API field not found: " + target.GetType().Name + "." + name);
        return field;
    }
    private static object Get(object target, string name) { return Field(target, name).GetValue(target); }
    private static void Set(object target, string name, object value) { Field(target, name).SetValue(target, value); }
    private static void Invoke(string name, params object[] args)
    {
        MethodInfo method = typeof(CandySortGameController).GetMethod(name, Fields);
        Check(method != null, "Required regression API method not found: " + name);
        method.Invoke(controller, args);
    }
    private static void Load(int level) { Invoke("LoadLevel", level); }
    private static string ModelSignature()
    {
        return string.Join("|", new[] { Model.Level.ToString(), Model.CompletedOrders.ToString(), Model.SortedCandies.ToString(),
            Model.CanUndo.ToString(), string.Join(",", Model.Temp.Select(k => k.HasValue ? ((int)k.Value).ToString() : "-")),
            string.Join(",", Model.PendingOrders.Select(k => (int)k)),
            string.Join(";", Model.Plates.Select(p => p.Active + ":" + p.Kind + ":" + p.Count)),
            string.Join(";", Model.Bubbles.Select(b => b.Id + ":" + b.Lane + ":" + string.Join(",", b.Candies.Select(k => (int)k)))) });
    }
    private static void Capture(int level)
    {
        screenshot = Path.GetFullPath(OutputPath + "/edited-ui-stage-" + level + ".png");
        if (File.Exists(screenshot)) File.Delete(screenshot);
        Canvas.ForceUpdateCanvases();
        ScreenCapture.CaptureScreenshot(screenshot);
        screenshotDeadline = EditorApplication.timeSinceStartup + 10;
        EditorApplication.QueuePlayerLoopUpdate();
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Pass(string message) { File.AppendAllText(ReportPath, "PASS: " + message + "\n"); }
    private static void Finish(bool passed, string message, bool stopPlay)
    {
        try
        {
            Directory.CreateDirectory(OutputPath);
            File.AppendAllText(ReportPath, (passed ? "PASS: " : "FAIL: ") + message + "\n");
            Debug.Log("Candy Sort edited UI smoke " + (passed ? "PASSED" : "FAILED") + ". Report: " + ReportPath);
        }
        finally
        {
            SessionState.SetString(StateKey, stopPlay ? "stopping" : "");
            if (stopPlay) EditorApplication.isPlaying = false;
            else ResetSession();
        }
    }
    private static void ResetSession()
    {
        SessionState.EraseString(StateKey);
        SessionState.EraseString(SnapshotKey);
        SessionState.EraseString(ErrorKey);
        SessionState.EraseFloat(DeadlineKey);
        controller = null;
        authored = null;
        transformIds = imageIds = null;
        controllerReferences = null;
        screenshot = null;
    }
}
