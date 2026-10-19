using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using CandySort;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Editor-only entry points. Smoke automation runs only after an explicit request file is created.
[InitializeOnLoad]
public static class CandySortEditor
{
    private const string ScenePath = "Assets/Scenes/CandySortGame.unity";
    private const string RequestPath = "Temp/CandySortSmoke.request";
    private const string OutputPath = "Temp/CandySortVerification";
    private static double nextTick;
    private static int step, clicks;
    private static CandySortGameController controller;
    private static bool testing;
    private static string report;
    private static Action deferredAction;
    private static int probeId = -1, probeCount, probeMoves, probeUndos;
    private static float probeDiameter;
    private static System.Collections.Generic.Dictionary<int, CandyBubbleLayout.Point> probeLayout;
    private static System.Collections.Generic.Dictionary<int, Vector2[]> probeCandyOffsets;
    private static int expectedCandyBubbleId;
    private static Vector2[] expectedCandyOffsets;
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly int[] CampaignSmokeLevels = { 1, 5, 15, 30 };
    private static CampaignSolutions campaignSolutions;
    private static int campaignIndex, solutionMove;
    private static bool stageCaptured, trayCaptured;
    private static double moveWaitStarted;

    [Serializable]
    private sealed class CampaignSolutions { public CampaignSolution[] levels = new CampaignSolution[0]; }
    [Serializable]
    private sealed class CampaignSolution { public int level = 0; public CampaignMove[] moves = new CampaignMove[0]; }
    [Serializable]
    private sealed class CampaignMove { public int bubbleId = 0; public int candyIndex = 0; }

    static CandySortEditor()
    {
        EditorApplication.update += Tick;
    }

    [MenuItem("Candy Sort/Open Game")]
    public static void OpenGame()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorSceneManager.OpenScene(ScenePath);
    }

    [MenuItem("Candy Sort/Play Game")]
    public static void PlayGame()
    {
        OpenGame();
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path == ScenePath) EditorApplication.isPlaying = true;
    }

    [MenuItem("Candy Sort/Run Play Mode Smoke Test")]
    public static void RequestSmoke()
    {
        File.WriteAllText(RequestPath, "run");
    }

    private static void Tick()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.timeSinceStartup < nextTick) return;
        if (!testing && File.Exists(RequestPath))
        {
            File.Delete(RequestPath);
            Directory.CreateDirectory(OutputPath);
            if (EditorApplication.isPlaying || UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)
            {
                File.WriteAllText(OutputPath + "/report.txt", "SKIPPED: stop Play mode and save the active scene before running the smoke test.");
                return;
            }
            testing = true;
            step = 0;
            clicks = 0;
            probeId = -1;
            expectedCandyOffsets = null;
            deferredAction = null;
            report = "Candy Sort: live Unity Play Mode smoke test\n";
            SessionState.SetBool("CandySortSmokeActive", true);
            EditorSceneManager.OpenScene(ScenePath);
            SetPortraitGameView();
            EditorApplication.isPlaying = true;
            nextTick = EditorApplication.timeSinceStartup + 3;
            return;
        }
        // Domain reload clears static state when entering Play mode.
        if (!testing && SessionState.GetBool("CandySortSmokeActive", false) && EditorApplication.isPlaying)
        {
            testing = true;
            step = 0;
            report = "Candy Sort: live Unity Play Mode smoke test\n";
            nextTick = EditorApplication.timeSinceStartup + 3;
            return;
        }
        if (!testing || !EditorApplication.isPlaying) return;
        try
        {
            controller = UnityEngine.Object.FindFirstObjectByType<CandySortGameController>();
            Require(controller != null, "Scene controller exists");
            var model = (CandySortModel)typeof(CandySortGameController).GetField("model", Private).GetValue(controller);
            Require(model != null, "Level initialized");
            if (expectedCandyOffsets != null)
            {
                if (expectedCandyOffsets.Length > 0)
                    Require(GetCandyOffsets(expectedCandyBubbleId).SequenceEqual(expectedCandyOffsets), "Remaining candies keep their original positions");
                expectedCandyOffsets = null;
            }
            if (deferredAction != null)
            {
                Action action = deferredAction;
                deferredAction = null;
                action();
                nextTick = EditorApplication.timeSinceStartup + .8;
                return;
            }
            if (step == 0)
            {
                // Never write the user's progression during automated smoke testing.
                typeof(CandySortGameController).GetField("rememberProgress", Private).SetValue(controller, false);
                string solutionsPath = "Temp/CandySortCampaignVerification/solutions.json";
                Require(File.Exists(solutionsPath), "Campaign solutions exist: run dotnet run --project Tests/CandySort/CandySort.Tests.csproj first");
                campaignSolutions = JsonUtility.FromJson<CampaignSolutions>(File.ReadAllText(solutionsPath));
                Require(campaignSolutions.levels.Length == CandySortCampaign.LevelCount, "All 30 campaign stages have verified solution traces");
                solutionMove = 0;
                moveWaitStarted = 0;
                LoadLevel(1);
                step++;
                nextTick = EditorApplication.timeSinceStartup + 3;
            }
            else if (step == 1)
            {
                Require(model.Level == 1 && model.ActivePlates == 2 && model.TotalCandies == 81, "Opening stage starts with 81 candies and 2 plates");
                Require(model.Definition.FlavorCount == 2 && !model.Definition.IsHard, "Tutorial introduces direct matching with two flavors");
                VerifyBubbleGeometry();
                Capture("01-start");
                deferredAction = () => LoadLevel(4);
                step = 12;
                nextTick = EditorApplication.timeSinceStartup + .8;
            }
            else if (step == 12)
            {
                VerifyBubbleGeometry();
                var wrong = model.Bubbles.First(b => b.Candies.Select((k, i) => new { Kind = k, Index = i }).Any(c => !model.Plates.Any(p => p.Kind == c.Kind) && CanClickCandy(b.Id, c.Index)));
                int index = Enumerable.Range(0, wrong.Candies.Count).First(i => !model.Plates.Any(p => p.Kind == wrong.Candies[i]) && CanClickCandy(wrong.Id, i));
                deferredAction = () => ClickCandy(wrong.Id, index);
                step = 2;
                nextTick = EditorApplication.timeSinceStartup + .8;
            }
            else if (step == 2)
            {
                Require(model.Temp.Any(k => k.HasValue), "Unmatched candy goes to temporary tray");
                Capture("02-temp-slot");
                deferredAction = () => ClickButton("Button HOÀN TÁC");
                step++;
                nextTick = EditorApplication.timeSinceStartup + .4;
            }
            else if (step == 3)
            {
                if (probeId < 0)
                {
                    Require(!model.Temp.Any(k => k.HasValue) && model.Bubbles.Sum(b => b.Candies.Count) == model.TotalCandies, "Undo restores candy and tray");
                    var bubble = model.Bubbles.First(b => b.Candies.Count >= 3 && SupportsOtherBubble(b.Id) && Enumerable.Range(0, b.Candies.Count).All(i => CanClickCandy(b.Id, i)));
                    probeId = bubble.Id;
                    probeCount = bubble.Candies.Count;
                    probeMoves = probeUndos = 0;
                    object view = ((IDictionary)typeof(CandySortGameController).GetField("bubbles", Private).GetValue(controller))[probeId];
                    probeDiameter = (float)view.GetType().GetField("Diameter").GetValue(view);
                    probeLayout = GetLayout().Capture();
                    probeCandyOffsets = model.Bubbles.ToDictionary(b => b.Id, b => GetCandyOffsets(b.Id));
                }
                if (probeMoves < probeCount)
                {
                    ClickCandy(probeId, 0);
                    probeMoves++;
                    nextTick = EditorApplication.timeSinceStartup + .6;
                }
                else if (probeUndos < probeCount)
                {
                    if (probeUndos == 0)
                    {
                        Require(!model.Bubbles.Any(b => b.Id == probeId), "Emptied bubble is removed");
                        var views = (IDictionary)typeof(CandySortGameController).GetField("bubbles", Private).GetValue(controller);
                        Require(GetLayout().Bodies.Any(b =>
                        {
                            object view = views[b.Id];
                            return ((RectTransform)view.GetType().GetField("Rect").GetValue(view)).anchoredPosition.y < probeLayout[b.Id].Y - 20;
                        }), "Supported bubbles physically settle into the freed gap");
                        VerifyBubbleGeometry();
                        Capture("07-bubble-collapse");
                        deferredAction = () => ClickButton("Button HOÀN TÁC");
                        probeUndos++;
                        nextTick = EditorApplication.timeSinceStartup + .3;
                        return;
                    }
                    ClickButton("Button HOÀN TÁC");
                    probeUndos++;
                    nextTick = EditorApplication.timeSinceStartup + .6;
                }
                else
                {
                    object view = ((IDictionary)typeof(CandySortGameController).GetField("bubbles", Private).GetValue(controller))[probeId];
                    Require(model.Bubbles.First(b => b.Id == probeId).Candies.Count == probeCount, "Repeated undo restores every candy");
                    Require((int)view.GetType().GetField("InitialCount").GetValue(view) == probeCount && Mathf.Approximately((float)view.GetType().GetField("Diameter").GetValue(view), probeDiameter), "Restored bubble preserves its original size and candy layout");
                    Require(GetLayout().Bodies.All(b => Mathf.Abs(b.Position.X - probeLayout[b.Id].X) < .01f && Mathf.Abs(b.Position.Y - probeLayout[b.Id].Y) < .01f), "Undo restores the exact irregular layout");
                    Require(model.Bubbles.All(b => GetCandyOffsets(b.Id).SequenceEqual(probeCandyOffsets[b.Id])), "Undo restores original candy positions, including recreated bubbles");
                    step++;
                }
            }
            else if (step == 4)
            {
                if (model.IsWon)
                {
                    Require(GameObject.Find("Result card") != null, "Win dialog is displayed");
                    Capture("03-level-complete");
                    deferredAction = () => ClickButton("Button MÀN TIẾP THEO  →");
                    step++;
                    nextTick = EditorApplication.timeSinceStartup + 3;
                    return;
                }
                PlaySolutionMove(model);
            }
            else if (step == 5)
            {
                Require(model.Level == 5 && model.Definition.IsHard, "Next-level button advances from level 4 to hard level 5");
                typeof(CandySortGameController).GetMethod("LoadLevel", Private).Invoke(controller, new object[] { 3 });
                step++;
                nextTick = EditorApplication.timeSinceStartup + 2;
            }
            else if (step == 6)
            {
                Require(model.ActivePlates == 3, "Third plate unlocks at level 3");
                typeof(CandySortGameController).GetMethod("LoadLevel", Private).Invoke(controller, new object[] { 5 });
                step++;
                nextTick = EditorApplication.timeSinceStartup + 2;
            }
            else if (step == 7)
            {
                Require(model.ActivePlates == 4 && model.Definition.IsHard, "Fourth plate unlocks at hard level 5");
                VerifyHardBubbleGroups(model);
                VerifyBubbleGeometry();
                Capture("04-all-plates");
                deferredAction = () => ClickButton("Button CHƠI LẠI");
                step++;
                nextTick = EditorApplication.timeSinceStartup + 2;
            }
            else if (step == 8)
            {
                Require(model.CompletedOrders == 0 && model.SortedCandies == 0, "Restart resets level");
                ClickButton("Button ?");
                Require(GameObject.Find("How to play") != null, "Help opens");
                Capture("05-help");
                step++;
                nextTick = EditorApplication.timeSinceStartup + 1;
            }
            else if (step == 9)
            {
                ClickButton("Button BẮT ĐẦU THÔI");
                SetPortraitGameView(390, 844);
                step++;
                nextTick = EditorApplication.timeSinceStartup + 2;
            }
            else if (step == 10)
            {
                var playArea = controller.GetComponentsInChildren<RectTransform>().First(r => r.name == "Portrait play area");
                var corners = new Vector3[4];
                playArea.GetWorldCorners(corners);
                Require(corners[0].x >= -1 && corners[0].y >= -1 && corners[2].x <= Screen.width + 1 && corners[2].y <= Screen.height + 1, "Portrait UI fits 390x844 without clipping");
                Capture("06-narrow-portrait");
                step++;
                nextTick = EditorApplication.timeSinceStartup + .5;
            }
            else if (step == 11)
            {
                SetPortraitGameView();
                campaignIndex = solutionMove = 0;
                stageCaptured = trayCaptured = false;
                moveWaitStarted = 0;
                LoadLevel(CampaignSmokeLevels[campaignIndex]);
                step = 13;
                nextTick = EditorApplication.timeSinceStartup + 3;
            }
            else if (step == 13)
            {
                Require(model.Level == CampaignSmokeLevels[campaignIndex], "Representative campaign stage loaded: " + model.Level);
                if (model.IsWon)
                {
                    Require(GameObject.Find("Result card") != null, "Campaign stage cleared through visible UI clicks: " + model.Level);
                    if (model.Level == CandySortCampaign.LevelCount)
                    {
                        Require(GameObject.Find("Button MÀN TIẾP THEO  →") == null, "Campaign completion has no level-31 action");
                        VerifyFinalPersistence();
                        Capture("10-campaign-complete");
                        deferredAction = () => ClickButton("Button CHƠI LẠI MÀN 30");
                        step = 14;
                    }
                    else
                    {
                        int nextLevel = model.Level + 1;
                        ClickButton("Button MÀN TIẾP THEO  →");
                        Require(((CandySortModel)typeof(CandySortGameController).GetField("model", Private).GetValue(controller)).Level == nextLevel, "Normal next-stage action remains available");
                        campaignIndex++;
                        LoadLevel(CampaignSmokeLevels[campaignIndex]);
                        solutionMove = 0;
                        stageCaptured = trayCaptured = false;
                    }
                    nextTick = EditorApplication.timeSinceStartup + 3;
                }
                else if (!stageCaptured)
                {
                    if (model.Definition.IsHard) VerifyHardBubbleGroups(model);
                    VerifyBubbleGeometry();
                    Capture("campaign-level-" + model.Level.ToString("00"));
                    stageCaptured = true;
                    nextTick = EditorApplication.timeSinceStartup + .3;
                }
                else if (!trayCaptured && model.Temp.Count(k => k.HasValue) >= 2)
                {
                    Capture("campaign-tray-level-" + model.Level.ToString("00"));
                    trayCaptured = true;
                    nextTick = EditorApplication.timeSinceStartup + .3;
                }
                else PlaySolutionMove(model);
            }
            else if (step == 14)
            {
                Require(model.Level == 30 && !model.IsWon && model.CompletedOrders == 0, "Final-stage replay stays on level 30 and resets the board");
                LoadLevel(999);
                Require(((CandySortModel)typeof(CandySortGameController).GetField("model", Private).GetValue(controller)).Level == 30, "Out-of-range saved/starting stage clamps to level 30");
                typeof(CandySortGameController).GetMethod("ShowResult", Private).Invoke(controller, new object[] { true });
                step = 15;
                nextTick = EditorApplication.timeSinceStartup + .5;
            }
            else if (step == 15)
            {
                ClickButton("Button Chơi lại từ màn 1");
                Require(((CandySortModel)typeof(CandySortGameController).GetField("model", Private).GetValue(controller)).Level == 1, "Campaign restart returns to tutorial");
                step++;
                nextTick = EditorApplication.timeSinceStartup + .5;
            }
            else
            {
                report += "PASS: live scene, fixed candy positions/sizing, multi-candy hard bubbles, temporary tray, exact undo, normal/hard progression, representative campaign clears (1/5/15/30), campaign completion/save boundary/replay, unlocks, restart, help, narrow portrait.\n";
                File.WriteAllText(OutputPath + "/report.txt", report);
                SessionState.SetBool("CandySortSmokeActive", false);
                testing = false;
                Debug.Log("Candy Sort live smoke test PASSED. Screenshots and report: " + OutputPath);
                EditorApplication.isPlaying = false;
            }
        }
        catch (Exception exception)
        {
            File.WriteAllText(OutputPath + "/report.txt", report + "FAIL: " + exception + "\n");
            Debug.LogException(exception);
            SessionState.SetBool("CandySortSmokeActive", false);
            testing = false;
            EditorApplication.isPlaying = false;
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        if (step != 4) report += "PASS: " + message + "\n";
    }

    private static void VerifyHardBubbleGroups(CandySortModel model)
    {
        Require(model.Bubbles.All(b => b.Candies.Count >= 2 && b.Candies.Count <= 5), "Hard stages start with 2–5 candies per bubble, not singleton blockers");
        Require(model.Bubbles.Any(b => b.Candies.Distinct().Count() > 1), "Hard stages include mixed-flavor bubbles");
        Require(model.Bubbles.Select(b => b.Candies.Count).Distinct().Count() > 1, "Hard stages retain varied bubble group sizes");
    }

    private static void LoadLevel(int level)
    {
        typeof(CandySortGameController).GetMethod("LoadLevel", Private).Invoke(controller, new object[] { level });
    }

    private static void PlaySolutionMove(CandySortModel model)
    {
        var solution = campaignSolutions.levels.First(s => s.level == model.Level);
        Require(solutionMove < solution.moves.Length, "Verified solution has a move for unfinished stage");
        var move = solution.moves[solutionMove];
        if (!CanClickCandy(move.bubbleId, move.candyIndex))
        {
            if (moveWaitStarted == 0) moveWaitStarted = EditorApplication.timeSinceStartup;
            Require(EditorApplication.timeSinceStartup - moveWaitStarted < 8, "Verified move becomes raycast-reachable after settling");
            nextTick = EditorApplication.timeSinceStartup + .1;
            return;
        }
        moveWaitStarted = 0;
        ClickCandy(move.bubbleId, move.candyIndex);
        solutionMove++;
        clicks++;
        nextTick = EditorApplication.timeSinceStartup + .5;
    }

    private static void VerifyFinalPersistence()
    {
        var type = typeof(CandySortGameController);
        var flags = BindingFlags.Static | BindingFlags.NonPublic;
        string progressKey = (string)type.GetField("SaveKey", flags).GetRawConstantValue();
        string completionKey = (string)type.GetField("CompletionKey", flags).GetRawConstantValue();
        bool hadProgress = PlayerPrefs.HasKey(progressKey), hadCompletion = PlayerPrefs.HasKey(completionKey);
        int oldProgress = PlayerPrefs.GetInt(progressKey), oldCompletion = PlayerPrefs.GetInt(completionKey);
        try
        {
            type.GetField("rememberProgress", Private).SetValue(controller, true);
            type.GetMethod("CompleteLevel", Private).Invoke(controller, null);
            Require(PlayerPrefs.GetInt(progressKey) == 30 && PlayerPrefs.GetInt(completionKey) == 1, "Final win persists level 30 plus campaign completion, never level 31");
            type.GetMethod("RestoreProgress", Private).Invoke(controller, null);
            var restored = (CandySortModel)type.GetField("model", Private).GetValue(controller);
            Require(restored.Level == 30 && GameObject.Find("Button CHƠI LẠI MÀN 30") != null, "Saved completion reopens the finale with a replay action");
        }
        finally
        {
            type.GetField("rememberProgress", Private).SetValue(controller, false);
            if (hadProgress) PlayerPrefs.SetInt(progressKey, oldProgress); else PlayerPrefs.DeleteKey(progressKey);
            if (hadCompletion) PlayerPrefs.SetInt(completionKey, oldCompletion); else PlayerPrefs.DeleteKey(completionKey);
            PlayerPrefs.Save();
        }
    }

    private static CandyBubbleLayout GetLayout()
    {
        return (CandyBubbleLayout)typeof(CandySortGameController).GetField("layout", Private).GetValue(controller);
    }

    private static bool SupportsOtherBubble(int id)
    {
        var bodies = GetLayout().Bodies;
        var lower = bodies.First(b => b.Id == id);
        return bodies.Any(b => b.Id != id && b.Position.Y > lower.Position.Y + 25 &&
            Mathf.Abs(Vector2.Distance(new Vector2(lower.Position.X, lower.Position.Y), new Vector2(b.Position.X, b.Position.Y)) - lower.Radius - b.Radius) < 5);
    }

    private static void VerifyBubbleGeometry()
    {
        var bodies = GetLayout().Bodies;
        float halfWidth = controller.GetComponentsInChildren<RectTransform>().First(r => r.name == "Falling bubbles").rect.width * .5f - 4;
        Require(bodies.All(b => Mathf.Abs(b.Position.X) + b.Radius <= halfWidth + .5f && b.Position.Y >= b.Radius - .5f), "Packed circles remain within walls and above the floor");
        float maxOverlap = 0;
        for (int i = 0; i < bodies.Count; i++)
            for (int j = i + 1; j < bodies.Count; j++)
            {
                var a = bodies[i];
                var b = bodies[j];
                float distance = Vector2.Distance(new Vector2(a.Position.X, a.Position.Y), new Vector2(b.Position.X, b.Position.Y));
                maxOverlap = Mathf.Max(maxOverlap, a.Radius + b.Radius - distance);
            }
        Require(maxOverlap <= 1, "Circle contacts do not overlap");
        if (bodies.Count >= 8) Require(bodies.Select(b => Mathf.RoundToInt(b.Position.X / 16)).Distinct().Count() >= 5, "Bubble packing is irregular rather than three straight columns");
        Require(bodies.All(b => GetCandyRects(b.Id).All(c => c.sizeDelta == new Vector2(72, 72))), "All bubble candies have the same normal size, including single-candy bubbles");
    }

    private static bool CanClickCandy(int id, int index)
    {
        var dictionary = (IDictionary)typeof(CandySortGameController).GetField("bubbles", Private).GetValue(controller);
        object view = dictionary[id];
        var candies = (RectTransform)view.GetType().GetField("Candies").GetValue(view);
        var target = candies.GetChild(index).gameObject;
        var data = new PointerEventData(EventSystem.current) { position = RectTransformUtility.WorldToScreenPoint(null, target.transform.position) };
        var hits = new System.Collections.Generic.List<RaycastResult>();
        EventSystem.current.RaycastAll(data, hits);
        return hits.Count > 0 && hits[0].gameObject == target;
    }

    private static RectTransform[] GetCandyRects(int id)
    {
        var dictionary = (IDictionary)typeof(CandySortGameController).GetField("bubbles", Private).GetValue(controller);
        object view = dictionary[id];
        var candies = (RectTransform)view.GetType().GetField("Candies").GetValue(view);
        return Enumerable.Range(0, candies.childCount).Select(i => (RectTransform)candies.GetChild(i))
            .Where(c => c.gameObject.activeSelf).ToArray();
    }

    private static Vector2[] GetCandyOffsets(int id)
    {
        return GetCandyRects(id).Select(c => c.anchoredPosition).ToArray();
    }

    private static void ClickCandy(int id, int index)
    {
        var candies = GetCandyRects(id);
        var offsets = candies.Where((c, i) => i != index).Select(c => c.anchoredPosition).ToArray();
        Click(candies[index].gameObject);
        expectedCandyBubbleId = id;
        expectedCandyOffsets = offsets;
    }

    private static void ClickButton(string name)
    {
        var button = GameObject.Find(name);
        Require(button != null, "Button exists: " + name);
        Click(button);
    }

    private static void Click(GameObject target)
    {
        Canvas.ForceUpdateCanvases();
        var data = new PointerEventData(EventSystem.current)
        {
            button = PointerEventData.InputButton.Left,
            position = RectTransformUtility.WorldToScreenPoint(null, target.transform.position)
        };
        var hits = new System.Collections.Generic.List<RaycastResult>();
        EventSystem.current.RaycastAll(data, hits);
        Require(hits.Count > 0 && hits[0].gameObject == target, "Pointer can reach visible control: " + target.name);
        ExecuteEvents.Execute(target, data, ExecuteEvents.pointerClickHandler);
    }

    private static void Capture(string name)
    {
        ScreenCapture.CaptureScreenshot(Path.GetFullPath(OutputPath + "/" + name + ".png"));
    }

    private static void SetPortraitGameView(int width = 720, int height = 1280)
    {
        var editorAssembly = typeof(Editor).Assembly;
        Type sizesType = editorAssembly.GetType("UnityEditor.GameViewSizes");
        Type singleton = typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
        object sizes = singleton.GetProperty("instance", BindingFlags.Static | BindingFlags.Public).GetValue(null);
        object groupType = sizesType.GetProperty("currentGroupType").GetValue(sizes);
        object group = sizesType.GetMethod("GetGroup").Invoke(sizes, new[] { groupType });
        Type sizeType = editorAssembly.GetType("UnityEditor.GameViewSize");
        Type sizeEnum = editorAssembly.GetType("UnityEditor.GameViewSizeType");
        int count = (int)group.GetType().GetMethod("GetTotalCount").Invoke(group, null);
        int index = -1;
        for (int i = 0; i < count; i++)
        {
            object existing = group.GetType().GetMethod("GetGameViewSize").Invoke(group, new object[] { i });
            if ((int)sizeType.GetProperty("width").GetValue(existing) == width && (int)sizeType.GetProperty("height").GetValue(existing) == height) { index = i; break; }
        }
        if (index < 0)
        {
            object size = Activator.CreateInstance(sizeType, new[] { Enum.ToObject(sizeEnum, 1), (object)width, height, "Candy Sort Portrait" });
            index = count;
            group.GetType().GetMethod("AddCustomSize").Invoke(group, new[] { size });
        }
        var window = EditorWindow.GetWindow(editorAssembly.GetType("UnityEditor.GameView"));
        window.GetType().GetProperty("selectedSizeIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(window, index);
        window.Focus();
    }
}
