using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CandySort
{
    public class CandySortGameController : MonoBehaviour
    {
        [SerializeField, Range(1, CandySortCampaign.LevelCount)] private int startingLevel = 1;
        [SerializeField] private bool rememberProgress = true;
        [Tooltip("Optional sprites: strawberry, lemon, grape, mint, blueberry, orange. Empty entries use colored placeholders.")]
        [SerializeField] private Sprite[] candySprites = new Sprite[6];
        private const string SaveKey = "CandySort.Level";
        private const string CompletionKey = "CandySort.CampaignComplete";
        private const float BubbleCandySize = 72;
        private CandySortModel model;
        private RectTransform root, plateLayer, tempLayer, board, bubbleLayer, modalLayer, effectLayer;
        private Text levelLabel, difficultyLabel, progressLabel, hintLabel, queueLabel;
        private Image progressFill;
        private Button undoButton;
        private Font font;
        private bool busy;
        private CandyBubbleLayout layout;
        private readonly Dictionary<int, float> originalRadii = new Dictionary<int, float>();
        private readonly Dictionary<int, List<int>> candySlots = new Dictionary<int, List<int>>();
        private readonly Stack<BoardSnapshot> boardHistory = new Stack<BoardSnapshot>();
        private Rect lastSafeArea;
        private Vector2 lastScreenSize;
        private readonly Dictionary<int, BubbleView> bubbles = new Dictionary<int, BubbleView>();
        private readonly Dictionary<int, int> originalBubbleCounts = new Dictionary<int, int>();
        private readonly List<RectTransform> platePositions = new List<RectTransform>();
        private readonly List<RectTransform> tempPositions = new List<RectTransform>();
        private readonly Color cream = CandySortArt.Hex("164F70");
        private readonly Color muted = CandySortArt.Hex("B5DCEB");

        private sealed class BoardSnapshot
        {
            public Dictionary<int, CandyBubbleLayout.Point> Layout;
            public Dictionary<int, List<int>> CandySlots;
        }

        private sealed class BubbleView
        {
            public RectTransform Rect;
            public RectTransform Candies;
            public Vector2 Target;
            public float Velocity;
            public float Diameter;
            public int InitialCount;
        }

        private void Start()
        {
            Application.targetFrameRate = 60;
            CandySortArt.SetCandySprites(candySprites);
            if (Camera.main == null)
            {
                var cameraObject = new GameObject("Candy Camera", typeof(Camera));
                cameraObject.transform.SetParent(transform, false);
                cameraObject.transform.localPosition = new Vector3(0, 0, -10);
                cameraObject.tag = "MainCamera";
                var camera = cameraObject.GetComponent<Camera>();
                camera.orthographic = true;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = CandySortArt.Hex("082A44");
            }
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildInterface();
            RestoreProgress();
        }

        private void RestoreProgress()
        {
            LoadLevel(rememberProgress ? Mathf.Max(startingLevel, PlayerPrefs.GetInt(SaveKey, startingLevel)) : startingLevel);
            if (rememberProgress && model.Level == CandySortCampaign.LevelCount && PlayerPrefs.GetInt(CompletionKey, 0) == 1)
                ShowResult(true);
        }

        private void Update()
        {
            FitSafeArea();
            float dt = Mathf.Min(Time.unscaledDeltaTime, .033f);
            foreach (var view in bubbles.Values)
            {
                Vector2 pos = view.Rect.anchoredPosition;
                if (pos.y > view.Target.y + .5f)
                {
                    view.Velocity -= 1800 * dt;
                    pos.y += view.Velocity * dt;
                    if (pos.y < view.Target.y)
                    {
                        pos.y = view.Target.y;
                        view.Velocity = Mathf.Abs(view.Velocity) > 90 ? -view.Velocity * .16f : 0;
                    }
                }
                else if (view.Velocity > 1)
                {
                    view.Velocity -= 1800 * dt;
                    pos.y = Mathf.Max(view.Target.y, pos.y + view.Velocity * dt);
                }
                else pos.y = view.Target.y;
                pos.x = Mathf.Lerp(pos.x, view.Target.x, dt * 9);
                view.Rect.anchoredPosition = pos;
            }
        }

        private void BuildInterface()
        {
            var canvasObject = new GameObject("Candy Sort Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(720, 1280);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var background = Panel(canvasObject.transform, "Backdrop", Vector2.zero, new Vector2(3000, 3000), CandySortArt.Hex("082A44"));
            background.anchorMin = background.anchorMax = new Vector2(.5f, .5f);
            root = NewRect(canvasObject.transform, "Portrait play area", Vector2.zero, new Vector2(720, 1280));
            root.anchorMin = root.anchorMax = new Vector2(.5f, .5f);
            root.anchoredPosition = Vector2.zero;
            var ocean = Image(root, "Ocean backdrop", Vector2.zero, new Vector2(720, 1280), Color.white, CandySortArt.Ocean);
            Center(ocean.rectTransform);
            ocean.raycastTarget = false;
            if (EventSystem.current == null) new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

            for (int i = 0; i < 16; i++)
            {
                var dot = Image(root, "Sea sparkle", new Vector2((i * 137 % 700) - 350, 38 + i * 79), Vector2.one * (3 + i % 3 * 2),
                    new Color(.6f, .92f, 1, .28f), CandySortArt.Circle);
                dot.raycastTarget = false;
            }
            Label(root, "SWEET BUBBLES", new Vector2(0, 23), new Vector2(480, 25), 14, CandySortArt.Hex("7ED9EA"), FontStyle.Bold);
            Label(root, "Candy Sort", new Vector2(0, 63), new Vector2(490, 64), 45, CandySortArt.Ink, FontStyle.Bold);
            Button(root, "?", new Vector2(302, 62), new Vector2(54, 54), CandySortArt.Hex("176582"), CandySortArt.Ink, 25, ShowHelp);
            var levelPill = Panel(root, "Level pill", new Vector2(0, 118), new Vector2(178, 37), CandySortArt.Hex("155675"));
            levelLabel = Label(levelPill, "MÀN 01", Vector2.zero, new Vector2(170, 35), 19, CandySortArt.Hex("FFDD93"), FontStyle.Bold, true);
            difficultyLabel = Label(root, "LÀM QUEN", new Vector2(0, 156), new Vector2(660, 27), 19, muted, FontStyle.Bold);
            plateLayer = NewRect(root, "Candy plates", new Vector2(0, 244), new Vector2(680, 148));
            Label(root, "KHAY TẠM", new Vector2(-222, 340), new Vector2(190, 25), 16, CandySortArt.Hex("FFDD93"), FontStyle.Bold);
            Label(root, "Tự chuyển khi có đĩa đúng vị", new Vector2(102, 340), new Vector2(430, 25), 16, muted);
            tempLayer = NewRect(root, "Temporary tray", new Vector2(0, 387), new Vector2(650, 68));
            Panel(root, "Jar shadow", new Vector2(0, 818), new Vector2(692, 702), CandySortArt.Hex("041D34"));
            Panel(root, "Glass jar", new Vector2(0, 811), new Vector2(688, 698), CandySortArt.Hex("32849F"));
            board = Panel(root, "Bubble chamber", new Vector2(0, 811), new Vector2(664, 674), CandySortArt.Hex("0C3858"));
            board.gameObject.AddComponent<RectMask2D>();
            var water = Image(board, "Water depth", Vector2.zero, new Vector2(664, 674), Color.white, CandySortArt.Ocean);
            Center(water.rectTransform);
            water.raycastTarget = false;
            for (int i = 0; i < 7; i++)
            {
                var mote = Image(board, "Water mote", new Vector2((i * 101 % 610) - 305, -285 + i * 91), Vector2.one * (5 + i % 3 * 3), new Color(.55f, .89f, 1, .22f), CandySortArt.Circle);
                Center(mote.rectTransform);
                mote.raycastTarget = false;
            }
            bubbleLayer = NewRect(board, "Falling bubbles", Vector2.zero, new Vector2(652, 650));
            bubbleLayer.anchorMin = bubbleLayer.anchorMax = new Vector2(.5f, 0);
            bubbleLayer.pivot = new Vector2(.5f, 0);
            bubbleLayer.anchoredPosition = new Vector2(0, 12);
            queueLabel = Label(root, "", new Vector2(0, 443), new Vector2(650, 27), 17, muted);
            hintLabel = Label(root, "Chạm vào từng viên kẹo trong bong bóng", new Vector2(0, 1180), new Vector2(670, 30), 19, CandySortArt.Ink);
            Button(root, "CHƠI LẠI", new Vector2(-230, 1234), new Vector2(180, 56), cream, CandySortArt.Ink, 17, () => { if (!busy) LoadLevel(model.Level); });
            undoButton = Button(root, "HOÀN TÁC", new Vector2(0, 1234), new Vector2(210, 56), CandySortArt.Hex("158CB0"), Color.white, 18, Undo);
            var progress = Panel(root, "Order progress", new Vector2(230, 1234), new Vector2(180, 56), cream);
            progressLabel = Label(progress, "", new Vector2(0, -5), new Vector2(170, 35), 17, CandySortArt.Ink, FontStyle.Bold, true);
            var bar = Panel(progress, "Track", new Vector2(0, 17), new Vector2(135, 5), CandySortArt.Hex("103C58"));
            Center(bar);
            progressFill = Image(bar, "Filled", Vector2.zero, new Vector2(135, 5), CandySortArt.Hex("71E0E7"), CandySortArt.Rounded);
            progressFill.rectTransform.anchorMin = progressFill.rectTransform.anchorMax = new Vector2(0, .5f);
            progressFill.rectTransform.pivot = new Vector2(0, .5f);
            progressFill.rectTransform.anchoredPosition = Vector2.zero;
            effectLayer = NewRect(root, "Candy flights", Vector2.zero, new Vector2(720, 1280));
            Center(effectLayer);
            modalLayer = NewRect(root, "Dialog layer", Vector2.zero, new Vector2(720, 1280));
            Center(modalLayer);
        }

        private void FitSafeArea()
        {
            if (root == null || Screen.width <= 0 || Screen.height <= 0) return;
            var screenSize = new Vector2(Screen.width, Screen.height);
            Rect safe = Screen.safeArea;
            if (screenSize == lastScreenSize && safe == lastSafeArea) return;
            lastScreenSize = screenSize;
            lastSafeArea = safe;
            Canvas.ForceUpdateCanvases();
            Vector2 canvasSize = ((RectTransform)root.parent).rect.size;
            Vector2 available = new Vector2(canvasSize.x * safe.width / Screen.width, canvasSize.y * safe.height / Screen.height);
            root.localScale = Vector3.one * Mathf.Min(1, available.x / 720, available.y / 1280);
            root.anchoredPosition = new Vector2((safe.center.x / Screen.width - .5f) * canvasSize.x, (safe.center.y / Screen.height - .5f) * canvasSize.y);
        }

        private void LoadLevel(int level)
        {
            StopAllCoroutines();
            busy = false;
            Clear(modalLayer);
            model = new CandySortModel(Mathf.Clamp(level, 1, CandySortCampaign.LevelCount));
            Clear(bubbleLayer);
            bubbles.Clear();
            Clear(effectLayer);
            originalBubbleCounts.Clear();
            originalRadii.Clear();
            candySlots.Clear();
            boardHistory.Clear();
            foreach (var bubble in model.Bubbles)
            {
                originalBubbleCounts.Add(bubble.Id, bubble.Candies.Count);
                originalRadii.Add(bubble.Id, Diameter(bubble.Candies.Count) * .5f);
                candySlots.Add(bubble.Id, Enumerable.Range(0, bubble.Candies.Count).ToList());
            }
            layout = new CandyBubbleLayout(bubbleLayer.rect.width - 8, model.Level);
            layout.Reset(model.Bubbles.Select(b => new CandyBubbleLayout.Body(b.Id, originalRadii[b.Id])));
            Render(true);
            hintLabel.text = model.Definition.HelpText;
        }

        private void Render(bool initial = false)
        {
            RenderPlates();
            RenderTemp();
            levelLabel.text = "MÀN " + model.Level.ToString("00") + " / " + CandySortCampaign.LevelCount;
            difficultyLabel.text = model.Definition.DifficultyLabel;
            difficultyLabel.color = model.Definition.IsHard ? CandySortArt.Hex("FFCB79") : muted;
            progressLabel.text = model.CompletedOrders + " / " + model.TargetOrders + " ĐĨA";
            progressFill.rectTransform.sizeDelta = new Vector2(135f * model.CompletedOrders / model.TargetOrders, 5);
            undoButton.interactable = model.CanUndo && !busy;
            var remaining = new HashSet<int>(model.Bubbles.Select(b => b.Id));
            foreach (int id in bubbles.Keys.Where(id => !remaining.Contains(id)).ToList())
            {
                StartCoroutine(Pop(bubbles[id].Rect));
                bubbles.Remove(id);
            }
            var centers = layout.Bodies.ToDictionary(b => b.Id, b => new Vector2(b.Position.X, b.Position.Y));
            foreach (var bubble in model.Bubbles)
            {
                if (!bubbles.TryGetValue(bubble.Id, out BubbleView view))
                {
                    float diameter = originalRadii[bubble.Id] * 2;
                    var rect = NewRect(bubbleLayer, "Bubble " + bubble.Id, Vector2.zero, Vector2.one * diameter);
                    rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0);
                    rect.pivot = new Vector2(.5f, .5f);
                    var tint = Image(rect, "Tint", Vector2.zero, Vector2.one * diameter, new Color(.17f, .62f, .83f, .18f), CandySortArt.Circle);
                    Center(tint.rectTransform);
                    var glass = Image(rect, "Bubble rim", Vector2.zero, Vector2.one * diameter, Color.white, CandySortArt.Bubble);
                    Center(glass.rectTransform);
                    var shine = Image(rect, "Glint", new Vector2(-diameter * .24f, -diameter * .29f), new Vector2(diameter * .2f, diameter * .065f), new Color(1, 1, 1, .8f), CandySortArt.Circle);
                    Center(shine.rectTransform);
                    view = new BubbleView { Rect = rect, Diameter = diameter, InitialCount = originalBubbleCounts[bubble.Id] };
                    view.Candies = NewRect(rect, "Sweets", Vector2.zero, Vector2.one * diameter);
                    Center(view.Candies);
                    bubbles.Add(bubble.Id, view);
                    rect.anchoredPosition = initial
                        ? new Vector2(centers[bubble.Id].x, Mathf.Max(board.rect.height + 30 + bubble.Id * 38, centers[bubble.Id].y + 100))
                        : centers[bubble.Id];
                }
                view.Target = centers[bubble.Id];
                Clear(view.Candies);
                int count = bubble.Candies.Count;
                for (int c = 0; c < count; c++)
                {
                    int index = c, id = bubble.Id;
                    Vector2 offset = CandyOffset(view.InitialCount, candySlots[bubble.Id][c], view.Diameter);
                    var button = Button(view.Candies, "", offset, Vector2.one * BubbleCandySize, Color.white, Color.white, 10, null, CandySortArt.Candy(bubble.Candies[c]));
                    Center(button.GetComponent<RectTransform>());
                    button.onClick.AddListener(() => Take(id, index, button.GetComponent<RectTransform>()));
                    button.gameObject.name = "Candy " + CandySortArt.Names[(int)bubble.Candies[c]];
                }
            }
            int waiting = bubbles.Values.Count(v => v.Target.y - v.Diameter * .5f >= board.rect.height - 12);
            queueLabel.text = model.Bubbles.Sum(b => b.Candies.Count) + " viên còn lại  ·  " +
                (waiting > 0 ? waiting + " bong bóng chờ phía trên" : model.ActivePlates + "/4 đĩa đang mở");
        }

        private void RenderPlates()
        {
            Clear(plateLayer);
            platePositions.Clear();
            for (int i = 0; i < 4; i++)
            {
                var plate = model.Plates[i];
                float x = (i - 1.5f) * 164;
                var tile = Panel(plateLayer, "Plate " + (i + 1), new Vector2(x, 0), new Vector2(152, 160), plate.Active ? cream : CandySortArt.Hex("103B56"));
                Center(tile);
                platePositions.Add(tile);
                if (!plate.Active)
                {
                    Label(tile, "· · ·", new Vector2(0, -18), new Vector2(135, 65), 37, muted, FontStyle.Bold, true);
                    Label(tile, "MỞ MÀN " + (i == 2 ? 3 : 5), new Vector2(0, 42), new Vector2(150, 30), 15, muted, FontStyle.Bold, true);
                    continue;
                }
                if (!plate.Kind.HasValue)
                {
                    Label(tile, model.IsWon ? "XONG" : "ĐÃ PHỤC VỤ", Vector2.zero, new Vector2(140, 50), 17, muted, FontStyle.Bold, true);
                    continue;
                }
                CandyKind kind = plate.Kind.Value;
                var dish = Image(tile, "Porcelain dish", new Vector2(0, -15), new Vector2(119, 79), Color.white, CandySortArt.Circle);
                Center(dish.rectTransform);
                var ring = Image(tile, "Colored plate rim", new Vector2(0, -15), new Vector2(119, 79), CandySortArt.Colors[(int)kind] * new Color(1, 1, 1, .45f), CandySortArt.Ring);
                Center(ring.rectTransform);
                var sample = Image(tile, "Flavor sample", new Vector2(0, -22), new Vector2(55, 55), Color.white, CandySortArt.Candy(kind));
                Center(sample.rectTransform);
                Label(tile, CandySortArt.Names[(int)kind], new Vector2(0, 32), new Vector2(150, 25), 17, CandySortArt.Ink, FontStyle.Bold, true);
                for (int c = 0; c < 3; c++)
                {
                    var dot = Image(tile, "Candy socket", new Vector2((c - 1) * 30, 61), new Vector2(20, 20), c < plate.Count ? CandySortArt.Colors[(int)kind] : CandySortArt.Hex("376C88"), CandySortArt.Circle);
                    Center(dot.rectTransform);
                }
            }
        }

        private void RenderTemp()
        {
            Clear(tempLayer);
            tempPositions.Clear();
            for (int i = 0; i < model.Temp.Length; i++)
            {
                int index = i;
                var slot = Panel(tempLayer, "Temp slot " + i, new Vector2((i - 2) * 115, 0), new Vector2(91, 76), cream);
                Center(slot);
                tempPositions.Add(slot);
                var outline = Image(slot, "Slot inset", Vector2.zero, new Vector2(61, 56), CandySortArt.Hex("0B324D"), CandySortArt.Circle);
                Center(outline.rectTransform);
                if (model.Temp[i].HasValue)
                {
                    var button = Button(slot, "", Vector2.zero, new Vector2(61, 61), Color.white, Color.white, 12, () => UseTemp(index), CandySortArt.Candy(model.Temp[i].Value));
                    Center(button.GetComponent<RectTransform>());
                }
                else Label(slot, "+", Vector2.zero, new Vector2(50, 45), 25, CandySortArt.Hex("71B2CF"), FontStyle.Normal, true);
            }
        }

        private void Take(int id, int index, RectTransform source)
        {
            if (busy || model.IsWon) return;
            var bubble = model.Bubbles.FirstOrDefault(b => b.Id == id);
            if (bubble == null || index >= bubble.Candies.Count) return;
            CandyKind kind = bubble.Candies[index];
            Vector3 origin = source.position;
            int plate = model.Plates.ToList().FindIndex(p => p.Active && p.Kind == kind);
            int slot = System.Array.FindIndex(model.Temp, t => !t.HasValue);
            int completed = model.CompletedOrders;
            var previousBoard = CaptureBoard();
            if (!model.TryTake(id, index))
            {
                hintLabel.text = "Khay tạm đã đầy. Chọn vị đang có trên đĩa!";
                StartCoroutine(Nudge(source));
                return;
            }
            boardHistory.Push(previousBoard);
            candySlots[id].RemoveAt(index);
            if (!model.Bubbles.Any(b => b.Id == id)) layout.Remove(id);
            busy = true;
            source.gameObject.SetActive(false);
            Vector3 destination = plate >= 0 ? platePositions[plate].position : tempPositions[Mathf.Max(0, slot)].position;
            StartCoroutine(Transfer(kind, origin, destination, completed));
        }

        private IEnumerator Transfer(CandyKind kind, Vector3 origin, Vector3 destination, int completed)
        {
            var flight = Image(effectLayer, "Flying candy", Vector2.zero, new Vector2(58, 58), Color.white, CandySortArt.Candy(kind));
            Center(flight.rectTransform);
            for (float t = 0; t < 1; t += Time.unscaledDeltaTime / .3f)
            {
                float eased = t * t * (3 - 2 * t);
                flight.transform.position = Vector3.Lerp(origin, destination, eased) + Vector3.up * Mathf.Sin(t * Mathf.PI) * 65 * root.lossyScale.y;
                flight.transform.localScale = Vector3.one * (1 + Mathf.Sin(t * Mathf.PI) * .2f);
                yield return null;
            }
            Destroy(flight.gameObject);
            busy = false;
            Render();
            hintLabel.text = model.CompletedOrders > completed ? "Ngọt ngào! Đủ bộ 3, đĩa mới đã sẵn sàng." : "Chạm vào từng viên kẹo trong bong bóng";
            if (model.CompletedOrders > completed) StartCoroutine(Celebrate(destination));
            if (model.IsWon) CompleteLevel();
            else if (IsBoardBlocked()) ShowResult(false);
        }

        private bool IsBoardBlocked()
        {
            if (model.Temp.Any(t => !t.HasValue)) return false;
            foreach (var bubble in model.Bubbles)
            {
                var view = bubbles[bubble.Id];
                for (int i = 0; i < bubble.Candies.Count; i++)
                {
                    float centerY = view.Target.y - CandyOffset(view.InitialCount, candySlots[bubble.Id][i], view.Diameter).y;
                    if (centerY >= 0 && centerY <= board.rect.height - 12 && model.Plates.Any(p => p.Active && p.Kind == bubble.Candies[i])) return false;
                }
            }
            return true;
        }

        private static Vector2 CandyOffset(int count, int index, float diameter)
        {
            if (count == 1) return Vector2.zero;
            float angle = (index * 360f / count + 90) * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * diameter * (count <= 3 ? .22f : .28f);
        }

        private void UseTemp(int index)
        {
            if (busy || !model.Temp[index].HasValue) return;
            var previousBoard = CaptureBoard();
            if (model.TryUseTemp(index))
            {
                boardHistory.Push(previousBoard);
                Render();
                if (model.IsWon) CompleteLevel();
            }
            else hintLabel.text = "Viên này đang chờ đĩa đúng vị. Hãy chọn kẹo khác.";
        }

        private BoardSnapshot CaptureBoard()
        {
            return new BoardSnapshot
            {
                Layout = layout.Capture(),
                CandySlots = candySlots.ToDictionary(entry => entry.Key, entry => new List<int>(entry.Value))
            };
        }

        private void Undo()
        {
            if (busy || !model.Undo()) return;
            var snapshot = boardHistory.Pop();
            layout.Restore(snapshot.Layout, originalRadii);
            candySlots.Clear();
            foreach (var entry in snapshot.CandySlots) candySlots.Add(entry.Key, entry.Value);
            Clear(modalLayer);
            Render();
            hintLabel.text = "Đã hoàn tác một lượt.";
        }

        private void CompleteLevel()
        {
            if (rememberProgress)
            {
                PlayerPrefs.SetInt(SaveKey, Mathf.Min(model.Level + 1, CandySortCampaign.LevelCount));
                if (model.Level == CandySortCampaign.LevelCount) PlayerPrefs.SetInt(CompletionKey, 1);
                PlayerPrefs.Save();
            }
            ShowResult(true);
        }

        private void RestartCampaign()
        {
            if (rememberProgress)
            {
                PlayerPrefs.SetInt(SaveKey, 1);
                PlayerPrefs.SetInt(CompletionKey, 0);
                PlayerPrefs.Save();
            }
            LoadLevel(1);
        }

        private void ShowResult(bool won)
        {
            bool campaignWon = won && model.Level == CandySortCampaign.LevelCount;
            Clear(modalLayer);
            var shade = Panel(modalLayer, "Dialog shade", Vector2.zero, new Vector2(720, 1280), new Color(.25f, .13f, .27f, .38f), false);
            Center(shade);
            shade.GetComponent<Image>().raycastTarget = true;
            var card = Panel(shade, "Result card", Vector2.zero, new Vector2(580, 430), cream);
            Center(card);
            Label(card, campaignWon ? "HOÀN THÀNH 30 MÀN!" : won ? "THẬT NGỌT NGÀO!" : "KHAY TẠM ĐÃ ĐẦY", new Vector2(0, -129), new Vector2(550, 60), 32, CandySortArt.Ink, FontStyle.Bold, true);
            Label(card, campaignWon ? "Bạn đã hoàn thành hành trình Candy Sort.\nCảm ơn bạn đã chơi!" : won ? "Bạn đã phân loại hết kẹo của màn này." : "Không còn nước đi phù hợp.\nHoàn tác để thử một cách xếp khác nhé.", new Vector2(0, -30), new Vector2(500, 100), 23, muted, FontStyle.Normal, true);
            var button = Button(card, campaignWon ? "CHƠI LẠI MÀN 30" : won ? "MÀN TIẾP THEO  →" : "HOÀN TÁC", new Vector2(0, 77), new Vector2(430, 66), CandySortArt.Hex("168BB0"), Color.white, 22, () => { if (campaignWon) LoadLevel(CandySortCampaign.LevelCount); else if (won) LoadLevel(model.Level + 1); else Undo(); });
            Center(button.GetComponent<RectTransform>());
            if (campaignWon)
            {
                button = Button(card, "Chơi lại từ màn 1", new Vector2(0, 155), new Vector2(400, 50), cream, muted, 19, RestartCampaign);
                Center(button.GetComponent<RectTransform>());
            }
            else if (!won)
            {
                button = Button(card, "Chơi lại màn này", new Vector2(0, 155), new Vector2(400, 50), cream, muted, 19, () => LoadLevel(model.Level));
                Center(button.GetComponent<RectTransform>());
            }
        }

        private void ShowHelp()
        {
            if (busy) return;
            Clear(modalLayer);
            var shade = Panel(modalLayer, "Help shade", Vector2.zero, new Vector2(720, 1280), new Color(.25f, .13f, .27f, .38f), false);
            Center(shade);
            shade.GetComponent<Image>().raycastTarget = true;
            var card = Panel(shade, "How to play", Vector2.zero, new Vector2(600, 670), cream);
            Center(card);
            Label(card, "MỘT CHÚT NGỌT NGÀO", new Vector2(0, -248), new Vector2(560, 70), 29, CandySortArt.Ink, FontStyle.Bold, true);
            Label(card, "1. Chạm từng viên kẹo trong bong bóng.\n\n2. Kẹo đúng vị sẽ bay lên đĩa. Mỗi đĩa nhận 3 viên rồi đổi sang vị mới.\n\n3. Kẹo chưa có đĩa sẽ vào 5 ô tạm và tự chuyển khi đĩa phù hợp xuất hiện.\n\n4. Bong bóng hết kẹo sẽ vỡ; các bong bóng phía trên rơi xuống.\n\nĐĩa mới mở ở màn 3 và màn 5.", new Vector2(0, -5), new Vector2(510, 450), 22, CandySortArt.Ink, FontStyle.Normal, true);
            var close = Button(card, "BẮT ĐẦU THÔI", new Vector2(0, 263), new Vector2(450, 65), CandySortArt.Hex("168BB0"), Color.white, 21, () => Clear(modalLayer));
            Center(close.GetComponent<RectTransform>());
        }

        private IEnumerator Pop(RectTransform rect)
        {
            for (float t = 0; t < 1; t += Time.unscaledDeltaTime / .18f)
            {
                if (rect == null) yield break;
                rect.localScale = Vector3.one * (1 + t * .18f);
                foreach (var image in rect.GetComponentsInChildren<Image>()) { var c = image.color; c.a *= 1 - t; image.color = c; }
                yield return null;
            }
            if (rect != null) Destroy(rect.gameObject);
        }

        private IEnumerator Celebrate(Vector3 position)
        {
            var pieces = new List<RectTransform>();
            for (int i = 0; i < 12; i++)
            {
                var image = Image(effectLayer, "Sugar sparkle", Vector2.zero, new Vector2(9, 15), CandySortArt.Colors[i % 6], CandySortArt.Rounded);
                Center(image.rectTransform);
                image.transform.position = position;
                pieces.Add(image.rectTransform);
            }
            for (float t = 0; t < 1; t += Time.unscaledDeltaTime / .65f)
            {
                for (int i = 0; i < pieces.Count; i++)
                {
                    float angle = i * 2.399f;
                    pieces[i].position = position + new Vector3(Mathf.Cos(angle) * 140 * t, Mathf.Sin(angle) * 120 * t - 70 * t * t, 0) * root.lossyScale.y;
                    pieces[i].localRotation = Quaternion.Euler(0, 0, i * 47 + t * 180);
                    pieces[i].localScale = Vector3.one * (1 - t);
                }
                yield return null;
            }
            foreach (var piece in pieces) if (piece != null) Destroy(piece.gameObject);
        }

        private IEnumerator Nudge(RectTransform rect)
        {
            Vector2 original = rect.anchoredPosition;
            for (float t = 0; t < .3f; t += Time.unscaledDeltaTime)
            {
                if (rect == null) yield break;
                rect.anchoredPosition = original + Vector2.right * Mathf.Sin(t * 70) * 7;
                yield return null;
            }
            if (rect != null) rect.anchoredPosition = original;
        }

        private static float Diameter(int count) => 144 + count * 16;
        private static void Center(RectTransform rect)
        {
            Vector2 position = rect.anchoredPosition;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.anchoredPosition = position;
        }
        private static RectTransform NewRect(Transform parent, string name, Vector2 position, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1);
            rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = new Vector2(position.x, -position.y);
            rect.sizeDelta = size;
            return rect;
        }
        private RectTransform Panel(Transform parent, string name, Vector2 position, Vector2 size, Color color, bool rounded = true)
        {
            return Image(parent, name, position, size, color, rounded ? CandySortArt.Rounded : null).rectTransform;
        }
        private Image Image(Transform parent, string name, Vector2 position, Vector2 size, Color color, Sprite sprite)
        {
            var rect = NewRect(parent, name, position, size);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.type = sprite == CandySortArt.Rounded ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple;
            image.color = color;
            // Transparent bubble corners must not occlude candies in a neighbouring circle.
            image.raycastTarget = false;
            return image;
        }
        private Text Label(Transform parent, string text, Vector2 position, Vector2 size, int fontSize, Color color, FontStyle style = FontStyle.Normal, bool center = false)
        {
            var rect = NewRect(parent, "Text " + text, position, size);
            if (center) Center(rect);
            var label = rect.gameObject.AddComponent<Text>();
            label.font = font;
            label.text = text;
            label.fontSize = fontSize;
            label.color = color;
            label.fontStyle = style;
            label.alignment = TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.raycastTarget = false;
            return label;
        }
        private Button Button(Transform parent, string text, Vector2 position, Vector2 size, Color color, Color textColor, int fontSize, UnityEngine.Events.UnityAction action, Sprite sprite = null)
        {
            var image = Image(parent, "Button " + text, position, size, color, sprite ?? CandySortArt.Rounded);
            var button = image.gameObject.AddComponent<Button>();
            image.raycastTarget = true;
            button.targetGraphic = image;
            var colors = button.colors;
            colors.highlightedColor = new Color(.97f, .94f, 1);
            colors.pressedColor = new Color(.83f, .76f, .91f);
            colors.disabledColor = new Color(.85f, .85f, .85f, .45f);
            button.colors = colors;
            if (action != null) button.onClick.AddListener(action);
            if (!string.IsNullOrEmpty(text)) Label(image.transform, text, Vector2.zero, size - new Vector2(10, 4), fontSize, textColor, FontStyle.Bold, true);
            return button;
        }
        private void Clear(Transform target)
        {
            for (int i = target.childCount - 1; i >= 0; i--)
            {
                target.GetChild(i).gameObject.SetActive(false);
                Destroy(target.GetChild(i).gameObject);
            }
        }
    }
}
