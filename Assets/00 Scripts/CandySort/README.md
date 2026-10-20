# Candy Sort prototype

## Run

Open `Assets/Scenes/CandySortGame.unity` and press Play, or use **Candy Sort → Play Game**. This scene is the enabled startup scene in Build Settings. The original template scenes and gameplay scripts are untouched.

## Authored scene UI

Outside Play mode, use **Candy Sort → Build or Refresh Scene** to create or refresh the saved UI hierarchy in `Assets/Scenes/CandySortGame.unity`. The canvas, camera, EventSystem, static interface and settled stage-1 preview can then be inspected in the Hierarchy/Scene view without pressing Play. Refresh reuses the authored views instead of creating another canvas. Runtime uses those serialized views and binds gameplay when Play begins.

The builder uses current in-memory edits when the Candy Sort scene is active. If another scene is active, it asks whether to save modified scenes before switching; Cancel stops the build. A successful build saves the Candy Sort scene, including its current edits. It does not delete unrelated scene objects. Duplicate controllers or extra canvases/cameras/EventSystems should be resolved manually, not silently removed.

The five procedural interface sprites are exported to real PNG assets in `Assets/CandySortUI/` (`Rounded`, `Circle`, `Ocean`, `Bubble`, `Ring`), imported as full-rect sprites; `Rounded` has a 26-pixel nine-slice border. Existing `.meta` files/GUIDs survive refresh, and unchanged bytes/import settings are not rewritten or reimported. The controller receives the existing candy sheet slices in numeric suffix order `_0`–`_31`; the candy source texture and slicing metadata are not modified. Keep the generated PNGs and their `.meta` files with the saved scene so its sprite references survive editor/domain reloads.

Use **Candy Sort → Verify Authored Scene** for an explicit edit-mode check. It builds/saves twice, checks stable scene object/component identities and counts, requires one canvas/camera/EventSystem, 81 stage-1 candy buttons, imported plate samples and persistent sprite references on every scene `Image`, and checks unchanged source metadata and generated asset GUIDs/bytes. The report is `Temp/CandySortVerification/scene-build-report.txt`. This is an authored-scene/persistence check, not a campaign-solvability certification.

After reopening the saved scene or reloading the editor, use **Candy Sort → Validate Saved Scene** to check the authored hierarchy and persistent sprites without rebuilding or saving it. This also checks that retained camera/rect/text/button references, four plate views, five tray slots and all five interface sprites survived serialization. Save any scene edits before this validation. It writes the same `scene-build-report.txt` report.

These three new commands run only when explicitly invoked: no startup hooks, polling, watchers, request files or pending-task persistence. They never enter Play mode, access PlayerPrefs or change Starting Level/Remember Progress. The saved edit preview is always stage 1, independently of the gameplay progress/settings used at runtime. For batch editor use, the public entry points are `-executeMethod CandySortSceneBuilder.BuildScene`, `-executeMethod CandySortSceneBuilder.VerifyAuthoredScene` and `-executeMethod CandySortSceneBuilder.ValidateSavedScene` (alongside the normal Unity `-projectPath`, `-batchmode`, `-quit` and `-logFile` arguments). Existing live smoke/trace helpers below remain separate tools.

## Controls and rules

- Tap a candy inside a bubble, not just the bubble outline.
- Each plate has its own candy type, shown by an **icon and counter (0/3, 1/3, 2/3, 3/3) below the dish**, without a candy-name label. The dish starts empty and displays each collected candy in one of three positions. The complete set and 3/3 counter are briefly shown before the plate takes another pending order.
- A candy without a matching plate enters the first empty one of five temporary slots. Stored candies automatically move when a compatible plate becomes available.
- Empty bubbles pop. Supported bubbles fall and settle into the freed space.
- Bubbles retain their original sizes. All bubble candies use the same normal size, including single-candy bubbles. Taking one leaves its position empty; the others do not rearrange. Undo restores the original candy positions too.
- Overflow waits above the chamber until space opens, rather than shrinking the candy artwork.
- A full tray does not end the game if an exposed matching candy remains playable. Undo and restart are available when no exposed matching move remains.
- Plate 3 unlocks at stage 3; plate 4 unlocks at stage 5. There is no timer, advertising, currency or account dependency.

## The 30-stage campaign

`CandySortCampaign.cs` is the centralized, deterministic stage table. Every stage defines its order count, flavor pool, plate count, bubble sizes, mixing, seed, difficulty label and short Vietnamese hint. Each order contributes exactly three candies to the inventory.

| Stages | Orders per stage | Candy totals | Unlocked candy types |
| --- | --- | --- | --- |
| 1–5 | 27, 28, 29, 30, **40** | 81, 84, 87, 90, **120** | 2, 3, 3, 4, 6 |
| 6–10 | 41, 42, 43, 44, **45** | 123, 126, 129, 132, **135** | 7, 8, 9, 10, 12 |
| 11–15 | 46, 47, 48, 49, **50** | 138, 141, 144, 147, **150** | 13, 14, 15, 16, 17 |
| 16–20 | 51, 52, 53, 54, **55** | 153, 156, 159, 162, **165** | 18, 19, 20, 21, 22 |
| 21–25 | 56, 57, 58, 59, **60** | 168, 171, 174, 177, **180** | 23, 24, 25, 26, 27 |
| 26–30 | 61, 62, 63, 64, **65** | 183, 186, 189, 192, **195** | 28, 29, 30, 31, **32** |

Unlocked types follow the catalog prefix below, rather than randomly selecting locked types. Every unlocked type has at least one order and three candies; stage **30** is the first board with all **32** types. English IDs are unique even where the player supplied the same Vietnamese label: Candy 03 and Candy 32 are separate types named “Kẹo chanh”. Names are metadata, not matching keys; gameplay matches `CandyKind` values and displays icons instead of names on plates.

The opening normal stages have **81–90 candies** and the first hard stage has **120**. Every subsequent stage adds **three candies**, reaching **195** at stage 30. Totals strictly increase across all 30 stages; there is no quantity reset after a hard milestone. All totals are multiples of three. Early stages are easier through fewer types and simpler groups, not a tiny inventory.

Hard stages are **5, 10, 15, 20, 25 and 30**. They start with **2–5 candies per bubble**, with varied sizes and mixed types rather than rows of singleton blockers. Taking matching candies can leave unmatched candies holding a bubble open, so the player must manage the temporary tray while clearing the pile. A bubble may naturally contain one candy after play, but starts with at least two. Difficulty labels describe intended pacing, not a mathematically proven ranking or guarantee of a winning strategy.

The last stage shows a campaign-complete result with **replay stage 30** and **restart from stage 1** actions. Normal navigation never creates stage 31. The pure model rejects levels outside 1–30; controller inputs/saved progress are clamped to that range.

### Saves and trying a stage

On the scene controller, **Starting Level** chooses the initial stage. Disable **Remember Progress** when trying a specific stage; otherwise the saved next-unlocked stage can take precedence.

Progress uses the game-specific `CandySort.Level` PlayerPrefs key, capped at 30. `CandySort.CampaignComplete` records the finale. Relaunching after campaign completion offers the finale replay; restarting the campaign clears that completion flag. Only stage progress is saved, not candy-kind ordinals or a partially played board, so the new catalog does not require migrating a saved candy inventory. The live smoke test disables normal persistence and backs up/restores both keys around its explicit final-save check.

## Candy catalog and reskin

Select **Candy Sort Game** in the scene. Its **Candy Sprites** array has 32 entries in the player's approved sheet order: Candy ID minus one equals the enum/array index and sprite suffix. The scene assigns all 32 sliced sprites from the newly supplied higher-resolution `Assets/CandyAsset/icon.png` on the board, plates, tray and in flight. Its existing slice names retain their original prefix (`gLS0YlmzGKvWZ_i2lhPva_oYKucD0a_0` through `_31`), so the builder maps the numeric suffix, not the `icon` filename. The source texture, slicing, sprite IDs and original sheet are preserved. Images preserve aspect ratios on every candy view.

| Candy ID | Slot / suffix | English ID | Vietnamese name |
| --- | --- | --- | --- |
| 01 | 0 / `_0` | `swirl_lollipop` | Kẹo mút xoắn ốc |
| 02 | 1 / `_1` | `purple_jellybean` | Kẹo đậu thạch tím |
| 03 | 2 / `_2` | `lemon_candy` | Kẹo chanh |
| 04 | 3 / `_3` | `gummy_bear_red` | Kẹo dẻo gấu đỏ |
| 05 | 4 / `_4` | `candy_cane` | Kẹo gậy sọc đỏ trắng |
| 06 | 5 / `_5` | `peppermint_mint` | Kẹo bạc hà xoắn |
| 07 | 6 / `_6` | `orange_wrapped_candy` | Kẹo cam gói giấy |
| 08 | 7 / `_7` | `blueberry_candy` | Kẹo viên xanh |
| 09 | 8 / `_8` | `chocolate_bar` | Thanh sô-cô-la |
| 10 | 9 / `_9` | `marshmallow` | Kẹo dẻo trắng |
| 11 | 10 / `_10` | `rainbow_lollipop` | Kẹo mút cầu vồng |
| 12 | 11 / `_11` | `green_jellybean` | Kẹo đậu thạch xanh |
| 13 | 12 / `_12` | `chocolate_tart` | Bánh tart sô-cô-la |
| 14 | 13 / `_13` | `gummy_worm` | Kẹo dẻo hình giun |
| 15 | 14 / `_14` | `rock_candy_stick` | Kẹo đường kết tinh |
| 16 | 15 / `_15` | `strawberry_wrapped_candy` | Kẹo dâu gói giấy |
| 17 | 16 / `_16` | `sprinkle_candy_ball` | Kẹo viên nhiều màu |
| 18 | 17 / `_17` | `cotton_candy` | Kẹo bông gòn |
| 19 | 18 / `_18` | `caramel_cube` | Kẹo caramel vuông |
| 20 | 19 / `_19` | `gummy_bear_green` | Kẹo dẻo gấu xanh |
| 21 | 20 / `_20` | `caramel_toffee` | Kẹo caramel dạng thỏi |
| 22 | 21 / `_21` | `peppermint_swirl` | Kẹo bạc hà xoáy |
| 23 | 22 / `_22` | `pink_candy_ball` | Kẹo viên hồng |
| 24 | 23 / `_23` | `black_licorice_twist` | Kẹo cam thảo xoắn đen |
| 25 | 24 / `_24` | `orange_candy_blue_gum` | Kẹo cam kèm viên xanh |
| 26 | 25 / `_25` | `striped_lollipop` | Kẹo mút sọc |
| 27 | 26 / `_26` | `chocolate_fudge` | Kẹo sô-cô-la mềm |
| 28 | 27 / `_27` | `gummy_fish` | Kẹo dẻo hình cá |
| 29 | 28 / `_28` | `heart_candy` | Kẹo hình trái tim |
| 30 | 29 / `_29` | `red_candy_ball` | Kẹo viên đỏ |
| 31 | 30 / `_30` | `wafer_bar` | Bánh xốp wafer |
| 32 | 31 / `_31` | `lemon_wedge` | Kẹo chanh |

The catalog intentionally replaces the old ten-flavor mapping, including the temporary watermelon/kiwi representations. The controller resizes its Inspector array to the canonical count. Leave an entry empty for its generated colored placeholder; short or null override arrays also fall back safely. Both the board and plate previews use `CandySortArt.Candy`, and metadata uses aligned `Ids`, `Names` and `Colors` arrays.

## Structure

- `CandySortCampaign.cs`: immutable definitions for all 30 campaign stages.
- `CandySortModel.cs`: Unity-independent state/generation, distinct plate assignments, temporary-slot routing, completion cascades and undo.
- `CandySortGameController.cs`: UI, fixed candy positions/sizing, pointer buttons, overflow, drop/bounce animation, safe-area fitting, progression and dialogs.
- `CandyBubbleLayout.cs`: Unity-independent variable-radius circle packing, collision-aware settling after removal and exact layout undo.
- `CandySortArt.cs`: procedural sprites, dark ocean backdrop, high-contrast bubble rims, sheet-ordered candy metadata and optional artwork overrides.
- `Assets/Editor/CandySortSceneBuilder.cs`: explicit authored-scene build/refresh and edit-mode idempotence/persistent-sprite verification.
- `Assets/Editor/CandySortEditor.cs`: existing scene menus and explicitly requested live Play Mode verification (separate from the edit-mode builder).
- `Tests/CandySort/`: standalone campaign/model/layout/playability regression harness.

Bubbles pack against the chamber walls, floor and each other with irregular offsets instead of columns. Removing a bubble settles the current pile; undo restores the previous layout exactly. The controller animates solved targets with gravity and a small bounce, **not free-floating Rigidbody2D physics**.

## Verification

With .NET 10 available, run from the project root:

```sh
dotnet run --project Tests/CandySort/CandySort.Tests.csproj
```

This checks all 32 enum identities, the 30 definitions, strictly increasing candy totals, per-stage prefix pools, multi-candy bubble sizes, determinism, conservation and move/undo invariants. Its geometry suite attempts actual-geometry playthroughs with the same fixed candy slots and visibility boundary as the controller and requires winning replays. Successful traces are written to `Temp/CandySortCampaignVerification/solutions.json`; a failing geometry suite is a real playability warning, not an asset failure. Existing ten-type traces are stale after this catalog/progression change. Optional tuning commands are development tools, not runtime generation.

**Current verification status (32-type configuration):** the saved stage-1 scene passed edit-mode build/idempotence and post-reload validation (338 GameObjects, 81 board candies, persistent sprite references). Live Unity checks passed for all 32 `icon.png` slice identities, catalog/board/tray art, plate counters/collection and undo. Play-mode checks also confirmed one Canvas/camera/EventSystem, reuse of all plate/tray objects through level changes and undo, and reuse of initial bubble/candy objects on restart. Standalone model (180 complete matches covering all 32 kinds) and layout suites passed. The full harness is **2/4 suites passing**: the campaign suite flags missing pure/mixed hard-bubble variety at stage 15, and the bounded geometry policies found wins for stages 1–8 but not 9–30. Bounded seed/opening searches also did not find recommendations for stages 15 or 30. These search limits do not prove mathematical unsolvability, but later stages still need generation/difficulty balancing; neither the asset smoke nor earlier ten-type traces certify the expanded campaign.

Use **Candy Sort → Run Candy Catalog Smoke Test** for a focused imported-art check, independent of campaign solutions: 32 unique sprites, exact IDs/names and row-major source mapping; an all-32 catalog and stage-30 board screenshot; temporary-tray artwork; and plate collection/undo checks. It verifies the icon with 0/3, 1/3, 2/3 and 3/3 below the dish and no candy-name label. Screenshots use the `assets-` and `plate-` prefixes under `Temp/CandySortVerification/`.

Use **Candy Sort → Run Plate Visual Smoke Test** for only the plate counter/icon, collected candies, completed-set display and undo checks. It does not need generated campaign traces.

Use **Candy Sort → Run 32 Flavor Smoke Test** after generating current campaign traces for metadata/colors/fallback coverage and actual UI clears of stages 25 and 30. It also checks the finale save/replay boundary without changing saved progress. Live clicks require a real raycast hit and may use a visible portion of a partially clipped candy. Small solver differences between .NET and Unity mean standalone traces do not replace live UI verification.

To generate a trace using Unity's own runtime, write a stage number (for example `30`) to `Temp/CandySortUnityTrace.request` with the editor open. This explicit editor-only helper searches bounded move policies without changing the board, then verifies a fresh replay and writes a per-stage solution/report under `Temp/CandySortCampaignVerification/`. Trace level, configuration signature and move count must match before playback. The helper never runs during ordinary gameplay.

For the full live campaign check, use **Candy Sort → Run Play Mode Smoke Test** outside Play mode after saving the active scene. It consumes current traces and checks tutorial setup, geometry, fixed candy slots/sizing, tray routing, exact undo, progression, plate unlocks, restart/help, narrow portrait and representative stage clears (1, 5, 15, 30). It ends Play mode and writes screenshots under `Temp/CandySortVerification/`. Existing unrelated editor-extension warnings may still appear in this template.
