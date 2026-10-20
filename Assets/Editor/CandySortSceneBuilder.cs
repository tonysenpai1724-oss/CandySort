using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CandySort;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Deliberately explicit entry points only: no editor startup or update subscriptions.
public static class CandySortSceneBuilder
{
    private const string ScenePath = "Assets/Scenes/CandySortGame.unity";
    private const string InterfaceFolder = "Assets/CandySortUI";
    private const string CandySheetPath = "Assets/CandyAsset/icon.png";
    private const string ReportPath = "Temp/CandySortVerification/scene-build-report.txt";
    private static readonly string[] InterfaceNames = { "Rounded", "Circle", "Ocean", "Bubble", "Ring" };

    [MenuItem("Candy Sort/Build or Refresh Scene")]
    public static void BuildScene()
    {
        var controller = BuildSceneCore();
        if (controller != null)
            Debug.Log("Candy Sort authored scene saved: " + ScenePath + ". Preview is stage 1; gameplay progress is unchanged.", controller);
    }

    [MenuItem("Candy Sort/Verify Authored Scene")]
    public static void VerifyAuthoredScene()
    {
        var report = new StringBuilder("Candy Sort authored-scene verification\n");
        try
        {
            RequireEditMode();
            // Read before either build, so source importer/slice metadata changes cannot go unnoticed.
            byte[] sourceBytes = File.ReadAllBytes(Absolute(CandySheetPath));
            byte[] sourceMetadata = File.ReadAllBytes(Absolute(CandySheetPath + ".meta"));
            var controller = BuildSceneCore();
            if (controller == null)
            {
                report.AppendLine("SKIPPED: scene switch was cancelled. No build performed.");
                WriteReport(report);
                return;
            }

            Scene scene = controller.gameObject.scene;
            VerifyScene(scene, controller, report);
            var firstObjects = SnapshotObjects(scene);
            var firstAssets = SnapshotInterfaceAssets();
            report.AppendLine("First build: " + SceneObjects<GameObject>(scene).Length + " GameObjects, " + firstObjects.Count + " total object/component identities.");

            var secondController = BuildSceneCore();
            Require(secondController == controller, "Refresh preserves the controller instance", report);
            var secondObjects = SnapshotObjects(scene);
            Require(firstObjects.Count == secondObjects.Count && firstObjects.All(pair =>
                secondObjects.TryGetValue(pair.Key, out string value) && value == pair.Value),
                "Two builds preserve every scene GameObject/component identity, hierarchy and count", report);
            Require(firstAssets.SequenceEqual(SnapshotInterfaceAssets()),
                "Refresh preserves interface asset GUIDs, bytes and modification times (no redundant writes/reimports)", report);
            Require(sourceBytes.SequenceEqual(File.ReadAllBytes(Absolute(CandySheetPath))) &&
                sourceMetadata.SequenceEqual(File.ReadAllBytes(Absolute(CandySheetPath + ".meta"))),
                "Candy source texture and slicing metadata remain byte-for-byte unchanged", report);
            VerifyScene(scene, secondController, report);
            Require(!scene.isDirty, "Authored scene is saved", report);
            report.AppendLine("PASS: explicit edit-mode build and idempotence checks completed. No Play mode or PlayerPrefs access.");
            WriteReport(report);
            Debug.Log("Candy Sort authored scene verification PASSED. Report: " + ReportPath, controller);
        }
        catch (Exception exception)
        {
            report.AppendLine("FAIL: " + exception);
            WriteReport(report);
            // Also lets -executeMethod callers receive a failing Unity invocation.
            throw;
        }
    }

    [MenuItem("Candy Sort/Validate Saved Scene")]
    public static void ValidateSavedScene()
    {
        var report = new StringBuilder("Candy Sort saved-scene validation (no rebuilding)\n");
        try
        {
            RequireEditMode();
            Scene scene = TargetScene();
            if (!scene.IsValid())
            {
                report.AppendLine("SKIPPED: scene switch was cancelled. No validation performed.");
                WriteReport(report);
                return;
            }
            Require(!scene.isDirty, "Scene has no unsaved edits; validation does not build or save", report);
            var controllers = SceneObjects<CandySortGameController>(scene);
            Require(controllers.Length == 1, "Saved scene has exactly one controller", report);
            VerifyScene(scene, controllers[0], report, false);
            report.AppendLine("PASS: saved authored hierarchy, serialized view references and persistent sprites validated without rebuilding.");
            WriteReport(report);
            Debug.Log("Candy Sort saved scene validation PASSED. Report: " + ReportPath, controllers[0]);
        }
        catch (Exception exception)
        {
            report.AppendLine("FAIL: " + exception);
            WriteReport(report);
            throw;
        }
    }

    private static Scene TargetScene()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return default;
            scene = SceneManager.GetSceneByPath(ScenePath);
            if (!scene.IsValid() || !scene.isLoaded)
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            else if (!SceneManager.SetActiveScene(scene))
                throw new InvalidOperationException("Could not activate the loaded Candy Sort scene.");
        }
        return scene;
    }

    private static CandySortGameController BuildSceneCore()
    {
        RequireEditMode();
        Scene scene = TargetScene();
        if (!scene.IsValid()) return null;
        var controllers = SceneObjects<CandySortGameController>(scene);
        if (controllers.Length > 1)
            throw new InvalidOperationException("Candy Sort scene has multiple controllers. Resolve duplicates explicitly; the builder does not delete scene objects.");
        // Validate the existing source slices before creating or changing scene content.
        Sprite[] candies = LoadCandySprites();
        Sprite[] interfaces = PersistInterfaceSprites();
        CandySortGameController controller;
        if (controllers.Length == 0)
        {
            var game = new GameObject("Candy Sort Game");
            SceneManager.MoveGameObjectToScene(game, scene);
            Undo.RegisterCreatedObjectUndo(game, "Create Candy Sort controller");
            controller = Undo.AddComponent<CandySortGameController>(game);
        }
        else controller = controllers[0];

        var serialized = new SerializedObject(controller);
        SerializedProperty candySprites = serialized.FindProperty("candySprites");
        if (candySprites == null || !candySprites.isArray)
            throw new InvalidOperationException("Controller must expose its serialized candySprites array.");
        candySprites.arraySize = candies.Length;
        for (int i = 0; i < candies.Length; i++)
            candySprites.GetArrayElementAtIndex(i).objectReferenceValue = candies[i];
        serialized.ApplyModifiedPropertiesWithoutUndo();

        controller.BuildScenePreview(interfaces);
        EditorUtility.SetDirty(controller);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene, ScenePath))
            throw new IOException("Could not save the authored Candy Sort scene: " + ScenePath);
        SceneView.RepaintAll();
        return controller;
    }

    private static Sprite[] LoadCandySprites()
    {
        var imported = AssetDatabase.LoadAllAssetsAtPath(CandySheetPath).OfType<Sprite>().ToArray();
        // Imported slice names retain their original prefix, not necessarily the texture filename.
        if (imported.Length != 32)
            throw new InvalidOperationException("Expected exactly 32 existing sliced candy sprites at " + CandySheetPath + "; the builder never changes the source importer.");
        var ordered = new Sprite[32];
        for (int i = 0; i < ordered.Length; i++)
        {
            var matches = imported.Where(sprite => sprite.name.EndsWith("_" + i, StringComparison.Ordinal)).ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException("Missing or duplicate candy slice suffix _" + i + " at " + CandySheetPath);
            ordered[i] = matches[0];
        }
        return ordered;
    }

    private static Sprite[] PersistInterfaceSprites()
    {
        if (!AssetDatabase.IsValidFolder(InterfaceFolder))
            AssetDatabase.CreateFolder("Assets", "CandySortUI");
        // Never accidentally export an imported override instead of the procedural originals.
        CandySortArt.SetInterfaceSprites(null);
        Sprite[] generated = { CandySortArt.Rounded, CandySortArt.Circle, CandySortArt.Ocean, CandySortArt.Bubble, CandySortArt.Ring };
        var imported = new Sprite[generated.Length];
        for (int i = 0; i < generated.Length; i++)
        {
            string assetPath = InterfaceFolder + "/" + InterfaceNames[i] + ".png";
            byte[] bytes = generated[i].texture.EncodeToPNG();
            if (bytes == null || bytes.Length == 0)
                throw new InvalidOperationException("Could not encode procedural sprite " + InterfaceNames[i]);
            string fullPath = Absolute(assetPath);
            bool changed = !File.Exists(fullPath) || !File.ReadAllBytes(fullPath).SequenceEqual(bytes);
            if (changed)
            {
                // Overwrite only PNG bytes, never delete/recreate the asset or its GUID-bearing .meta.
                File.WriteAllBytes(fullPath, bytes);
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            }
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
                throw new InvalidOperationException("Expected a TextureImporter at " + assetPath);
            Vector4 border = i == 0 ? Vector4.one * 26 : Vector4.zero;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            if (importer.textureType != TextureImporterType.Sprite || importer.spriteImportMode != SpriteImportMode.Single ||
                settings.spriteMeshType != SpriteMeshType.FullRect || importer.spriteBorder != border ||
                importer.spritePixelsPerUnit != 100 || importer.mipmapEnabled || !importer.alphaIsTransparency ||
                importer.textureCompression != TextureImporterCompression.Uncompressed || importer.filterMode != FilterMode.Bilinear)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spriteBorder = border;
                importer.spritePixelsPerUnit = 100;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.filterMode = FilterMode.Bilinear;
                importer.ReadTextureSettings(settings);
                settings.spriteMeshType = SpriteMeshType.FullRect;
                importer.SetTextureSettings(settings);
                importer.SaveAndReimport();
            }
            imported[i] = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            if (imported[i] == null || !AssetDatabase.Contains(imported[i]))
                throw new InvalidOperationException("Persistent interface sprite was not imported: " + assetPath);
        }
        CandySortArt.SetInterfaceSprites(imported);
        return imported;
    }

    private static void VerifyScene(Scene scene, CandySortGameController controller, StringBuilder report, bool generatedPreview = true)
    {
        Require(SceneObjects<CandySortGameController>(scene).Length == 1, "Exactly one scene controller", report);
        Require(SceneObjects<Canvas>(scene).Length == 1, "Exactly one canvas (including inactive objects)", report);
        Require(SceneObjects<Camera>(scene).Length == 1, "Exactly one camera (including inactive objects)", report);
        Require(SceneObjects<EventSystem>(scene).Length == 1, "Exactly one EventSystem (including inactive objects)", report);
        Require(SceneObjects<UnityEngine.UI.Text>(scene).Length == 0,
            "No legacy UI Text components remain in the authored scene (including inactive objects)", report);
        Require(controller.GetComponentsInChildren<TextMeshProUGUI>(true).Any(text => text.text.StartsWith("MÀN 01", StringComparison.Ordinal)),
            "TMP edit preview displays stage 1", report);

        var bubbleLayers = controller.GetComponentsInChildren<Transform>(true).Where(t => t.name == "Falling bubbles").ToArray();
        Require(bubbleLayers.Length == 1, "Exactly one authored bubble layer", report);
        var buttons = bubbleLayers[0].GetComponentsInChildren<Button>(true);
        var candies = LoadCandySprites();
        Require(buttons.Length == 30, "Stage 1 tutorial contains exactly 30 candy buttons", report);
        Require(buttons.All(button => button.GetComponent<Image>() != null && button.GetComponent<Image>().preserveAspect &&
            candies.Contains(button.GetComponent<Image>().sprite)), "Every board candy uses an imported sheet slice with preserved aspect", report);

        var serialized = new SerializedObject(controller);
        string[] retainedFields = { "sceneCamera", "root", "plateLayer", "tempLayer", "board", "bubbleLayer", "modalLayer", "effectLayer",
            "levelLabel", "progressLabel", "queueLabel", "progressFill", "restartButton", "undoButton" };
        Require(retainedFields.All(name => serialized.FindProperty(name) != null && serialized.FindProperty(name).objectReferenceValue != null),
            "Retained camera, rect, text, image and button fields are serialized and assigned", report);
        Require(new[] { "levelLabel", "progressLabel", "queueLabel" }.All(name =>
            serialized.FindProperty(name).objectReferenceValue is TextMeshProUGUI),
            "Required HUD labels reference TextMeshProUGUI components", report);
        foreach (string name in new[] { "difficultyLabel", "hintLabel" })
        {
            var caption = serialized.FindProperty(name).objectReferenceValue;
            Require(caption == null || caption is TextMeshProUGUI,
                "Optional HUD label " + name + " uses TMP when assigned", report);
        }
        var helpButton = serialized.FindProperty("helpButton").objectReferenceValue;
        Require(helpButton == null || helpButton is Button, "Optional help button is valid when assigned", report);
        var interfaceSprites = serialized.FindProperty("interfaceSprites");
        Require(interfaceSprites != null && interfaceSprites.arraySize == InterfaceNames.Length &&
            Enumerable.Range(0, InterfaceNames.Length).All(i => interfaceSprites.GetArrayElementAtIndex(i).objectReferenceValue ==
                AssetDatabase.LoadAssetAtPath<Sprite>(InterfaceFolder + "/" + InterfaceNames[i] + ".png")),
            "Five interface sprites are retained in Rounded/Circle/Ocean/Bubble/Ring order", report);
        var plateViews = serialized.FindProperty("plateViews");
        var tempViews = serialized.FindProperty("tempViews");
        Require(plateViews != null && plateViews.arraySize == 4 && tempViews != null && tempViews.arraySize == 5,
            "Serialized views retain four plates and five temporary tray slots", report);
        var references = serialized.GetIterator();
        int referenceCount = 0;
        while (references.NextVisible(true))
        {
            if (references.propertyType != SerializedPropertyType.ObjectReference) continue;
            if (references.objectReferenceValue == null)
            {
                string path = references.propertyPath;
                bool optionalPlateDecoration = path.StartsWith("plateViews.Array.data[", StringComparison.Ordinal) &&
                    new[] { ".Ring", ".LockIcon", ".Locked", ".Unlock", ".Served" }.Any(path.EndsWith);
                bool optionalTrayLabel = path.StartsWith("tempViews.Array.data[", StringComparison.Ordinal) && path.EndsWith(".Empty", StringComparison.Ordinal);
                bool optionalControllerView = path == "difficultyLabel" || path == "hintLabel" || path == "helpButton";
                if (optionalPlateDecoration || optionalTrayLabel || optionalControllerView) continue;
                throw new InvalidOperationException("Missing retained serialized reference: " + path);
            }
            if (references.objectReferenceValue is Component component && component.gameObject.scene != scene)
                throw new InvalidOperationException("Retained view reference points outside the authored scene: " + references.propertyPath);
            referenceCount++;
        }
        Require(referenceCount > 0, "All required retained references are assigned (" + referenceCount + " total); optional decorations may be omitted", report);
        for (int i = 0; i < plateViews.arraySize; i++)
        {
            var entry = plateViews.GetArrayElementAtIndex(i);
            Require(entry.FindPropertyRelative("Count").objectReferenceValue is TextMeshProUGUI,
                "Plate " + (i + 1) + " retains its TMP quantity label", report);
            foreach (string name in new[] { "Locked", "Unlock", "Served" })
            {
                var caption = entry.FindPropertyRelative(name).objectReferenceValue;
                Require(caption == null || caption is TextMeshProUGUI,
                    "Plate " + (i + 1) + " optional " + name + " caption uses TMP when assigned", report);
            }
            var rect = (RectTransform)entry.FindPropertyRelative("Rect").objectReferenceValue;
            var icon = (Image)entry.FindPropertyRelative("LockIcon").objectReferenceValue;
            var authoredIcon = rect.Find("Lock icon") ?? rect.Find("Image");
            if (authoredIcon != null && authoredIcon.GetComponent<Image>() != null)
                Require(icon == authoredIcon.GetComponent<Image>(), "Plate " + (i + 1) + " retains its authored lock Image", report);
            if (icon != null)
                Require(icon.gameObject.activeSelf == (i >= 2), "Plate " + (i + 1) + " lock Image matches stage-1 unlock state", report);
        }
        for (int i = 0; i < tempViews.arraySize; i++)
        {
            var caption = tempViews.GetArrayElementAtIndex(i).FindPropertyRelative("Empty").objectReferenceValue;
            Require(caption == null || caption is TextMeshProUGUI,
                "Tray " + (i + 1) + " optional Empty caption uses TMP when assigned", report);
        }
        var assigned = serialized.FindProperty("candySprites");
        Require(assigned != null && assigned.arraySize == 32 && Enumerable.Range(0, 32).All(i =>
            assigned.GetArrayElementAtIndex(i).objectReferenceValue == candies[i]),
            "Serialized candySprites uses exact numeric suffix order _0 through _31", report);
        var samples = controller.GetComponentsInChildren<Image>(true).Where(image => image.name == "Flavor sample").ToArray();
        Require(samples.Length == 4 && samples.Count(sample => sample.gameObject.activeInHierarchy) == 2,
            "Four authored plate samples are retained; exactly two are active at stage 1", report);
        var model = new CandySortModel(1);
        for (int i = 0; i < model.ActivePlates; i++)
        {
            string plateName = "Plate " + (i + 1);
            var sample = samples.Single(image => image.transform.parent.name == plateName);
            var counts = sample.transform.parent.GetComponentsInChildren<TextMeshProUGUI>(true).Where(text => text.name == "Plate count").ToArray();
            Require(candies.Contains(sample.sprite) && sample.preserveAspect &&
                counts.Length == 1 && counts[0].text == "0/3", plateName + " has its imported flavor sample and 0/3 quantity", report);
            bool currentFlavor = sample.sprite == candies[(int)model.Plates[i].Kind.Value];
            if (generatedPreview)
                Require(currentFlavor, plateName + " preview matches the current stage-1 generated order", report);
            else if (!currentFlavor)
                report.AppendLine("NOTE: " + plateName + " retains its authored preview flavor; runtime will apply the current generated order. Validation does not rebuild the preview.");
        }

        var images = SceneObjects<Image>(scene);
        foreach (var image in images)
        {
            if (image.sprite == null || !AssetDatabase.Contains(image.sprite))
                throw new InvalidOperationException("Nonpersistent or missing Image sprite: " + HierarchyPath(image.transform));
            if (image.overrideSprite != null && !AssetDatabase.Contains(image.overrideSprite))
                throw new InvalidOperationException("Nonpersistent Image override sprite: " + HierarchyPath(image.transform));
        }
        Require(true, "All " + images.Length + " scene Images reference persistent sprite assets", report);
        for (int i = 0; i < InterfaceNames.Length; i++)
        {
            string path = InterfaceFolder + "/" + InterfaceNames[i] + ".png";
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            var settings = new TextureImporterSettings();
            if (importer != null) importer.ReadTextureSettings(settings);
            Require(importer != null && importer.textureType == TextureImporterType.Sprite &&
                importer.spriteImportMode == SpriteImportMode.Single && settings.spriteMeshType == SpriteMeshType.FullRect &&
                importer.spriteBorder == (i == 0 ? Vector4.one * 26 : Vector4.zero),
                InterfaceNames[i] + " is a persistent full-rect Sprite" + (i == 0 ? " with border 26" : ""), report);
        }
    }

    private static Dictionary<int, string> SnapshotObjects(Scene scene)
    {
        var result = new Dictionary<int, string>();
        foreach (GameObject game in SceneObjects<GameObject>(scene))
        {
            string path = HierarchyPath(game.transform);
            result.Add(game.GetInstanceID(), path + " [GameObject]");
            foreach (var component in game.GetComponents<Component>())
                if (component != null) result.Add(component.GetInstanceID(), path + " [" + component.GetType().FullName + "]");
        }
        return result;
    }

    private static string[] SnapshotInterfaceAssets()
    {
        return InterfaceNames.Select(name => InterfaceFolder + "/" + name + ".png").Select(path =>
            AssetDatabase.AssetPathToGUID(path) + "|" + Convert.ToBase64String(File.ReadAllBytes(Absolute(path))) + "|" +
            Convert.ToBase64String(File.ReadAllBytes(Absolute(path + ".meta"))) + "|" +
            File.GetLastWriteTimeUtc(Absolute(path)).Ticks + "|" + File.GetLastWriteTimeUtc(Absolute(path + ".meta")).Ticks).ToArray();
    }

    private static T[] SceneObjects<T>(Scene scene) where T : UnityEngine.Object
    {
        if (typeof(T) == typeof(GameObject))
            return scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .Select(t => t.gameObject as T).ToArray();
        return scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();
    }

    private static string HierarchyPath(Transform transform)
    {
        string path = transform.GetSiblingIndex() + ":" + transform.name;
        while (transform.parent != null)
        {
            transform = transform.parent;
            path = transform.GetSiblingIndex() + ":" + transform.name + "/" + path;
        }
        return path;
    }

    private static void RequireEditMode()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play mode before building or verifying the authored scene.");
    }

    private static void Require(bool condition, string message, StringBuilder report)
    {
        if (!condition) throw new InvalidOperationException(message);
        report.AppendLine("PASS: " + message);
    }

    private static void WriteReport(StringBuilder report)
    {
        Directory.CreateDirectory(Absolute("Temp/CandySortVerification"));
        File.WriteAllText(Absolute(ReportPath), report.ToString());
    }

    private static string Absolute(string projectPath)
    {
        return Path.Combine(Path.GetDirectoryName(Application.dataPath), projectPath);
    }
}
