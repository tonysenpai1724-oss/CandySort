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
    private const int CatalogCount = 32;
    private const string CandySheetPath = "Assets/CandyAsset/icon.png";
    private static readonly string[] CatalogIds =
    {
        "swirl_lollipop", "purple_jellybean", "lemon_candy", "gummy_bear_red",
        "candy_cane", "peppermint_mint", "orange_wrapped_candy", "blueberry_candy",
        "chocolate_bar", "marshmallow", "rainbow_lollipop", "green_jellybean",
        "chocolate_tart", "gummy_worm", "rock_candy_stick", "strawberry_wrapped_candy",
        "sprinkle_candy_ball", "cotton_candy", "caramel_cube", "gummy_bear_green",
        "caramel_toffee", "peppermint_swirl", "pink_candy_ball", "black_licorice_twist",
        "orange_candy_blue_gum", "striped_lollipop", "chocolate_fudge", "gummy_fish",
        "heart_candy", "red_candy_ball", "wafer_bar", "lemon_wedge"
    };
    private static readonly string[] CatalogNames =
    {
        "Kẹo mút xoắn ốc", "Kẹo đậu thạch tím", "Kẹo chanh", "Kẹo dẻo gấu đỏ",
        "Kẹo gậy sọc đỏ trắng", "Kẹo bạc hà xoắn", "Kẹo cam gói giấy", "Kẹo viên xanh",
        "Thanh sô-cô-la", "Kẹo dẻo trắng", "Kẹo mút cầu vồng", "Kẹo đậu thạch xanh",
        "Bánh tart sô-cô-la", "Kẹo dẻo hình giun", "Kẹo đường kết tinh", "Kẹo dâu gói giấy",
        "Kẹo viên nhiều màu", "Kẹo bông gòn", "Kẹo caramel vuông", "Kẹo dẻo gấu xanh",
        "Kẹo caramel dạng thỏi", "Kẹo bạc hà xoáy", "Kẹo viên hồng", "Kẹo cam thảo xoắn đen",
        "Kẹo cam kèm viên xanh", "Kẹo mút sọc", "Kẹo sô-cô-la mềm", "Kẹo dẻo hình cá",
        "Kẹo hình trái tim", "Kẹo viên đỏ", "Bánh xốp wafer", "Kẹo chanh"
    };
    private static readonly int[] ExpectedFlavorCounts =
    {
        2, 3, 3, 4, 6, 7, 8, 9, 10, 12, 13, 14, 15, 16, 17,
        18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32
    };
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
    private static bool plateSmoke, flavorSmoke, assetSmoke;
    private static RectTransform flavorCatalog;
    private static readonly int[] FlavorSmokeLevels = { 25, 30 };
    private static int[] SmokeLevels => flavorSmoke ? FlavorSmokeLevels : CampaignSmokeLevels;
    private static int plateProbe, completedBeforePlate;
    private static CandyKind plateFlavor;
    private static int[] retainedViewIds;

    [Serializable]
    private sealed class CampaignSolutions { public CampaignSolution[] levels = new CampaignSolution[0]; }
    [Serializable]
    private sealed class CampaignSolution { public int level = 0; public string signature = ""; public CampaignMove[] moves = new CampaignMove[0]; }
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

    [MenuItem("Candy Sort/Run Plate Visual Smoke Test")]
    public static void RequestPlateSmoke()
    {
        File.WriteAllText(RequestPath, "plates");
    }

    [MenuItem("Candy Sort/Run 32 Flavor Smoke Test")]
    public static void Request32FlavorSmoke()
    {
        File.WriteAllText(RequestPath, "flavors");
    }

    [MenuItem("Candy Sort/Run Candy Catalog Smoke Test")]
    public static void RequestCandyCatalogSmoke()
    {
        File.WriteAllText(RequestPath, "assets");
    }

    private static void Tick()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.timeSinceStartup < nextTick) return;
        if (!testing && File.Exists(RequestPath))
        {
            string mode = File.ReadAllText(RequestPath).Trim();
            assetSmoke = mode == "assets" || mode == "catalog";
            plateSmoke = mode == "plates" || assetSmoke;
            flavorSmoke = mode == "flavors";
            File.Delete(RequestPath);
            Directory.CreateDirectory(OutputPath);
            if (EditorApplication.isPlaying || UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)
            {
                File.WriteAllText(OutputPath + "/report.txt", "SKIPPED: stop Play mode and save the active scene before running the smoke test.");
                return;
            }
            testing = true;
            step = 0;
            retainedViewIds = null;
            clicks = 0;
            probeId = -1;
            expectedCandyOffsets = null;
            deferredAction = null;
            report = "Candy Sort: live Unity Play Mode smoke test\n";
            SessionState.SetBool("CandySortSmokeActive", true);
            SessionState.SetBool("CandySortPlateSmoke", plateSmoke);
            SessionState.SetBool("CandySortFlavorSmoke", flavorSmoke);
            SessionState.SetBool("CandySortAssetSmoke", assetSmoke);
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
            plateSmoke = SessionState.GetBool("CandySortPlateSmoke", false);
            flavorSmoke = SessionState.GetBool("CandySortFlavorSmoke", false);
            assetSmoke = SessionState.GetBool("CandySortAssetSmoke", false);
            step = 0;
            retainedViewIds = null;
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
            if (retainedViewIds != null) VerifyRetainedSceneViews();
            if (!plateSmoke && (bool)typeof(CandySortGameController).GetField("busy", Private).GetValue(controller))
            {
                nextTick = EditorApplication.timeSinceStartup + .1;
                return;
            }
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
                VerifyRetainedSceneViews();
                LoadLevel(1);
                var initialViews = RuntimeViewIds("bubbleLayer");
                LoadLevel(1);
                Require(initialViews.SequenceEqual(RuntimeViewIds("bubbleLayer")), "Restart reuses every initial bubble and candy object");
                if (assetSmoke)
                {
                    // Catalog and fallback verification must not depend on a campaign solver trace.
                    VerifyCandyArt();
                    VerifyImportedCandySprites();
                    LoadLevel(CandySortCampaign.LevelCount);
                    VerifyStageFlavorCoverage((CandySortModel)typeof(CandySortGameController).GetField("model", Private).GetValue(controller));
                    ShowCandyCatalog();
                    step = 40;
                    nextTick = EditorApplication.timeSinceStartup + 3;
                    return;
                }
                if (plateSmoke)
                {
                    LoadLevel(1);
                    step = 20;
                    nextTick = EditorApplication.timeSinceStartup + 3;
                    return;
                }
                string solutionsPath = "Temp/CandySortCampaignVerification/solutions.json";
                Require(File.Exists(solutionsPath), "Campaign solutions exist: run dotnet run --project Tests/CandySort/CandySort.Tests.csproj first");
                campaignSolutions = JsonUtility.FromJson<CampaignSolutions>(File.ReadAllText(solutionsPath));
                Require(campaignSolutions.levels.Length == CandySortCampaign.LevelCount, "All 30 campaign stages have verified solution traces");
                foreach (int level in SmokeLevels)
                {
                    string unityPath = "Temp/CandySortCampaignVerification/unity-solutions-level-" + level + ".json";
                    if (!File.Exists(unityPath)) continue;
                    var unitySolution = JsonUtility.FromJson<CampaignSolution>(File.ReadAllText(unityPath));
                    var definition = CandySortCampaign.GetLevel(level);
                    string signature = string.Join(":", new[] { CandySortModel.CandyKindCount, 644, 662, definition.Seed,
                        definition.OrderCount, definition.FlavorCount, definition.ActivePlates, definition.MinBubbleSize,
                        definition.MaxBubbleSize, definition.Mixing, definition.OpeningTrayCandies });
                    Require(unitySolution.level == level && unitySolution.signature == signature &&
                        unitySolution.moves.Length == definition.OrderCount * 3, "Unity-generated trace matches current stage definition: " + level);
                    campaignSolutions.levels[Array.FindIndex(campaignSolutions.levels, s => s.level == level)] = unitySolution;
                }
                solutionMove = 0;
                moveWaitStarted = 0;
                if (flavorSmoke)
                {
                    VerifyCandyArt();
                    VerifyImportedCandySprites();
                    LoadLevel(FlavorSmokeLevels[0]);
                    ShowCandyCatalog();
                    step = 40;
                }
                else
                {
                    LoadLevel(1);
                    step++;
                }
                nextTick = EditorApplication.timeSinceStartup + 3;
            }
            else if (plateSmoke && step >= 20 && step < 40)
            {
                TickPlateSmoke(model);
            }
            else if (assetSmoke && step == 40)
            {
                VerifyCandyCatalogVisuals();
                Capture("assets-all-32");
                deferredAction = () => UnityEngine.Object.Destroy(flavorCatalog.gameObject);
                step = 41;
                nextTick = EditorApplication.timeSinceStartup + .8;
            }
            else if (assetSmoke && step == 41)
            {
                VerifyBubbleGeometry();
                foreach (var bubble in model.Bubbles)
                {
                    var candies = GetCandyRects(bubble.Id);
                    for (int i = 0; i < candies.Length; i++)
                        Require(candies[i].GetComponent<Image>().sprite == CandySortArt.Candy(bubble.Candies[i]),
                            "Board candy uses assigned artwork");
                }
                Capture("assets-board-30");
                var trayBubble = model.Bubbles.First(b => Enumerable.Range(0, b.Candies.Count)
                    .Any(i => !model.Plates.Any(p => p.Kind == b.Candies[i]) && CanClickCandy(b.Id, i)));
                int index = Enumerable.Range(0, trayBubble.Candies.Count)
                    .First(i => !model.Plates.Any(p => p.Kind == trayBubble.Candies[i]) && CanClickCandy(trayBubble.Id, i));
                deferredAction = () => ClickCandy(trayBubble.Id, index);
                step = 42;
                nextTick = EditorApplication.timeSinceStartup + .8;
            }
            else if (assetSmoke && step == 42)
            {
                int slot = Array.FindIndex(model.Temp, k => k.HasValue);
                Require(slot >= 0, "Imported candy routes into the temporary tray");
                var image = GameObject.Find("Temp slot " + slot).GetComponentsInChildren<Image>()
                    .First(i => i.GetComponent<Button>() != null);
                Require(image.sprite == CandySortArt.Candy(model.Temp[slot].Value) && image.preserveAspect,
                    "Temporary tray uses the assigned sprite without stretching");
                Capture("assets-temp");
                deferredAction = () => LoadLevel(1);
                step = 20;
                nextTick = EditorApplication.timeSinceStartup + .8;
            }
            else if (flavorSmoke && step == 40)
            {
                VerifyCandyCatalogVisuals();
                Capture("flavors-all-32");
                deferredAction = () =>
                {
                    UnityEngine.Object.Destroy(flavorCatalog.gameObject);
                    campaignIndex = solutionMove = 0;
                    stageCaptured = trayCaptured = false;
                    moveWaitStarted = 0;
                };
                step = 13;
                nextTick = EditorApplication.timeSinceStartup + .8;
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
                LoadLevel(SmokeLevels[campaignIndex]);
                step = 13;
                nextTick = EditorApplication.timeSinceStartup + 3;
            }
            else if (step == 13)
            {
                Require(model.Level == SmokeLevels[campaignIndex], "Representative campaign stage loaded: " + model.Level);
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
                        LoadLevel(SmokeLevels[campaignIndex]);
                        solutionMove = 0;
                        stageCaptured = trayCaptured = false;
                    }
                    nextTick = EditorApplication.timeSinceStartup + 3;
                }
                else if (!stageCaptured)
                {
                    if (model.Definition.IsHard) VerifyHardBubbleGroups(model);
                    if (flavorSmoke) VerifyStageFlavorCoverage(model);
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
                report += flavorSmoke
                    ? "PASS: 32-flavor art/catalog metadata/legacy sprite overrides, imported sprite identities, actual UI clears (25: 27 flavors / 30: 32 flavors), fixed candy positions/sizing, multi-candy hard bubbles, tray routing, campaign completion/save boundary/replay.\n"
                    : "PASS: live scene, fixed candy positions/sizing, multi-candy hard bubbles, temporary tray, exact undo, normal/hard progression, representative campaign clears (1/5/15/30), campaign completion/save boundary/replay, unlocks, restart, help, narrow portrait.\n";
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

    private static void VerifyCandyCatalogMetadata()
    {
        Require(CandySortModel.CandyKindCount == CatalogCount && Enum.GetValues(typeof(CandyKind)).Length == CatalogCount,
            "Exactly 32 real candy kinds exist");
        Require(CandySortArt.Ids.SequenceEqual(CatalogIds) && CandySortArt.Ids.Distinct().Count() == CatalogCount,
            "All 32 stable candy IDs match the approved row-major catalog");
        // Display names need not be unique: lemon_candy and lemon_wedge are both Kẹo chanh.
        Require(CandySortArt.Names.SequenceEqual(CatalogNames), "All 32 Vietnamese labels match the approved catalog, including both Kẹo chanh entries");
        for (int i = 0; i < CatalogCount; i++)
        {
            string enumName = string.Concat(CatalogIds[i].Split('_').Select(part => char.ToUpperInvariant(part[0]) + part.Substring(1)));
            Require(Enum.GetName(typeof(CandyKind), i) == enumName, "Candy index " + i + " identifies " + CatalogIds[i]);
        }
        Require(CandySortArt.Colors.Length == CatalogCount && CandySortArt.Colors.Distinct().Count() == CatalogCount,
            "32 distinct fallback candy colors exist");
        Require(CandySortCampaign.LevelCount == ExpectedFlavorCounts.Length &&
            Enumerable.Range(1, CandySortCampaign.LevelCount).Select(level => CandySortCampaign.GetLevel(level).FlavorCount)
                .SequenceEqual(ExpectedFlavorCounts), "Approved 30-stage flavor schedule reaches all 32 only at stage 30");
    }

    private static void VerifyImportedCandySprites()
    {
        var sprites = (Sprite[])typeof(CandySortGameController).GetField("candySprites", Private).GetValue(controller);
        Require(sprites != null && sprites.Length == CatalogCount && sprites.All(s => s != null) && sprites.Distinct().Count() == CatalogCount,
            "All 32 candy kinds have distinct assigned imported sprites");
        var imported = AssetDatabase.LoadAllAssetsAtPath(CandySheetPath).OfType<Sprite>().ToArray();
        Require(imported.Length == CatalogCount, "Source candy sheet contains exactly 32 sliced sprites");
        for (int i = 0; i < CatalogCount; i++)
        {
            string spriteName = imported.Single(s => s.name.EndsWith("_" + i, StringComparison.Ordinal)).name;
            Require(AssetDatabase.GetAssetPath(sprites[i]) == CandySheetPath && sprites[i].name == spriteName,
                "Candy " + (i + 1).ToString("00") + " / " + CatalogIds[i] + " / " + CatalogNames[i] + " maps to " + CandySheetPath + "#" + spriteName);
            Require(imported.Single(s => s.name == spriteName) == sprites[i] && CandySortArt.Candy((CandyKind)i) == sprites[i],
                "Imported subasset and runtime sprite identities agree for " + CatalogIds[i]);
        }
    }

    private static void VerifyStageFlavorCoverage(CandySortModel model)
    {
        int expected = ExpectedFlavorCounts[model.Level - 1];
        Require(model.Definition.FlavorCount == expected && model.Bubbles.SelectMany(b => b.Candies)
            .Select(k => (int)k).Distinct().OrderBy(k => k).SequenceEqual(Enumerable.Range(0, expected)),
            "Stage " + model.Level + " board contains every unlocked candy in its " + expected + "-kind prefix pool");
        Require((expected == CatalogCount) == (model.Level == CandySortCampaign.LevelCount),
            "All 32 candy kinds unlock only in the finale");
    }

    private static void VerifyCandyArt()
    {
        VerifyCandyCatalogMetadata();
        var saved = (Sprite[])typeof(CandySortGameController).GetField("candySprites", Private).GetValue(controller);
        Require(saved.Length == CatalogCount, "Inspector offers 32 optional sprite slots");
        try
        {
            CandySortArt.SetCandySprites(null);
            var sprites = Enumerable.Range(0, CatalogCount).Select(i => CandySortArt.Candy((CandyKind)i)).ToArray();
            Require(sprites.All(s => s != null) && sprites.Distinct().Count() == CatalogCount, "Every candy kind has a distinct generated fallback sprite");
            var pixels = sprites.Select(s => s.texture.GetPixels32()).ToArray();
            Require(pixels.All(p => p.Any(c => c.a > 0)), "All 32 generated fallback sprites contain visible pixels");
            for (int i = 0; i < pixels.Length; i++)
                for (int j = 0; j < i; j++)
                    Require(!pixels[i].SequenceEqual(pixels[j]), "Generated candy art differs: " + CatalogIds[i] + " / " + CatalogIds[j]);
            foreach (int length in new[] { 6, 10 })
            {
                var legacy = new Sprite[length];
                legacy[0] = CandySortArt.Circle;
                CandySortArt.SetCandySprites(legacy);
                Require(CandySortArt.Candy(CandyKind.SwirlLollipop) == legacy[0], "Partial " + length + "-entry sprite override preserves index zero");
                Require(Enumerable.Range(1, CatalogCount - 1).All(i => CandySortArt.Candy((CandyKind)i) == sprites[i]),
                    "Partial " + length + "-entry overrides safely fall back for every missing or null slot");
            }
            var expanded = new Sprite[CatalogCount];
            expanded[CatalogCount - 1] = CandySortArt.Rounded;
            CandySortArt.SetCandySprites(expanded);
            Require(CandySortArt.Candy(CandyKind.LemonWedge) == expanded[CatalogCount - 1], "Final candy sprite override index 31 works");
            Require(Enumerable.Range(0, CatalogCount - 1).All(i => CandySortArt.Candy((CandyKind)i) == sprites[i]),
                "Full-length override preserves fallback artwork in all null slots");
        }
        finally { CandySortArt.SetCandySprites(saved); }
    }

    private static void ShowCandyCatalog()
    {
        var type = typeof(CandySortGameController);
        var parent = (RectTransform)type.GetField("root", Private).GetValue(controller);
        const int columns = 4;
        int rows = (CatalogCount + columns - 1) / columns;
        float width = Mathf.Min(696, parent.rect.width - 24);
        float height = Mathf.Min(1248, parent.rect.height - 24);
        float cellWidth = (width - 16) / columns;
        float rowHeight = (height - 80) / rows;
        flavorCatalog = (RectTransform)type.GetMethod("Panel", Private).Invoke(controller, new object[]
        {
            parent, "32 candy catalog", Vector2.zero, new Vector2(width, height), CandySortArt.Hex("103B56"), true
        });
        flavorCatalog.anchorMin = flavorCatalog.anchorMax = new Vector2(.5f, .5f);
        type.GetMethod("Label", Private).Invoke(controller, new object[]
        {
            flavorCatalog, "32 LOẠI KẸO", new Vector2(0, -height * .5f + 29), new Vector2(width - 32, 40), 26, CandySortArt.Ink, FontStyle.Bold, true
        });
        for (int i = 0; i < CatalogCount; i++)
        {
            float x = (i % columns - (columns - 1) * .5f) * cellWidth;
            float top = -height * .5f + 64 + i / columns * rowHeight;
            var image = (Image)type.GetMethod("Image", Private).Invoke(controller, new object[]
            {
                flavorCatalog, "Catalog candy " + CatalogIds[i], new Vector2(x, top + 33), new Vector2(64, 64), Color.white, CandySortArt.Candy((CandyKind)i)
            });
            image.preserveAspect = true;
            image.rectTransform.anchorMin = image.rectTransform.anchorMax = new Vector2(.5f, .5f);
            var label = (Text)type.GetMethod("Label", Private).Invoke(controller, new object[]
            {
                flavorCatalog, (i + 1).ToString("00") + " · " + CatalogNames[i], new Vector2(x, top + 101),
                new Vector2(cellWidth - 8, rowHeight - 70), 18, CandySortArt.Ink, FontStyle.Bold, true
            });
            label.name = "Catalog label " + CatalogIds[i];
        }
    }

    private static void VerifyCandyCatalogVisuals()
    {
        Canvas.ForceUpdateCanvases();
        var corners = new Vector3[4];
        flavorCatalog.GetWorldCorners(corners);
        Require(corners[0].x >= -1 && corners[0].y >= -1 && corners[2].x <= Screen.width + 1 && corners[2].y <= Screen.height + 1,
            "All-32 candy catalog fits the portrait screenshot");
        var images = flavorCatalog.GetComponentsInChildren<Image>().Where(image => image.name.StartsWith("Catalog candy ", StringComparison.Ordinal)).ToArray();
        var labels = flavorCatalog.GetComponentsInChildren<Text>().Where(label => label.name.StartsWith("Catalog label ", StringComparison.Ordinal)).ToArray();
        Require(images.Length == CatalogCount && labels.Length == CatalogCount, "Catalog shows all 32 sprite/name pairs in a four-column, eight-row grid");
        for (int i = 0; i < CatalogCount; i++)
        {
            var image = images.Single(item => item.name == "Catalog candy " + CatalogIds[i]);
            var label = labels.Single(item => item.name == "Catalog label " + CatalogIds[i]);
            Require(image.sprite == CandySortArt.Candy((CandyKind)i) && image.preserveAspect &&
                label.text == (i + 1).ToString("00") + " · " + CatalogNames[i] && label.preferredHeight <= label.rectTransform.rect.height + 1,
                "Catalog sprite and readable, untruncated label match " + CatalogIds[i]);
        }
    }

    private static void TickPlateSmoke(CandySortModel model)
    {
        if (step == 20)
        {
            var bubble = model.Bubbles.First(b => b.Candies.Count == 3 &&
                b.Candies.All(k => k == b.Candies[0]) && model.Plates.Any(p => p.Kind == b.Candies[0]) &&
                Enumerable.Range(0, b.Candies.Count).All(i => CanClickCandy(b.Id, i)));
            probeId = bubble.Id;
            plateFlavor = bubble.Candies[0];
            plateProbe = model.Plates.ToList().FindIndex(p => p.Kind == plateFlavor);
            completedBeforePlate = model.CompletedOrders;
            VerifyPlateCandies(0);
            Capture("plate-00-empty");
            deferredAction = () => ClickCandy(probeId, 0);
            step = 21;
        }
        else if (step == 21 || step == 22)
        {
            int count = step - 20;
            Require(model.Plates[plateProbe].Count == count, "Matching candy fills plate slot " + count);
            VerifyPlateCandies(count);
            Capture("plate-0" + count + "-filled");
            if (count == 1) deferredAction = () => ClickCandy(probeId, 0);
            step++;
        }
        else if (step == 23)
        {
            ClickCandy(probeId, 0);
            step = 24;
            nextTick = EditorApplication.timeSinceStartup + .36;
            return;
        }
        else if (step == 24)
        {
            Require((bool)typeof(CandySortGameController).GetField("busy", Private).GetValue(controller), "Completed set is held visibly before order replacement");
            VerifyPlateCandies(3);
            Capture("plate-03-complete");
            step = 25;
        }
        else if (step == 25)
        {
            Require(model.CompletedOrders == completedBeforePlate + 1, "Three matching candies complete exactly one order");
            VerifyPlateCandies(model.Plates[plateProbe].Count);
            Capture("plate-04-next-order");
            deferredAction = () => ClickButton("Button HOÀN TÁC");
            step = 26;
        }
        else if (step >= 26 && step <= 28)
        {
            int count = 28 - step;
            Require(model.CompletedOrders == completedBeforePlate && model.Plates[plateProbe].Kind == plateFlavor && model.Plates[plateProbe].Count == count, "Undo restores plate candy count " + count);
            VerifyPlateCandies(count);
            if (count > 0) deferredAction = () => ClickButton("Button HOÀN TÁC");
            step++;
        }
        else
        {
            if (assetSmoke) report += "PASS: all 32 approved candy IDs/names and row-major imported sprite identities, readable catalog screenshot, stage-30 board artwork and temporary-tray routing; asset smoke does not certify campaign solvability.\n";
            report += "PASS: plate flavor icon and exact 0/3–3/3 quantity below dish without candy-name labels, visible 0/1/2/3 actual candies, completed triple before replacement, exact visual undo.\n";
            File.WriteAllText(OutputPath + "/report.txt", report);
            SessionState.SetBool("CandySortSmokeActive", false);
            testing = false;
            EditorApplication.isPlaying = false;
        }
        nextTick = EditorApplication.timeSinceStartup + .8;
    }

    private static int[] RuntimeViewIds(params string[] fields)
    {
        return fields.SelectMany(field => ((RectTransform)typeof(CandySortGameController).GetField(field, Private).GetValue(controller))
            .GetComponentsInChildren<Transform>(true)).Select(t => t.GetInstanceID()).OrderBy(id => id).ToArray();
    }

    private static void VerifyRetainedSceneViews()
    {
        var scene = controller.gameObject.scene;
        Require(UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None).Count(c => c.gameObject.scene == scene) == 1,
            "Play mode retains one authored Canvas without duplication");
        Require(UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None).Count(c => c.gameObject.scene == scene) == 1 &&
            UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None).Count(c => c.gameObject.scene == scene) == 1,
            "Play mode retains one camera and EventSystem");
        var current = RuntimeViewIds("plateLayer", "tempLayer");
        if (retainedViewIds == null) retainedViewIds = current;
        Require(retainedViewIds.SequenceEqual(current), "Plate and tray objects are reused across collection, level changes and undo");
    }

    private static void VerifyPlateCandies(int count)
    {
        var tile = (RectTransform)GameObject.Find("Plate " + (plateProbe + 1)).transform;
        var images = tile.GetComponentsInChildren<Image>();
        var dish = images.First(i => i.name == "Porcelain dish").rectTransform;
        var sample = images.First(i => i.name == "Flavor sample").rectTransform;
        Require(sample.anchoredPosition.y + sample.rect.height * .5f < dish.anchoredPosition.y - dish.rect.height * .5f, "Flavor icon sits below the dish");
        var texts = tile.GetComponentsInChildren<Text>();
        var quantity = texts.Single(text => text.name == "Plate count");
        Require(quantity.text == count + "/3", "Plate quantity reads exactly " + count + "/3, including collection and undo");
        Require(quantity.rectTransform.anchoredPosition.y + quantity.rectTransform.rect.height * .5f <
            dish.anchoredPosition.y - dish.rect.height * .5f, "Plate quantity sits below the dish beside the flavor icon");
        Require(texts.All(text => !CatalogNames.Contains(text.text)), "Plate displays no candy-name label, only its flavor icon and quantity");
        var candies = images.Where(i => i.name.StartsWith("Plate candy ")).ToArray();
        Require(candies.Length == count, "Plate displays " + count + " actual candies");
        foreach (var candy in candies)
        {
            Require(candy.sprite == sample.GetComponent<Image>().sprite, "Plate candy uses its assigned flavor sprite");
            var rect = candy.rectTransform;
            Require(Mathf.Abs(rect.anchoredPosition.x) + rect.rect.width * .5f <= dish.rect.width * .5f &&
                Mathf.Abs(rect.anchoredPosition.y - dish.anchoredPosition.y) + rect.rect.height * .5f <= dish.rect.height * .5f,
                "Collected candy stays inside the dish");
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
            if (EditorApplication.timeSinceStartup - moveWaitStarted >= 8)
            {
                var target = GetCandyRects(move.bubbleId)[move.candyIndex];
                var point = RectTransformUtility.WorldToScreenPoint(null, target.position);
                var hits = new System.Collections.Generic.List<RaycastResult>();
                EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = point }, hits);
                var body = GetLayout().Bodies.First(b => b.Id == move.bubbleId);
                throw new InvalidOperationException("Verified move is not raycast-reachable: level=" + model.Level +
                    " move=" + solutionMove + " bubble=" + move.bubbleId + " index=" + move.candyIndex +
                    " candy=" + target.name + " screen=" + point + " offset=" + target.anchoredPosition +
                    " body=" + body.Position.X + "," + body.Position.Y + " radius=" + body.Radius +
                    " uiBubble=" + ((RectTransform)target.parent.parent).anchoredPosition +
                    " hits=" + string.Join(",", hits.Select(h => h.gameObject.name).ToArray()) +
                    " result=" + (GameObject.Find("Result card") != null));
            }
            nextTick = EditorApplication.timeSinceStartup + .1;
            return;
        }
        moveWaitStarted = 0;
        ClickCandy(move.bubbleId, move.candyIndex);
        solutionMove++;
        clicks++;
        nextTick = EditorApplication.timeSinceStartup + .1;
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
        var candies = GetCandyRects(id);
        return index >= 0 && index < candies.Length && TryGetClickPoint(candies[index].gameObject, out _);
    }

    private static bool TryGetClickPoint(GameObject target, out Vector2 point)
    {
        Canvas.ForceUpdateCanvases();
        var rect = (RectTransform)target.transform;
        var hits = new System.Collections.Generic.List<RaycastResult>();
        // A candy clipped by the chamber edge can still be tapped on its visible portion.
        float[] fractions = { 0, -.2f, .2f, -.4f, .4f };
        foreach (float y in fractions)
            foreach (float x in fractions)
            {
                var local = rect.rect.center + new Vector2(rect.rect.width * x, rect.rect.height * y);
                point = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(local));
                hits.Clear();
                EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = point }, hits);
                if (hits.Count > 0 && hits[0].gameObject == target) return true;
            }
        point = Vector2.zero;
        return false;
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
        Require(TryGetClickPoint(target, out Vector2 point), "Pointer can reach visible control: " + target.name);
        var data = new PointerEventData(EventSystem.current)
        {
            button = PointerEventData.InputButton.Left,
            position = point
        };
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
