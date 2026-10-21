// Explicit, editor-only regression probe for the user's authored Candy Sort UI.
// Never saves a scene, changes PlayerPrefs, destroys authored UI, or runs on ordinary imports/Play.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CandySort;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class CandySortEditedUiSmoke
{
    private const string LegacyScenePath = "Assets/Scenes/CandySortGame.unity";
    private const string GameplayScenePath = "Assets/Scenes/Gameplay.unity";
    private const string TargetSceneKey = "CandySortEditedUiSmoke.TargetScene";
    private const string SceneSetupKey = "CandySortEditedUiSmoke.SceneSetup";
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
        "helpButton", "restartButton", "undoButton", "magnetButton", "shuffleButton",
        "magnetCountLabel", "undoCountLabel", "shuffleCountLabel"
    };
    private static CandySortGameController controller;
    private static AuthoredSnapshot authored;
    private static Dictionary<string, int> transformIds, imageIds;
    private static Dictionary<string, object> controllerReferences;
    private static int stage, phase;
    private static double nextTick, screenshotDeadline;
    private static string screenshot;
    private static string beforeMove, boardBefore;
    private static int moveBubble, moveIndex, trayBefore;
    private static CandyKind moveKind;
    private static int boosterStep, stockBefore;
    private static string boosterModelBefore, boosterBoardBefore;
    private static CandySortModel expectedBoosterModel;
    private static bool economyInjected, economyIsolated, editModeConfirmed;
    private static object inventory;
    private static Dictionary<string, int> inventoryPrefs;
    private static long wallet, rewardWalletBefore;
    private static int walletWrites, inventorySaves;
    private static Action pendingWalletSuccess;
    private static bool deferWalletChange;
    private static readonly string[] BoosterNames = { "Magnet", "Undo", "Shuffle" };
    private static readonly int[] UnlockLevels = { 3, 5, 7 };
    private static bool IsGameplay { get { return SessionState.GetString(TargetSceneKey, "") == GameplayScenePath; } }

    [Serializable] private sealed class SavedSceneSetup
    {
        public List<SavedScene> scenes = new List<SavedScene>();
    }

    [Serializable] private sealed class SavedScene
    {
        public string path;
        public bool loaded, active;
    }

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
        InstallStartHook(false);
        EditorApplication.delayCall += RecoverStoppedSession;
    }

    private static void RecoverStoppedSession()
    {
        // A reimport can happen after EnteredEditMode was delivered to the old domain.
        // Require two deferred idle observations, and recover only sessions with no
        // scene setup to restore: this path never calls scene APIs during a transition.
        if (SessionState.GetString(StateKey, "") == "stopping" &&
            !Application.isPlaying && !EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode &&
            SessionState.GetString(SceneSetupKey, "").Length == 0)
            EditorApplication.delayCall += ConfirmStoppedSession;
    }

    private static void ConfirmStoppedSession()
    {
        if (SessionState.GetString(StateKey, "") != "stopping" ||
            EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode ||
            SessionState.GetString(SceneSetupKey, "").Length != 0) return;
        editModeConfirmed = true;
        ResetSession();
    }

    private static void InstallStartHook(bool required)
    {
        // Editor assemblies do not reliably receive RuntimeInitializeOnLoadMethod.
        // This editor-only runtime hook is called as Start's first statement, before
        // InitializeViews or RestoreProgress can grant persistent welcome stock.
        FieldInfo hook = typeof(CandySortGameController).GetField("BeforeEditorStart",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (required) Check(hook != null, "The controller's editor-only pre-Start isolation hook is available.");
        if (hook != null)
        {
            var callbacks = (Action<CandySortGameController>)hook.GetValue(null);
            callbacks -= PrepareControllerStart;
            callbacks += PrepareControllerStart;
            hook.SetValue(null, callbacks);
        }
    }

    private static void PrepareControllerStart(CandySortGameController runtimeController)
    {
        if (SessionState.GetString(StateKey, "") != "entering" ||
            runtimeController.gameObject.scene.path != SessionState.GetString(TargetSceneKey, "")) return;
        try
        {
            Check(Application.isPlaying && !runtimeController.IsInitialized,
                "Economy isolation is installed on the runtime copy before controller Start initialization.");
            controller = runtimeController;
            Set(controller, "persistEconomy", false);
            Set(controller, "rememberProgress", false);
            Set(controller, "startingLevel", 1);
            economyInjected = false;
            wallet = 100;
            walletWrites = inventorySaves = 0;
            deferWalletChange = false;
            pendingWalletSuccess = null;
            InjectWallet();
            economyIsolated = true;
        }
        catch (Exception exception)
        {
            Finish(false, exception.ToString(), true);
            // Abort this Start rather than allowing it to fall through into persistent
            // initialization if the hook cannot establish isolation.
            throw;
        }
    }

    [MenuItem("Candy Sort/Run Edited UI Regression Smoke Test")]
    public static void RequestSmoke()
    {
        // Also callable through -executeMethod CandySortEditedUiSmoke.RequestSmoke.
        WriteRequest("run");
    }

    [MenuItem("Candy Sort/Run Gameplay UI Regression Smoke Test")]
    public static void RequestGameplaySmoke()
    {
        WriteRequest("Gameplay");
    }

    private static void WriteRequest(string target)
    {
        Check(!EditorApplication.isPlayingOrWillChangePlaymode && SessionState.GetString(StateKey, "") == "",
            "Stop Play mode / the active smoke before requesting this test.");
        Directory.CreateDirectory("Temp");
        File.WriteAllText(RequestPath, target);
    }

    private static bool ScenesAreClean()
    {
        return !Enumerable.Range(0, SceneManager.sceneCount).Any(i => SceneManager.GetSceneAt(i).isDirty);
    }

    private static void Tick()
    {
        string state = SessionState.GetString(StateKey, "");
        try
        {
            if (state == "")
            {
                if (EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(RequestPath)) return;
                string request = File.ReadAllText(RequestPath).Trim();
                File.Delete(RequestPath); // One-shot; a static constructor/import is never a request.
                Directory.CreateDirectory(OutputPath);
                if (request == "RestoreSplash")
                {
                    Check(!Application.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode && ScenesAreClean(),
                        "Stop Play mode and keep scenes clean before restoring Splash; no scene is saved or discarded.");
                    EditorSceneManager.OpenScene("Assets/Scenes/Splash.unity", OpenSceneMode.Single);
                    Pass("Restored the original Splash scene without saving any scene.");
                    return;
                }
                File.WriteAllText(ReportPath, "Candy Sort edited UI: actual Play Mode regression smoke\n");
                Check(request == "run" || request == "Gameplay", "Unknown smoke request: " + request);
                Check(!EditorApplication.isPlayingOrWillChangePlaymode, "Stop Play mode before requesting this test.");
                editModeConfirmed = true; // This request has not initiated any Play transition yet.
                InstallStartHook(true); // Fail before entering Play if isolation cannot be installed.
                Check(!EditorSettings.enterPlayModeOptionsEnabled ||
                    (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableSceneReload) == 0,
                    "This smoke requires scene reload so its pre-Start hook runs on a fresh runtime copy.");
                Check(ScenesAreClean(), "Save or discard pending scene edits before requesting this test (the test never saves).");
                Check(!SessionState.GetBool("CandySortSmokeActive", false) && !File.Exists("Temp/CandySortSmoke.request"),
                    "Do not run the existing CandySortEditor smoke at the same time.");
                if (request == "Gameplay")
                {
                    var setup = new SavedSceneSetup();
                    foreach (SceneSetup scene in EditorSceneManager.GetSceneManagerSetup())
                    {
                        Check(!string.IsNullOrEmpty(scene.path), "Save every open scene before explicitly opening Gameplay for the smoke.");
                        setup.scenes.Add(new SavedScene { path = scene.path, loaded = scene.isLoaded, active = scene.isActive });
                    }
                    SessionState.SetString(SceneSetupKey, JsonUtility.ToJson(setup));
                    EditorSceneManager.OpenScene(GameplayScenePath, OpenSceneMode.Single);
                }
                string target = SceneManager.GetActiveScene().path;
                Check(target == LegacyScenePath || target == GameplayScenePath,
                    "Open the saved CandySortGame or Gameplay scene first (or explicitly request Gameplay).");
                SessionState.SetString(TargetSceneKey, target);
                Pass("Requested saved target: " + target);
                controller = FindController();
                VerifyTextComponents();
                authored = CaptureAuthored();
                SessionState.SetString(SnapshotKey, JsonUtility.ToJson(authored));
                SessionState.SetString(ErrorKey, "");
                SessionState.SetFloat(DeadlineKey, (float)EditorApplication.timeSinceStartup + 150);
                economyIsolated = false;
                editModeConfirmed = false;
                SessionState.SetString(StateKey, "entering");
                // No serialized edit-mode settings are touched. The pre-Start hook changes only runtime copies.
                EditorWindow.GetWindow(typeof(Editor).Assembly.GetType("UnityEditor.GameView")).Focus();
                EditorApplication.isPlaying = true;
                return;
            }
            if (state == "stopping")
            {
                if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
                if (!Application.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isPlaying)
                {
                    editModeConfirmed = true;
                    EditorApplication.delayCall -= ResetSession;
                    EditorApplication.delayCall += ResetSession;
                }
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
                Check(SceneManager.GetActiveScene().path == SessionState.GetString(TargetSceneKey, ""),
                    "The requested scene must remain active when entering Play mode.");
                var runtimeController = FindController();
                Check(economyIsolated && controller == runtimeController && !(bool)Get(runtimeController, "persistEconomy") &&
                    !(bool)Get(runtimeController, "rememberProgress"),
                    "The pre-Start hook isolated this runtime controller before EnteredPlayMode.");
                // Start may already have run. EnteredPlayMode verifies the earlier hook;
                // it must never try to install isolation after welcome grants happened.
                controller = runtimeController;
                authored = JsonUtility.FromJson<AuthoredSnapshot>(SessionState.GetString(SnapshotKey, ""));
                transformIds = CurrentRects().ToDictionary(PathOf, r => r.GetInstanceID());
                imageIds = CurrentImages().ToDictionary(i => PathOf(i.transform), i => i.GetInstanceID());
                // Runtime count/icon fallbacks may be appended when the authored scene
                // has no binding. Existing authored fields, including counts, must not move.
                controllerReferences = AuthoredReferences.Where(name => !name.EndsWith("CountLabel") || Get(controller, name) != null)
                    .ToDictionary(name => name, name => Get(controller, name));
                stage = phase = boosterStep = 0;
                screenshot = null;
                nextTick = EditorApplication.timeSinceStartup + .5;
                SessionState.SetString(StateKey, "running");
                VerifyRetained();
            }
            else if (change == PlayModeStateChange.EnteredEditMode)
            {
                if (state != "stopping") File.AppendAllText(ReportPath, "FAIL: Play mode exited before completion.\n");
                editModeConfirmed = true;
                EditorApplication.delayCall -= ResetSession;
                EditorApplication.delayCall += ResetSession;
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
        if (!controller.IsInitialized) { nextTick = EditorApplication.timeSinceStartup + .1; return; }
        Check(Get(controller, "model") != null, "Controller Start initialized its model.");
        Check(!(bool)Get(controller, "rememberProgress") && !(bool)Get(controller, "persistEconomy"),
            "Progress and economy persistence stay disabled on the runtime copy.");
        if (!economyInjected)
        {
            // Start has finished, but no test LoadLevel/action has run yet. Neither this
            // inventory nor the coin delegates can reach PlayerPrefs or ResourceService.
            InjectInventory();
            InjectWallet();
            foreach (string name in new[] { "magnetCountLabel", "undoCountLabel", "shuffleCountLabel" })
                if (!controllerReferences.ContainsKey(name) && Get(controller, name) != null) controllerReferences[name] = Get(controller, name);
            economyInjected = true;
            Pass("After-Start in-memory inventory and fake coin wallet installed before test LoadLevel; runtime fallbacks tracked independently of authored plate/tray geometry.");
        }
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
            ProbeDialogs();
            ProbeRecovery();
            ProbeOptionalAndLegacy();
            Load(5); // Undo unlocks here; unmatched kinds are guaranteed among nine flavors.
            beforeMove = ModelSignature();
            boardBefore = BoardSignature();
            trayBefore = Model.Temp.Count(k => k.HasValue);
            var candidate = Model.Bubbles.SelectMany(b => b.Candies.Select((kind, index) => new { b.Id, Index = index, Kind = kind }))
                .First(c => !Model.Plates.Any(p => p.Active && p.Kind == c.Kind));
            moveBubble = candidate.Id;
            moveIndex = candidate.Index;
            moveKind = candidate.Kind;
            Button button = CandyButton(moveBubble, moveIndex);
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
            VerifyStage(5);
            Check(((Button)Get(controller, "undoButton")).interactable, "Controller enabled unlocked Undo after the tray move.");
            stockBefore = Stock("Undo");
            long coins = wallet;
            ((Button)Get(controller, "undoButton")).onClick.Invoke();
            Check(ModelSignature() == beforeMove && BoardSignature() == boardBefore,
                "Bound Undo restores exact model candies, plates, tray, orders, counters, layout and slot mapping.");
            Check(Stock("Undo") == stockBefore - 1 && wallet == coins && walletWrites == 0,
                "Successful tray Undo consumes one free use without touching the fake wallet.");
            VerifyStage(5);
            Pass("Actual controller candy binding, tray occupancy/art and finite-stock exact Undo.");
            Load(5);
            VerifyStage(5);
            phase = 4;
            nextTick = EditorApplication.timeSinceStartup + .3; // Catch asynchronous controller logs before reporting success.
        }
        else if (phase == 4)
        {
            if ((bool)Get(controller, "busy")) { nextTick = EditorApplication.timeSinceStartup + .1; return; }
            var modal = (RectTransform)Get(controller, "modalLayer");
            trayBefore = Model.Temp.Count(k => k.HasValue);
            Check(trayBefore < 5 && modal.childCount == 0, "Up to four occupied slots do not show a loss dialog.");
            Check(PlayableCandy(true) != null, "A visible matching candy remains playable before filling the tray.");
            Button button = PlayableCandy(false);
            Check(button != null, "A visible unmatched candy can fill the next temp slot.");
            beforeMove = ModelSignature();
            boardBefore = BoardSignature();
            button.onClick.Invoke();
            Check((bool)Get(controller, "busy") && modal.childCount == 0,
                "The loss dialog waits for the candy transfer to finish.");
            phase = 5;
            nextTick = EditorApplication.timeSinceStartup + .1;
        }
        else if (phase == 5)
        {
            if ((bool)Get(controller, "busy")) { nextTick = EditorApplication.timeSinceStartup + .1; return; }
            Check(Model.Temp.Count(k => k.HasValue) == trayBefore + 1, "Each bound unmatched candy fills exactly one temp slot.");
            VerifyStage(5);
            if (trayBefore < 4) phase = 4;
            else
            {
                Check(Model.Temp.All(k => k.HasValue) && !Model.IsBlocked,
                    "Five slots are full even though the model still has a direct matching move.");
                VerifyDialog("Result card", "KHAY TẠM ĐÃ ĐẦY");
                Capture("edited-ui-temp-full");
                phase = 6;
            }
            nextTick = EditorApplication.timeSinceStartup + .3;
        }
        else if (phase == 6)
        {
            var modal = (RectTransform)Get(controller, "modalLayer");
            var undo = modal.GetComponentsInChildren<Button>().Single(button => button.name == "Button HOÀN TÁC");
            stockBefore = Stock("Undo");
            long coins = wallet;
            undo.onClick.Invoke();
            Check(Stock("Undo") == stockBefore - 1 && wallet == coins,
                "Loss-dialog Undo consumes one free use, without spending coins.");
            // Clear disables the modal immediately; Unity destroys its objects at the end of the frame.
            Check(!modal.GetComponentsInChildren<Button>().Any() && Model.Temp.Count(k => k.HasValue) == 4,
                "Loss-dialog Undo clears the modal and restores four occupied slots.");
            Check(ModelSignature() == beforeMove && BoardSignature() == boardBefore,
                "Loss-dialog Undo restores the exact pre-fifth-candy model, layout and candy slots.");
            Check(PlayableCandy(true) != null, "Matching candy is playable again after loss-dialog Undo.");
            VerifyStage(5);
            Pass("Fifth temp slot causes loss after transfer despite a remaining matching move; finite-stock dialog Undo restores exact state.");
            Load(1);
            phase = 7;
            nextTick = EditorApplication.timeSinceStartup + .3;
        }
        else if (phase == 7)
        {
            if (ProbeBoosters())
            {
                Load(1);
                rewardWalletBefore = wallet;
                phase = 8;
                nextTick = EditorApplication.timeSinceStartup + .3;
            }
        }
        else if (phase == 8)
        {
            if ((bool)Get(controller, "busy")) { nextTick = EditorApplication.timeSinceStartup + .1; return; }
            if (!Model.IsWon)
            {
                // Win honestly through bound candy actions. Do not fabricate counters or
                // invoke CompleteLevel on a nonwinning model merely to probe its dialog.
                var candidate = Model.Bubbles.SelectMany(b => b.Candies.Select((kind, index) => new { b.Id, Index = index, Kind = kind }))
                    .FirstOrDefault(c => Model.Plates.Any(p => p.Active && p.Kind == c.Kind));
                Check(candidate != null, "Valid reward fixture has a directly matching board move.");
                CandyButton(candidate.Id, candidate.Index).onClick.Invoke();
                nextTick = EditorApplication.timeSinceStartup + .1;
                return;
            }
            Check(wallet == rewardWalletBefore + 40, "A valid completed level awards exactly 40 fake coins.");
            int writes = walletWrites;
            Invoke("CompleteLevel");
            Invoke("CompleteLevel");
            Check(Model.IsWon && wallet == rewardWalletBefore + 40 && walletWrites == writes,
                "Duplicate completion callbacks on the same won level cannot award twice.");
            VerifyDialog("Result card", "THẬT NGỌT NGÀO!");
            object claimedReward = Get(controller, "winReward");
            Load(1);
            Check(!ReferenceEquals(claimedReward, Get(controller, "winReward")) && wallet == rewardWalletBefore + 40 && walletWrites == writes,
                "LoadLevel creates a fresh reward attempt without awarding a nonwinning model.");
            Pass("Actual bound moves win level 1; +40 reward goes only to fake wallet, once per LoadLevel.");
            phase = 9;
            nextTick = EditorApplication.timeSinceStartup + .3;
        }
        else Finish(true, "Target " + SessionState.GetString(TargetSceneKey, "") +
            "; levels 1/2/3/4/5/4/3/2/1; authored locks/dishes/layout/identities and booster references; reference recovery; array resize; optional captions; legacy rim/locks; tray-only exact Undo; fifth-slot loss and dialog Undo; unlocks 3/5/7; one-time grants of two; free stock consumption; depleted purchase cancel/insufficient/confirm/in-flight guard; loss-dialog paid Undo; boosters invalidate rather than create Undo; rendered candy slots; valid win +40 and duplicate reward guard; help/loss/win/final-win TMP dialogs; no persistent economy/progress writes; text-fit warnings reported separately.", true);
    }

    // An independent rules model is only an oracle: controller actions own all live
    // board/history mutations. The inventory/wallet are deliberately disposable fakes.
    private static bool ProbeBoosters()
    {
        if ((bool)Get(controller, "busy")) { nextTick = EditorApplication.timeSinceStartup + .1; return false; }
        switch (boosterStep)
        {
            case 0:
                ProbeInventoryThresholds();
                ProbeControllerLocks();
                InjectInventory();
                Load(4);
                expectedBoosterModel = new CandySortModel(4);
                TakeOracleCandy(false);
                break;
            case 1:
                Check(Model.CanUndo && !Unlocked("Undo"), "Locked Undo fixture has a real tray pickup before level 5.");
                string lockedModel = ModelSignature(), lockedBoard = BoardSignature(), lockedStock = StockSignature();
                Request("Undo");
                Invoke("RequestBooster", Booster("Undo"));
                Invoke("ExecuteBooster", Booster("Undo"));
                Check(ModelSignature() == lockedModel && BoardSignature() == lockedBoard && StockSignature() == lockedStock && !ModalActive && walletWrites == 0,
                    "A genuinely undoable tray pickup cannot bypass Undo's level-5 lock or open a purchase.");
                VerifyStage(4);
                ResetBoosterFixture();
                TakeOracleCandy(true);
                break;
            case 2:
                Check(!Model.CanUndo && BoardHistoryCount() == 0, "A successful plate pickup is not undoable.");
                VerifyUnavailableUndo();
                ResetBoosterFixture();
                TakeOracleCandy(false);
                break;
            case 3:
                Check(Model.CanUndo && BoardHistoryCount() == 1, "Unmatched pickup creates tray-only model/board history.");
                boosterBoardBefore = BoardSignature();
                BeginBooster(false);
                break;
            case 4:
                VerifyBoosterResult("Shuffle");
                Check(BoardSignature() == boosterBoardBefore, "Shuffle preserves bubble layout and original candy slots.");
                VerifyUnavailableUndo();
                ResetBoosterFixture();
                TakeOracleCandy(false);
                break;
            case 5:
                Check(Model.CanUndo, "Magnet fixture begins with an undoable tray pickup.");
                BeginBooster(true);
                break;
            case 6:
                VerifyBoosterResult("Magnet");
                VerifyUnavailableUndo();
                Check(Stock("Magnet") == 1, "First Magnet consumes exactly one of two free uses.");
                BeginBooster(true);
                break;
            case 7:
                VerifyBoosterResult("Second free Magnet");
                Check(Stock("Magnet") == 0 && walletWrites == 0, "Second Magnet exhausts free stock without coin writes.");
                boosterModelBefore = ModelSignature();
                boosterBoardBefore = BoardSignature();
                Request("Magnet");
                VerifyPurchase();
                PurchaseButton("HỦY").onClick.Invoke();
                VerifyNoPurchaseMutation("Cancel", 100);
                wallet = 9;
                Request("Magnet");
                VerifyPurchase();
                PurchaseButton("MUA & DÙNG · 10 VÀNG").onClick.Invoke();
                VerifyInsufficientPurchase(boosterModelBefore, 0);
                PurchaseButton("HỦY").onClick.Invoke();
                VerifyNoPurchaseMutation("Insufficient funds then cancel", 9);
                wallet = 10;
                Request("Magnet");
                VerifyPurchase();
                deferWalletChange = true;
                PurchaseButton("MUA & DÙNG · 10 VÀNG").onClick.Invoke();
                Check((bool)Get(controller, "purchaseInFlight") && pendingWalletSuccess != null && wallet == 0 && walletWrites == 1,
                    "Confirm debits exactly ten fake coins and waits for the wallet callback.");
                Invoke("ConfirmBoosterPurchase");
                Invoke("CancelBoosterPurchase");
                Invoke("RequestBooster", Booster("Shuffle"));
                Check(walletWrites == 1 && Stock("Magnet") == 0 && ModelSignature() == boosterModelBefore,
                    "Repeated confirm/cancel/request during purchase cannot debit, grant or execute again.");
                Check(expectedBoosterModel.TryMagnet(), "Paid Magnet oracle accepts the action.");
                Action success = pendingWalletSuccess;
                pendingWalletSuccess = null;
                deferWalletChange = false;
                success();
                Check((bool)Get(controller, "busy") && Stock("Magnet") == 0 && ModelSignature() == ModelSignature(expectedBoosterModel),
                    "Successful purchase grants one use and consumes that use for exactly one Magnet.");
                success(); // Resource callbacks can be delivered twice; settlement must be idempotent.
                Check(Stock("Magnet") == 0 && wallet == 0 && walletWrites == 1 && ModelSignature() == ModelSignature(expectedBoosterModel),
                    "Duplicate wallet success cannot grant, debit or execute a second purchased use.");
                VerifyBusyGuards();
                break;
            case 8:
                VerifyBoosterResult("Purchased Magnet");
                Check(wallet == 0 && walletWrites == 1, "Paid use costs ten once, with no recurring or booster reward charge.");
                // Reload cannot replenish exhausted free grants; an earlier level stays unlocked.
                Load(1);
                Check(Stock("Magnet") == 0 && Unlocked("Magnet"), "Reloading an earlier level never refills or re-locks stock.");
                ResetBoosterFixture();
                while (Stock("Undo") > 0) Check((bool)InventoryCall(inventory, "TryConsume", Booster("Undo")), "Exhaust fake Undo stock.");
                break;
            case 9:
                if (Model.Temp.Count(k => k.HasValue) < 5)
                {
                    beforeMove = ModelSignature();
                    boardBefore = BoardSignature();
                    TakeOracleCandy(false);
                    nextTick = EditorApplication.timeSinceStartup + .1;
                    return false;
                }
                VerifyDialog("Result card", "KHAY TẠM ĐÃ ĐẦY");
                ModelButton("HOÀN TÁC").onClick.Invoke();
                VerifyPurchase();
                string fullTray = ModelSignature();
                PurchaseButton("HỦY").onClick.Invoke();
                Check(ModelSignature() == fullTray && Stock("Undo") == 0 && walletWrites == 1,
                    "Cancelling paid loss-dialog Undo leaves the full tray and stock untouched.");
                VerifyDialog("Result card", "KHAY TẠM ĐÃ ĐẦY");
                wallet = 9;
                ModelButton("HOÀN TÁC").onClick.Invoke();
                PurchaseButton("MUA & DÙNG · 10 VÀNG").onClick.Invoke();
                VerifyInsufficientPurchase(fullTray, 1);
                Check(Stock("Undo") == 0, "Insufficient paid loss Undo grants no stock.");
                PurchaseButton("HỦY").onClick.Invoke();
                VerifyDialog("Result card", "KHAY TẠM ĐÃ ĐẦY");
                wallet = 10;
                ModelButton("HOÀN TÁC").onClick.Invoke();
                PurchaseButton("MUA & DÙNG · 10 VÀNG").onClick.Invoke();
                Check(ModelSignature() == beforeMove && BoardSignature() == boardBefore && Model.Temp.Count(k => k.HasValue) == 4,
                    "Paid loss-dialog Undo restores exactly the pre-fifth-pickup model, slots and layout.");
                Check(Stock("Undo") == 0 && wallet == 0 && walletWrites == 2 && !ModalActive,
                    "Paid Undo uses its single purchased use, debits ten and dismisses the loss dialog.");
                VerifyStage(7);
                Pass("Finite grants, locked actions, tray-only Undo, free consumption, purchase cancel/insufficient/confirm/in-flight and paid loss Undo.");
                return true;
        }
        boosterStep++;
        nextTick = EditorApplication.timeSinceStartup + .7;
        return false;
    }

    private static void ResetBoosterFixture()
    {
        InjectInventory();
        Load(7);
        VerifyStage(7);
        expectedBoosterModel = new CandySortModel(7);
        Check(ModelSignature() == ModelSignature(expectedBoosterModel), "Booster fixture matches the independent canonical model.");
    }

    private static void TakeOracleCandy(bool matching)
    {
        var candidate = Model.Bubbles.SelectMany(b => b.Candies.Select((kind, index) => new { b.Id, Index = index, Kind = kind }))
            .First(c => Model.Plates.Any(p => p.Active && p.Kind == c.Kind) == matching);
        Check(expectedBoosterModel.TryTake(candidate.Id, candidate.Index), "Independent oracle accepts the bound candy pickup.");
        CandyButton(candidate.Id, candidate.Index).onClick.Invoke();
        Check((bool)Get(controller, "busy") && ModelSignature() == ModelSignature(expectedBoosterModel),
            "Bound candy pickup applies the exact independent transaction.");
    }

    private static void BeginBooster(bool magnet)
    {
        string name = magnet ? "Magnet" : "Shuffle";
        var button = (Button)Get(controller, name.ToLowerInvariant() + "Button");
        Check(button == null || (button.isActiveAndEnabled && button.IsInteractable()), "Assigned booster is visibly enabled: " + name);
        Check(magnet ? expectedBoosterModel.TryMagnet() : expectedBoosterModel.TryShuffle(), "Independent booster oracle accepts " + name);
        int count = Stock(name), writes = walletWrites;
        long coins = wallet;
        Request(name);
        Check((bool)Get(controller, "busy") && ModelSignature() == ModelSignature(expectedBoosterModel),
            "Booster applies exactly one model transaction: " + name);
        Check(Stock(name) == count - 1 && wallet == coins && walletWrites == writes,
            "Successful free booster consumes one stock use and no coins: " + name);
        VerifyBusyGuards();
    }

    private static void VerifyBusyGuards()
    {
        string model = ModelSignature(), board = BoardSignature(), stock = StockSignature();
        int history = BoardHistoryCount(), writes = walletWrites;
        foreach (string name in BoosterNames)
        {
            var button = (Button)Get(controller, name.ToLowerInvariant() + "Button");
            if (button != null) Check(!button.interactable, "Busy controller disables " + name);
            Request(name); // UnityEvent.Invoke deliberately bypasses Selectable filtering.
            Invoke("RequestBooster", Booster(name));
            Invoke("ExecuteBooster", Booster(name));
            Check((bool)Get(controller, "busy") && ModelSignature() == model && BoardSignature() == board && BoardHistoryCount() == history &&
                StockSignature() == stock && walletWrites == writes, "Busy reentry changes neither model, board, stock nor wallet: " + name);
        }
        var bubble = Model.Bubbles.First(b => b.Candies.Count > 0);
        CandyButton(bubble.Id, 0).onClick.Invoke();
        Check(ModelSignature() == model && BoardSignature() == board && BoardHistoryCount() == history && StockSignature() == stock,
            "A bound candy action cannot interleave with the booster transaction.");
    }

    private static void VerifyBoosterResult(string action)
    {
        Check(!(bool)Get(controller, "busy"), action + " animation completed.");
        Check(ModelSignature() == ModelSignature(expectedBoosterModel), action + " matches exact oracle state after animation.");
        Check(!Model.CanUndo && BoardHistoryCount() == 0 && ModelHistoryCount(Model) == 0,
            action + " invalidates prior tray pickups and creates no Undo entry of its own.");
        Check(!ModalActive, action + " leaves no unexpected modal.");
        VerifyStage(7);
    }

    private static void VerifyUnavailableUndo()
    {
        string model = ModelSignature(), board = BoardSignature(), stock = StockSignature();
        int writes = walletWrites;
        Request("Undo");
        Invoke("ExecuteBooster", Booster("Undo"));
        Check(!Model.CanUndo && ModelSignature() == model && BoardSignature() == board && StockSignature() == stock && walletWrites == writes && !ModalActive,
            "Undo cannot reverse plate moves/boosters, spend stock/coins or open a purchase without a tray pickup.");
    }

    private static void VerifyPurchase()
    {
        VerifyDialog("Booster purchase", "MUA THÊM LƯỢT");
        Check(PurchaseButton("MUA & DÙNG · 10 VÀNG") != null && PurchaseButton("HỦY") != null, "Purchase has explicit confirm and cancel actions.");
        Check(ModelSignature() == boosterModelBefore || Model.Temp.All(k => k.HasValue), "Opening purchase does not execute its booster.");
    }

    private static void VerifyNoPurchaseMutation(string context, long coins)
    {
        Check(ModelSignature() == boosterModelBefore && BoardSignature() == boosterBoardBefore && Stock("Magnet") == 0 &&
            wallet == coins && walletWrites == 0 && !ModalActive && Get(controller, "pendingBooster") == null,
            context + " closes purchase without changing the model, board, stock or coins.");
    }

    private static void VerifyInsufficientPurchase(string expectedModel, int writes)
    {
        VerifyPurchase();
        var feedback = (TextMeshProUGUI)Get(controller, "purchaseMessage");
        Check(ModelSignature() == expectedModel && wallet == 9 && walletWrites == writes && !(bool)Get(controller, "purchaseInFlight") &&
            feedback != null && feedback.isActiveAndEnabled && feedback.text.Contains("Không đủ vàng") && feedback.text.Contains("10"),
            "Insufficient funds keep a cancellable purchase open with visible feedback, without stock/coin/model mutation.");
    }

    private static bool ModalActive { get { return ((RectTransform)Get(controller, "modalLayer")).Cast<Transform>().Any(t => t.gameObject.activeSelf); } }
    private static Button ModelButton(string caption)
    { return ((RectTransform)Get(controller, "modalLayer")).GetComponentsInChildren<Button>().Single(b => b.name == "Button " + caption); }
    private static Button PurchaseButton(string caption) { return ModelButton(caption); }
    private static void Request(string name)
    {
        var button = (Button)Get(controller, name.ToLowerInvariant() + "Button");
        if (button != null) button.onClick.Invoke();
        else Invoke(name); // Legacy scene may omit Magnet/Shuffle; still probe their guarded controller entry points.
    }

    private static object Booster(string name)
    { return Enum.Parse(typeof(CandySortGameController).Assembly.GetType("CandySort.CandySortBooster", true), name); }
    private static object InventoryCall(object target, string method, params object[] args)
    {
        MethodInfo member = target.GetType().GetMethod(method, Fields);
        Check(member != null, "Required finite-stock API exists: " + method);
        return member.Invoke(target, args);
    }
    private static int Stock(string name) { return (int)InventoryCall(inventory, "Count", Booster(name)); }
    private static bool Unlocked(string name) { return (bool)InventoryCall(inventory, "IsUnlocked", Booster(name)); }
    private static string StockSignature() { return string.Join(";", BoosterNames.Select(name => name + ":" + Unlocked(name) + ":" + Stock(name))); }
    private static object NewInventory(Dictionary<string, int> prefs)
    {
        Type type = Field(controller, "boosterInventory").FieldType;
        return Activator.CreateInstance(type, new object[]
        {
            new Func<string, int, int>((key, fallback) => prefs.TryGetValue(key, out int value) ? value : fallback),
            new Action<string, int>((key, value) => prefs[key] = value),
            new Action(() => inventorySaves++)
        });
    }
    private static void InjectInventory()
    {
        inventoryPrefs = new Dictionary<string, int>();
        inventory = NewInventory(inventoryPrefs);
        Set(controller, "boosterInventory", inventory);
    }
    private static void InjectWallet()
    {
        Set(controller, "readCoins", new Func<long>(() => wallet));
        Set(controller, "changeCoins", new Action<int, Action, Action>((amount, success, error) =>
        {
            if (wallet + amount < 0) { error?.Invoke(); return; }
            wallet += amount;
            walletWrites++;
            if (deferWalletChange)
            {
                Check(pendingWalletSuccess == null, "Fake wallet accepts only one deferred transaction.");
                pendingWalletSuccess = success;
            }
            else success?.Invoke();
        }));
    }

    private static void ProbeInventoryThresholds()
    {
        var prefs = new Dictionary<string, int>();
        object probe = NewInventory(prefs);
        for (int level = 1; level <= 7; level++)
        {
            InventoryCall(probe, "GrantUnlocked", level);
            for (int i = 0; i < BoosterNames.Length; i++)
            {
                object booster = Booster(BoosterNames[i]);
                bool unlocked = level >= UnlockLevels[i];
                Check((bool)InventoryCall(probe, "IsUnlocked", booster) == unlocked &&
                    (int)InventoryCall(probe, "Count", booster) == (unlocked ? 2 : 0),
                    "Level " + level + " threshold grants exactly two once: " + BoosterNames[i]);
                if (!unlocked) Check(!(bool)InventoryCall(probe, "TryConsume", booster) && !(bool)InventoryCall(probe, "AddPurchasedUse", booster),
                    "Locked stock cannot be consumed or purchased before its unlock.");
            }
        }
        foreach (string name in BoosterNames)
        {
            object booster = Booster(name);
            Check((bool)InventoryCall(probe, "TryConsume", booster) && (bool)InventoryCall(probe, "TryConsume", booster) &&
                !(bool)InventoryCall(probe, "TryConsume", booster), "Each welcome grant permits exactly two uses: " + name);
        }
        InventoryCall(probe, "GrantUnlocked", 30);
        InventoryCall(probe, "GrantUnlocked", 1);
        probe = NewInventory(prefs); // Simulate persistence with this dictionary only, never PlayerPrefs.
        foreach (string name in BoosterNames)
        {
            object booster = Booster(name);
            Check((bool)InventoryCall(probe, "IsUnlocked", booster) && (int)InventoryCall(probe, "Count", booster) == 0,
                "Reload/backtracking cannot replenish exhausted free stock: " + name);
            Check((bool)InventoryCall(probe, "AddPurchasedUse", booster) && (int)InventoryCall(probe, "Count", booster) == 1,
                "One purchased use increments only that unlocked stock: " + name);
        }
        Pass("Isolated dictionary inventory: unlock thresholds 3/5/7, two free once, exhausted zero survives reload/backtrack, paid +1.");
    }

    private static void ProbeControllerLocks()
    {
        foreach (int level in new[] { 2, 3, 4, 5, 6, 7 })
        {
            InjectInventory();
            Load(level);
            VerifyStage(level);
            string model = ModelSignature(), board = BoardSignature(), stock = StockSignature();
            int writes = walletWrites;
            for (int i = 0; i < BoosterNames.Length; i++)
            {
                if (level >= UnlockLevels[i]) continue;
                Request(BoosterNames[i]);
                Invoke("RequestBooster", Booster(BoosterNames[i]));
                Invoke("ExecuteBooster", Booster(BoosterNames[i]));
                Check(ModelSignature() == model && BoardSignature() == board && StockSignature() == stock && walletWrites == writes && !ModalActive,
                    "Locked booster rejects bound/direct entry without purchase or spend at level " + level + ": " + BoosterNames[i]);
            }
        }
    }

    private static int ModelHistoryCount(CandySortModel model) { return ((ICollection)Get(model, "history")).Count; }
    private static int BoardHistoryCount() { return ((ICollection)Get(controller, "boardHistory")).Count; }

    private static Button CandyButton(int id, int index)
    {
        object bubble = ((IDictionary)Get(controller, "bubbles"))[id];
        var slots = (IDictionary)Get(controller, "candySlots");
        int originalSlot = (int)((IList)slots[id])[index];
        return ((IList)Get(bubble, "Buttons"))[originalSlot] as Button;
    }

    private static Button PlayableCandy(bool matching)
    {
        Canvas.ForceUpdateCanvases();
        var canvas = controller.GetComponentInChildren<Canvas>();
        foreach (var bubble in Model.Bubbles)
        for (int i = 0; i < bubble.Candies.Count; i++)
        {
            if (Model.Plates.Any(p => p.Active && p.Kind == bubble.Candies[i]) != matching) continue;
            Button button = CandyButton(bubble.Id, i);
            var pointer = new PointerEventData(EventSystem.current)
            {
                position = RectTransformUtility.WorldToScreenPoint(canvas.worldCamera, button.transform.position)
            };
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer, hits);
            if (hits.Count > 0 && hits[0].gameObject.GetComponentInParent<Button>() == button) return button;
        }
        return null;
    }

    private static string BoardSignature()
    {
        var positions = ((CandyBubbleLayout)Get(controller, "layout")).Capture();
        var slots = (IDictionary)Get(controller, "candySlots");
        return string.Join(";", positions.OrderBy(pair => pair.Key).Select(pair => pair.Key + ":" +
            pair.Value.X.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ":" +
            pair.Value.Y.ToString("R", System.Globalization.CultureInfo.InvariantCulture))) + "|slots=" +
            string.Join(";", slots.Keys.Cast<int>().OrderBy(id => id).Select(id => id + ":" + string.Join(",", ((IList)slots[id]).Cast<int>())));
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
                    "Plate " + (i + 1) + " accepts a missing Unlock caption or the authored TMP/Image graphic.");
                Check(icon != null && icon.sprite != null && icon.sprite.name == "erasebg-transformed_4",
                    "Plate " + (i + 1) + " has its authored lock Image / erasebg-transformed_4.");
            }
            result.plates.Add(new PlateArt { path = PathOf(rect), dishPath = PathOf(dish.transform), dishSprite = SpriteKey(dish.sprite),
                lockPath = icon == null ? "" : PathOf(icon.transform), lockSprite = icon == null ? "" : SpriteKey(icon.sprite) });
        }
        return result;
    }

    private static void VerifyTextComponents()
    {
        Check(controller.gameObject.scene.GetRootGameObjects().All(root =>
            root.GetComponentsInChildren<UnityEngine.UI.Text>(true).Length == 0),
            "No legacy UI Text components remain in the authored/runtime scene, including inactive objects.");
        foreach (string name in new[] { "levelLabel", "queueLabel" })
            Check(Get(controller, name) is TextMeshProUGUI, "Required HUD label uses TextMeshProUGUI: " + name);
        Check(Get(controller, "progressLabel") is TextMeshProUGUI || (IsGameplay && Get(controller, "progressLabel") == null),
            "Legacy progressLabel remains required in CandySortGame; Gameplay permits its removal.");
        if (!IsGameplay)
        {
            Check(Get(controller, "progressFill") is Image && Get(controller, "restartButton") is Button,
                "Original CandySortGame retains legacy progressFill and restartButton bindings.");
        }
        foreach (string name in IsGameplay ? new[] { "magnetButton", "undoButton", "shuffleButton" } : new[] { "undoButton" })
            Check(Get(controller, name) is Button, "Required authored button is bound: " + name);
        Check(Get(controller, "helpButton") == null || Get(controller, "helpButton") is Button,
            "Optional helpButton is valid when assigned.");
        var boosters = new[] { "magnetButton", "undoButton", "shuffleButton" }.Select(name => Get(controller, name) as Button).Where(b => b != null).ToArray();
        Check(boosters.Distinct().Count() == boosters.Length, "Each assigned booster uses a distinct authored Button.");
        foreach (string name in new[] { "difficultyLabel", "hintLabel", "magnetCountLabel", "undoCountLabel", "shuffleCountLabel" })
            Check(Get(controller, name) == null || Get(controller, name) is TextMeshProUGUI,
                "Optional HUD label uses TextMeshProUGUI when assigned: " + name);
        foreach (object view in Views("plateViews"))
        {
            Check(Get(view, "Count") is TextMeshProUGUI, "Required plate Count uses TextMeshProUGUI.");
            foreach (string name in new[] { "Locked", "Served" })
                Check(Get(view, name) == null || Get(view, name) is TextMeshProUGUI,
                    "Optional plate caption uses TextMeshProUGUI: " + name);
            Check(Get(view, "Unlock") == null || Get(view, "Unlock") is TextMeshProUGUI || Get(view, "Unlock") is Image,
                "Optional Unlock keeps its authored TMP caption or Image artwork.");
        }
        foreach (object view in Views("tempViews"))
            Check(Get(view, "Empty") == null || Get(view, "Empty") is TextMeshProUGUI,
                "Optional tray Empty caption uses TextMeshProUGUI.");
    }

    private static void VerifyRetained()
    {
        VerifyTextComponents();
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
            // TMP captions may accompany an authored icon; captions must disappear on unlock.
            if (icon == null || plate.Active) OptionalVisible(view, "Unlock", !plate.Active);
            bool serving = plate.Active && plate.Kind.HasValue;
            var dish = (Image)Get(view, "Dish");
            Visible(dish, serving, "Dish " + (i + 1));
            bool legacy = dish.sprite == CandySortArt.Circle || dish.sprite == CandySortArt.Ring;
            var ring = (Image)Get(view, "Ring");
            if (ring != null) Visible(ring, serving && legacy, "Legacy rim " + (i + 1));
            Visible((Image)Get(view, "Sample"), serving, "Flavor sample " + (i + 1));
            Visible((TextMeshProUGUI)Get(view, "Count"), serving, "Count " + (i + 1));
            if (serving)
            {
                Check(((TextMeshProUGUI)Get(view, "Count")).text == plate.Count + "/3", "Rendered plate count matches the model.");
                Check(((Image)Get(view, "Sample")).sprite == CandySortArt.Candy(plate.Kind.Value), "Rendered plate sample matches the assigned flavor.");
            }
            OptionalVisible(view, "Served", plate.Active && !plate.Kind.HasValue);
            var candies = (Image[])Get(view, "Candies");
            Check(candies.Length == 3 && candies.All(c => c != null), "Three required Candy Images bound for plate " + (i + 1));
            for (int c = 0; c < 3; c++)
            {
                Visible(candies[c], serving && c < plate.Count, "Plate candy " + i + "/" + c);
                if (serving && c < plate.Count) Check(candies[c].sprite == CandySortArt.Candy(plate.Kind.Value), "Plate candy art matches its model flavor.");
            }
            if (i >= 2 && Get(view, "Unlock") is TextMeshProUGUI unlock)
                Check(unlock.transform.IsChildOf((RectTransform)Get(view, "Rect")), "Recovered TMP Unlock stays on its authored plate.");
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
        VerifyRenderedBoard();
        Check(((TextMeshProUGUI)Get(controller, "levelLabel")).text == "MÀN " + level.ToString("00") + " / " + CandySortCampaign.LevelCount,
            "Rendered level label matches the model.");
        if (Get(controller, "progressLabel") is TextMeshProUGUI progress)
            Check(progress.text == Model.CompletedOrders + " / " + Model.TargetOrders + " ĐĨA", "Legacy progress caption matches the model.");
        foreach (string name in BoosterNames)
        {
            string prefix = name.ToLowerInvariant();
            var button = (Button)Get(controller, prefix + "Button");
            var caption = (TextMeshProUGUI)Get(controller, prefix + "CountLabel");
            bool unlocked = Unlocked(name);
            int index = Array.IndexOf(BoosterNames, name);
            if (button != null)
            {
                Check(caption != null, "Assigned booster has an authored or fallback TMP count: " + name);
                bool actionAvailable = name == "Magnet" ? Model.MagnetPlateIndex >= 0 : name == "Shuffle" ? Model.CanShuffle : Model.CanUndo && BoardHistoryCount() > 0;
                bool available = !(bool)Get(controller, "busy") && !Model.IsWon && unlocked && actionAvailable &&
                    !(bool)Get(controller, "purchaseInFlight") && !ModalActive;
                Check(button.interactable == available, "Booster uses lock/action/busy/modal gates, not a positive-stock gate: " + name);
            }
            if (caption != null)
            {
                Check(caption.text == (unlocked ? Stock(name).ToString() : "LV " + UnlockLevels[index]),
                    "Booster label shows finite stock or its unlock threshold: " + name);
                Check(caption.isActiveAndEnabled, "Bound/fallback booster count remains visible: " + name);
            }
        }
        VerifyActiveText(controller.transform, "Stage " + level);
        Pass("Stage " + level + ": locks, custom dishes, legacy rims, tray, rendered candy slots/boosters and authored identities/layout.");
    }

    private static void VerifyRenderedBoard()
    {
        var views = (IDictionary)Get(controller, "bubbles");
        var slots = (IDictionary)Get(controller, "candySlots");
        Check(views.Keys.Cast<int>().OrderBy(id => id).SequenceEqual(Model.Bubbles.Select(b => b.Id).OrderBy(id => id)),
            "Rendered bubble identities exactly match the model's live bubbles.");
        var positions = ((CandyBubbleLayout)Get(controller, "layout")).Capture();
        Check(positions.Keys.OrderBy(id => id).SequenceEqual(Model.Bubbles.Select(b => b.Id).OrderBy(id => id)),
            "Layout bodies exactly match the model's live bubbles.");
        foreach (CandySortModel.Bubble bubble in Model.Bubbles)
        {
            object view = views[bubble.Id];
            var rect = (RectTransform)Get(view, "Rect");
            Check(rect.gameObject.activeInHierarchy, "Live bubble is rendered: " + bubble.Id);
            var target = (Vector2)Get(view, "Target");
            Check(Near(target, new Vector2(positions[bubble.Id].X, positions[bubble.Id].Y)), "Bubble's rendered target matches its layout body.");
            var mapping = ((IList)slots[bubble.Id]).Cast<int>().ToArray();
            var buttons = ((IList)Get(view, "Buttons")).Cast<Button>().ToArray();
            Check(mapping.Length == bubble.Candies.Count && mapping.Distinct().Count() == mapping.Length &&
                mapping.All(slot => slot >= 0 && slot < buttons.Length), "Every model candy maps to exactly one original visual slot.");
            for (int slot = 0; slot < buttons.Length; slot++)
                Visible(buttons[slot], mapping.Contains(slot), "Bubble candy slot " + bubble.Id + "/" + slot);
            for (int i = 0; i < bubble.Candies.Count; i++)
            {
                Button button = buttons[mapping[i]];
                var image = button.GetComponent<Image>();
                Check(image != null && image.enabled && image.color.a > 0 && image.sprite == CandySortArt.Candy(bubble.Candies[i]),
                    "Rendered board candy artwork matches its model flavor and original slot.");
                Check(button.name == "Candy " + CandySortArt.Names[(int)bubble.Candies[i]], "Rendered candy name matches its current binding.");
                Check(Vector3.Distance(button.transform.localScale, Vector3.one) < .001f,
                    "Rendered candy restores its full scale after booster animations.");
            }
        }
    }

    private static void ProbeDialogs()
    {
        Check(EditorApplication.isPlaying && !(bool)Get(controller, "rememberProgress"),
            "Dialog probes run only on the Play copy with persistence disabled.");
        var modal = (RectTransform)Get(controller, "modalLayer");
        try
        {
            Load(1);
            Invoke("ShowHelp");
            VerifyDialog("How to play", "MỘT CHÚT NGỌT NGÀO");
            var close = modal.GetComponentsInChildren<Button>().Single(button => button.name == "Button BẮT ĐẦU THÔI");
            close.onClick.Invoke();
            Check(!modal.GetComponentsInChildren<TextMeshProUGUI>().Any(), "Help close action clears its live labels.");
            // ShowResult only builds its dialog. Never invoke CompleteLevel, save callbacks or progression buttons.
            Invoke("ShowResult", false);
            VerifyDialog("Result card", "KHAY TẠM ĐÃ ĐẦY");
            Invoke("Clear", modal);
            Invoke("ShowResult", true);
            VerifyDialog("Result card", "THẬT NGỌT NGÀO!");
            Invoke("Clear", modal);
            Load(CandySortCampaign.LevelCount);
            Invoke("ShowResult", true);
            VerifyDialog("Result card", "HOÀN THÀNH 30 MÀN!");
            Pass("Play-only help, loss, win and final-win TMP dialogs; no completion/save callbacks invoked.");
        }
        finally
        {
            Invoke("Clear", modal);
            Load(1);
        }
        Check(!modal.GetComponentsInChildren<TextMeshProUGUI>().Any(), "Dialog probes leave no active modal labels before later stages.");
        VerifyStage(1);
    }

    private static void VerifyDialog(string cardName, string heading)
    {
        var modal = (RectTransform)Get(controller, "modalLayer");
        Transform card = modal.GetComponentsInChildren<Transform>().SingleOrDefault(item => item.name == cardName);
        Check(card != null, "Runtime dialog exists: " + heading);
        Check(card.GetComponentsInChildren<TextMeshProUGUI>().Any(label => label.text == heading),
            "Runtime dialog heading uses TextMeshProUGUI: " + heading);
        VerifyTextComponents();
        VerifyActiveText(card, heading);
    }

    private static void VerifyActiveText(Transform scope, string context)
    {
        Canvas.ForceUpdateCanvases();
        // Hidden/disabled/transparent optional decorations are not essential visible captions.
        var labels = scope.GetComponentsInChildren<TextMeshProUGUI>().Where(label =>
            label.isActiveAndEnabled && label.color.a > 0 && !string.IsNullOrWhiteSpace(label.text)).ToArray();
        Check(labels.Length > 0, "Visible TMP labels exist: " + context);
        int vietnameseGlyphs = 0, overflows = 0;
        foreach (var label in labels)
        {
            string path = PathOf(label.transform);
            Check(label.font != null, "Active TMP label has a font: " + path);
            label.ForceMeshUpdate();
            foreach (char character in label.text.Distinct().Where(c => !char.IsWhiteSpace(c) && !char.IsControl(c)))
            {
                Check(label.font.HasCharacter(character, true),
                    "Missing TMP glyph U+" + ((int)character).ToString("X4") + " in " + label.font.name + ": " + path);
                if (character > 127 && char.IsLetter(character)) vietnameseGlyphs++;
            }
            // Preferred size can use the maximum auto-size font, not the fitted mesh that is visible.
            Vector2 rendered = label.textBounds.size;
            Vector2 available = label.rectTransform.rect.size;
            if (label.isTextOverflowing || label.isTextTruncated || rendered.x > available.x + 1 || rendered.y > available.y + 1)
            {
                overflows++;
                File.AppendAllText(ReportPath, "WARNING: TMP text fit [" + context + "] " + path +
                    "; font=" + label.font.name + "; rendered=" + rendered + "; rect=" + available +
                    "; overflow=" + label.isTextOverflowing + "; truncated=" + label.isTextTruncated + "\n");
            }
        }
        Check(vietnameseGlyphs > 0, "Vietnamese accented glyphs were exercised: " + context);
        Pass(context + ": " + labels.Length + " active TMP labels have fonts and glyph coverage; " + overflows +
            " text-fit warnings (hidden optional captions excluded).");
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
                Check(saved.All(item => Get(item.Item1, item.Item2) == null), "Null optional TMP caption/rim/icon references stay safe during Render.");
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
        // Plate 1 has both TMP fallback captions and no LockIcon. Change only its runtime Active flag for this render-only probe.
        PropertyInfo active = typeof(CandySortModel.Plate).GetProperty("Active");
        CandySortModel.Plate plate = Model.Plates[0];
        try
        {
            Check(Get(first, "LockIcon") == null && Get(first, "Locked") != null && Get(first, "Unlock") != null, "TMP lock/unlock fallback fixture exists.");
            active.GetSetMethod(true).Invoke(plate, new object[] { false });
            Invoke("RenderPlates");
            OptionalVisible(first, "Locked", true);
            OptionalVisible(first, "Unlock", true);
        }
        finally { active.GetSetMethod(true).Invoke(plate, new object[] { true }); Invoke("RenderPlates"); }
        VerifyStage(1);
        Pass("Optional TMP Locked/Unlock/Served/Empty nulls, Circle/Ring rim compatibility and TMP Locked/Unlock fallback.");
    }

    private static RectTransform[] CurrentRects()
    {
        // Safe-area fitting intentionally moves/scales the outer root; compare authored LOCAL plate/tray layout instead.
        return new[] { "Candy plates", "Temporary tray" }.SelectMany(name =>
        {
            RectTransform layer = controller.GetComponentsInChildren<RectTransform>(true).Single(r => r.name == name);
            // TMP creates renderer children for fallback glyphs; they are not authored views.
            return layer.GetComponentsInChildren<RectTransform>(true).Where(rect => rect.GetComponent<TMP_SubMeshUI>() == null);
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
    private static string ModelSignature() { return ModelSignature(Model); }
    private static string ModelSignature(CandySortModel model)
    {
        return string.Join("|", new[] { model.Level.ToString(), model.CompletedOrders.ToString(), model.SortedCandies.ToString(),
            model.TotalCandies.ToString(), model.TargetOrders.ToString(), model.ActivePlates.ToString(), model.IsWon.ToString(),
            model.IsBlocked.ToString(), model.CanUndo.ToString(), ModelHistoryCount(model).ToString(),
            Get(Get(model, "shuffleRandom"), "state").ToString(), model.MagnetPlateIndex.ToString(), model.CanShuffle.ToString(),
            string.Join(",", model.Temp.Select(k => k.HasValue ? ((int)k.Value).ToString() : "-")),
            string.Join(",", model.PendingOrders.Select(k => (int)k)),
            string.Join(";", model.Plates.Select(p => p.Active + ":" + p.Kind + ":" + p.Count)),
            string.Join(";", model.Bubbles.Select(b => b.Id + ":" + b.Lane + ":" + string.Join(",", b.Candies.Select(k => (int)k)))) });
    }
    private static void Capture(int level) { Capture("edited-ui-stage-" + level); }
    private static void Capture(string name)
    {
        screenshot = Path.GetFullPath(OutputPath + "/" + name + ".png");
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
            else
            {
                EditorApplication.delayCall -= ResetSession;
                EditorApplication.delayCall += ResetSession;
            }
        }
    }
    private static void ResetSession()
    {
        if (!editModeConfirmed) return;
        if (Application.isPlaying || EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorApplication.delayCall -= ResetSession;
            EditorApplication.delayCall += ResetSession;
            return;
        }
        string setupJson = SessionState.GetString(SceneSetupKey, "");
        if (setupJson.Length > 0)
        {
            // Restore only saved clean scenes after confirmed Edit Mode and a delayed
            // editor tick. Keep the saved setup if restoring fails or new edits exist.
            if (ScenesAreClean())
            {
                var setup = JsonUtility.FromJson<SavedSceneSetup>(setupJson);
                EditorSceneManager.RestoreSceneManagerSetup(setup.scenes.Select(scene => new SceneSetup
                    { path = scene.path, isLoaded = scene.loaded, isActive = scene.active }).ToArray());
                SessionState.EraseString(SceneSetupKey); // Never discard recovery data before restoration succeeds.
                Pass("Restored the previous saved editor scene setup without saving any scene.");
            }
            else File.AppendAllText(ReportPath, "WARNING: Prior scene setup was not restored because new unsaved scene edits exist.\n");
        }
        SessionState.EraseString(TargetSceneKey);
        SessionState.EraseString(StateKey);
        SessionState.EraseString(SnapshotKey);
        SessionState.EraseString(ErrorKey);
        SessionState.EraseFloat(DeadlineKey);
        controller = null;
        authored = null;
        transformIds = imageIds = null;
        controllerReferences = null;
        expectedBoosterModel = null;
        boosterModelBefore = boosterBoardBefore = null;
        inventory = null;
        inventoryPrefs = null;
        pendingWalletSuccess = null;
        economyInjected = economyIsolated = deferWalletChange = false;
        screenshot = null;
    }
}
