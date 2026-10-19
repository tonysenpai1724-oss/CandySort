# Candy Sort prototype

## Run

Open `Assets/Scenes/CandySortGame.unity` and press Play, or use **Candy Sort → Play Game**. This scene is the enabled startup scene in Build Settings. The original template scenes and gameplay scripts are untouched.

## Controls and rules

- Tap a candy inside a bubble, not just the bubble outline.
- Each plate has its own flavor and accepts three matching candies. A completed plate takes another pending order.
- A candy without a matching plate enters the first empty one of five temporary slots. Stored candies automatically move when a compatible plate becomes available.
- Empty bubbles pop. Supported bubbles fall and settle into the freed space.
- Bubbles retain their original sizes. All bubble candies use the same normal size, including single-candy bubbles. Taking one leaves its position empty; the others do not rearrange. Undo restores the original candy positions too.
- Overflow waits above the chamber until space opens, rather than shrinking the candy artwork.
- A full tray does not end the game if an exposed matching candy remains playable. Undo and restart are available when no exposed matching move remains.
- Plate 3 unlocks at stage 3; plate 4 unlocks at stage 5. There is no timer, advertising, currency or account dependency.

## The 30-stage campaign

`CandySortCampaign.cs` is the centralized, deterministic stage table. Every stage defines its order count, flavor pool, plate count, bubble sizes, mixing, seed, difficulty label and short Vietnamese hint. Each order contributes exactly three candies to the inventory.

| Stages | Orders per stage | Candy totals | Progression |
| --- | --- | --- | --- |
| 1–5 | 27, 28, 29, 30, **40** | 81, 84, 87, 90, **120** | Two-flavor matching introduction, temporary tray, third plate, then first hard stage/fourth plate. |
| 6–10 | 41, 42, 43, 44, **45** | 123, 126, 129, 132, **135** | Four to five flavors, more mixing, then six-flavor hard stage. |
| 11–15 | 46, 47, 48, 49, **50** | 138, 141, 144, 147, **150** | Medium stages followed by a hard milestone. |
| 16–20 | 51, 52, 53, 54, **55** | 153, 156, 159, 162, **165** | Six flavors with four plates; increased mixed-group pressure. |
| 21–25 | 56, 57, 58, 59, **60** | 168, 171, 174, 177, **180** | Challenging stages and a hard milestone. |
| 26–30 | 61, 62, 63, 64, **65** | 183, 186, 189, 192, **195** | Final sequence, ending with the 65-order challenge. |

The opening normal stages have **81–90 candies** and the first hard stage has **120**. Every subsequent stage adds **three candies**, reaching **195** at stage 30. Totals strictly increase across all 30 stages; there is no quantity reset after a hard milestone. All totals are multiples of three. Early stages are easier through fewer flavors and simpler groups, not a tiny inventory.

Hard stages are **5, 10, 15, 20, 25 and 30**. They start with **2–5 candies per bubble**, with varied sizes and mixed flavors rather than rows of singleton blockers. Taking matching candies can leave unmatched candies holding a bubble open, so the player must manage the temporary tray while clearing the pile. A bubble may naturally contain one candy after play, but starts with at least two. Normal stages after each milestone give some breathing room. Difficulty labels describe the intended campaign pacing, not a mathematically proven ranking of every possible player strategy.

The last stage shows a campaign-complete result with **replay stage 30** and **restart from stage 1** actions. Normal navigation never creates stage 31. The pure model rejects levels outside 1–30; controller inputs/saved progress are clamped to that range.

### Saves and trying a stage

On the scene controller, **Starting Level** chooses the initial stage. Disable **Remember Progress** when trying a specific stage such as 5, 10 or 30; otherwise the saved next-unlocked stage can take precedence.

Progress uses the game-specific `CandySort.Level` PlayerPrefs key, capped at 30. `CandySort.CampaignComplete` records the finale. Relaunching after campaign completion offers the finale replay; restarting the campaign clears that completion flag. The live smoke test disables normal persistence and backs up/restores the two keys around its explicit final-save check.

## Reskin

Select **Candy Sort Game** in the scene. The controller's **Candy Sprites** array accepts optional final artwork, in this order:

0. Strawberry (pink wrapped sweet)
1. Lemon (yellow square)
2. Grape (purple swirl)
3. Mint (green striped sweet)
4. Blueberry (blue diamond)
5. Orange (orange bonbon)

Leave an entry empty to retain its generated placeholder. Both the board and plate previews use the same sprites. For placeholder colors, names, bubble material and plate shapes, see `CandySortArt.cs` and `CandySortGameController.cs`.

## Structure

- `CandySortCampaign.cs`: immutable definitions for all 30 campaign stages.
- `CandySortModel.cs`: Unity-independent state/generation, distinct plate assignments, temporary-slot routing, completion cascades and undo.
- `CandySortGameController.cs`: UI, fixed candy positions/sizing, pointer buttons, overflow, drop/bounce animation, safe-area fitting, progression and dialogs.
- `CandyBubbleLayout.cs`: Unity-independent variable-radius circle packing, collision-aware settling after removal and exact layout undo.
- `CandySortArt.cs`: procedural sprites, dark ocean backdrop, high-contrast bubble rims and optional artwork overrides.
- `Assets/Editor/CandySortEditor.cs`: scene menus and explicitly requested live Play Mode verification.
- `Tests/CandySort/`: standalone campaign/model/layout/playability regression harness.

Bubbles pack against the chamber walls, floor and each other with irregular offsets instead of columns. Removing a bubble settles the current pile; undo restores the previous layout exactly. The controller animates solved targets with gravity and a small bounce, **not free-floating Rigidbody2D physics**.

## Verification

With .NET 10 available, run from the project root:

```sh
dotnet run --project Tests/CandySort/CandySort.Tests.csproj
```

This verifies the 30 definitions, strictly increasing candy totals, multi-candy bubble sizes, deterministic model behavior, conservation, move/undo invariants and actual-geometry playthroughs with the same fixed candy slots and visibility boundary as the controller. It writes per-stage winning traces to `Temp/CandySortCampaignVerification/solutions.json`. Optional tuning commands are development tools, not runtime generation.

Then use **Candy Sort → Run Play Mode Smoke Test** outside Play mode after saving the active scene. The smoke test consumes those traces and checks tutorial setup, circle geometry, fixed candy positions/sizing, tray routing, exact undo, normal/hard progression, plate unlocks, restart/help and narrow portrait. It clears representative stages 1, 5, 15 and 30 through raycast-verified UI clicks, checks final saving/replay/campaign restart, captures screenshots under `Temp/CandySortVerification/`, and ends Play mode. Existing unrelated editor-extension warnings may still appear in this template.
