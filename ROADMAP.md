# Cheese Escape — Roadmap

Feature review, 18 Sep 2026. Full writeup: https://claude.ai/artifact/LJULCLA2qKiyEVb7bysazG

Ranked by what the game can't function without, not by effort.

---

## The finding: there is no round

`grep` for round-end logic across `Assets/Script/` returns nothing. Every exit path is
`NetworkManager.Shutdown()` + `LoadScene("MainMenuScene")`, fired independently from three places:

- `Movement.HandlePlayerElimination` — on being caught
- `PlayerData.HandlePlayerVictory` — on reaching the exit
- `PlayerData.HandleGameEndForOthers` — when anyone else does

Consequences:

1. **The hunter cannot win.** Nothing counts surviving players. Catch everyone and the round never
   ends — no victory state, no way out but Alt+F4.
2. **One escapee ends it for everybody.** `GameEnd` → `ReachExit` → RPC → everyone else waits 3s and
   lands on the menu. It's a race, not a co-op escape.
3. **A second round means re-hosting.** Shutdown drops the session: back to main menu, re-host,
   everyone re-enters the IP.

---

## Tier 1 — the loop is missing its second half

Nothing else here is buildable first.

- [x] **01. Networked round state, results, return to lobby**
      One `RoundManager : NetworkBehaviour` with a `NetworkVariable<RoundState>`, survivor count and
      timer. Ends on: all survivors escaped / all caught / timer expiry (hunter wins). Results
      screen, then back to `LobbyScene` with the connection **intact** — no shutdown.
- [x] **02. Spectate on death**
      Don't despawn on catch. Disable collider + `Movement`, keep the camera, attach to another
      player's view. The object is already spawned and networked.
- [x] **03. Cheese counter onto the network**
      `GameUI` is a plain MonoBehaviour singleton; `AddCheese` runs per-client via ClientRpc and each
      client destroys its own `cheeseDoor`. Make it a `NetworkVariable<int>` on the round manager.
      **Do this with 01 — it's one piece of work.**

## Tier 2 — the hunter is a movement controller, not a character

Survivors have sprint, keys, doors, cheese, safe zones. The hunter has walking into people.

- [x] **04. One real hunter ability**
      A movement-ping on cooldown, lock-a-door-behind-you, a short lunge, or a trap on a cheese
      pickup. Biggest *content* gap in the game. (`Rat.prefab` already has `interactionRange: 10`
      vs the survivor's 4 — someone was already thinking about this.)
- [x] **05. Proximity audio**
      Footsteps, heartbeat on approach, audible pickups. Primary information channel in this genre,
      not polish. Every script already declares `AudioSource`/`AudioClip` fields; almost none wired.
      `Assets/Assets/Music/` is untracked — may already be in progress.
- [x] **06. Hiding spots**
      It's hide-and-seek with nowhere to hide. A `HidingSpot : IInteractable` — hold E to enter,
      camera moves inside, hunter has to check it. Reuses the existing interaction system and turns
      the 11 tables into gameplay.

## Tier 3 — depth, once the loop holds

- [x] **07. Rescue mechanic**
      No reason for two survivors to share a room today. Downed-instead-of-dead + teammate revive.
      Same plumbing as 02.
- [x] **08. Randomized cheese and key spawns**
      9 cheese / 4 keys at fixed positions = round two is round one. Server picks 9 of ~20 candidate
      points at round start and replicates.
      **Cheese only.** Each cheese goes to one of 20 spots (9 authored + a spot on every table). Keys
      are *not* shuffled: keyIds 1, 2 and 5 each gate a door, so a key landing behind its own door
      soft-locks the round. Needs a reachability check (or a hand-authored per-key candidate list)
      before it is safe.

## Cleanup — before building on any of it

- [x] **Three cheese implementations.** `Cheese.cs` (trigger, networked), `CheeseInteractable.cs`
      (hold E, networked — the live one, used by `cheese.prefab`), `CheeseCollectible.cs` (hold E,
      *not* networked). Delete two before someone places the wrong one and gets a silent no-op.
- [x] **Safe zones are free and unlimited.** No timer, no cost — a survivor can stand in one forever.
      Becomes a stalemate hole the moment a round timer exists.

---

## Sequencing

01 + 03 together first. Then 02, then 05 — those buy the most feeling per hour. 04 is what makes the
hunter fun rather than merely functional. Tier 3 waits for a version people have played twice.

---

## Round 2 — depth and drama (planned 19 Sep 2026)

Ten ideas were approved. Eight are code-only and go in now; two need scene authoring in the Editor
and are **deferred**: survivor-only shortcuts (tunnels the hunter can't fit through) and a second map.

Conventions kept from round 1: pure numbers/logic in `Rules/RoundRules.cs` with a test; server-authoritative
RPCs on `RoundManager`; new player behaviour is a plain MonoBehaviour added at runtime; **no scene or prefab
edits**; no new NetworkObjects (runtime spawning would need prefab registration).

- [x] **09. Hunter rage.** `Rage = ½·cheese/needed + ½·elapsed/duration`, 0..1, derived on every peer from
      values already replicated (no new NetworkVariable). Hunter speed ×(1 + 0.10·rage), Q cooldown 25s → 15s.
      The +10% cap is deliberate: 18 × 1.10 = 19.8 < survivor sprint 20, so a fresh sprint still breaks
      contact. `check_balance.py` asserts it.
- [x] **10. Rotating hunter + match score.** `RoundRules.PickHunters` takes the least-hunted clients (random
      among ties) instead of a random pick. Server keeps points across rounds; results screen shows the
      running table. Escaped 3, still in play at the end 1, caught 0; hunter 2 per survivor caught or downed.
      Resets with the session.
- [x] **11. Hunter count scales with the lobby.** `RoundRules.HunterCount`: 1 up to 5 players, 2 from 6.
      Extra hunters spawn 1.5 units apart; the heartbeat uses the *nearest* hunter.
- [x] **12. Survivor perks.** Picked in the lobby (IMGUI buttons, remembered in PlayerPrefs, sent to the
      server). *Sprinter* regen ×1.5 / drain ×0.75; *Medic* revive 3s not 5s; *Scout* press Q to see the
      hunters for 3s (40s cooldown). Hunters ignore perks.
- [x] **13. Alarm on door open.** When `cheeseCollected` crosses the door threshold the server reveals every
      survivor to the hunter for 5s (`PlayerData.revealedUntil`) and everybody gets an alarm sound + banner.
- [x] **14. Crumb throw (F, survivors).** Server-validated 12s cooldown; a loud clatter at the target point,
      audible in 3D to everyone, plus a "?" marker for the hunter for 3s.
- [x] **15. Carry-to-deposit cheese.** Picking up cheese now *carries* it (cap 2, ×0.93 speed each) until you
      enter a **safe zone**, which banks it into the team total. A carrier who gets downed loses it, and
      `CheeseNeeded` shrinks by what was lost so the door can never become unreachable.
- [x] **16. Hunter trap (F, hunters).** Hidden snare at the hunter's feet: 20s cooldown, max 3 live, 45s
      lifetime. A survivor within 1.5 units is revealed for 10s and 30% slower for 4s. Checked every server
      frame (a sprinting survivor moves 5 units between 4 Hz ticks). The hunter sees them as markers.
      *Changed from the pitch:* a hidden snare, not fake cheese — fake cheese needs a runtime NetworkObject.

Not verifiable without the Editor: safe-zone positions vs cheese positions (the deposit trip length), the
2-hunter spawn offset (1.5) against the walls, IMGUI perk buttons against the lobby canvas.

---

## Done

**Round 2, 19 Sep 2026** — items 09–16 (see above). **Compiles, 17 unit tests pass (11 new in
`Tests/EditMode/MatchRulesTests.cs`), `check_balance.py` green — but never run in Unity, and none of the
networked parts have seen two clients.** New: `SurvivorAbilities.cs` (F crumb, Q scout);
`RoundRules` gained rage, hunter picking, points, carry, perk numbers. The two deferred items (shortcuts,
second map) are scene authoring and still open.

**Needs an Editor pass** (on top of the round-1 list below):

- Safe zones are now the cheese bank. `check_balance.py` only proves one exists; walk from the far cheese
  to the nearest zone and decide whether the trip is too long or too short. Carry cap 2 and ×0.93 speed
  per cheese are first guesses.
- Second hunter spawns 1.5 units to the +X of the first. Confirm it isn't inside a wall.
- Perk buttons are IMGUI at the bottom-left of the lobby; confirm they don't sit under the lobby canvas
  and that a click doesn't also hit a uGUI button beneath.
- Traps are invisible to survivors on purpose. If that feels unfair, draw a faint decal (local only).
- The 2-hunter threshold (6 players), rage cap (+10%), trap radius/cooldown and crumb cooldown are guesses.
- The Scout glimpse only draws while the hunter is on screen; add edge arrows if that is too subtle.
- All new sounds are synthesized placeholders; drop `Resources/Audio/alarm|clatter|snap` to replace.
- Open the project once so Unity writes `.meta` files for `SurvivorAbilities.cs` and `MatchRulesTests.cs`.

**Round system, 19 Sep 2026** — all eight items and both cleanups. **Compiles and unit-tested, but
never run in Unity** (see "Needs an Editor pass" below).

- `RoundManager` (on the Game Manager, next to `PlayerManager`) owns state, timer (5 min), cheese
  counter and win conditions. Server polls live `PlayerData` 4x/sec rather than being told about each
  catch or escape, so a mid-round disconnect can't strand a counter. **No `Shutdown()` anywhere** in
  the round path — the results screen ends by loading `LobbyScene` with the session intact.
- Outcome rules live in `Assets/Script/Rules/RoundRules.cs` (own asmdef so the EditMode test in
  `Assets/Tests/EditMode/` can reference it). One escapee no longer ends the round for the others.
- A catch now means **Downed** (revivable, 45s bleedout, hold E for 5s), then **Caught** (spectates).
  Nothing despawns. `PlayerData.lifeState` is the one source of truth.
- Hunter ability: **Q** pulses recently-moved survivors (25s cooldown). Computed on the hunter's client
  from replicated transforms, no RPC.
- Hiding: hold E at any table under the scene's `Tables` object; the hunter holds E to flush you out.
- Audio is synthesized placeholders. Drop real clips at `Resources/Audio/footstep|heartbeat|pickup`.
- Safe zones: 20s occupancy charge, recharging at half rate; a drained zone stops blocking the hunter.
- Deleted `Cheese.cs` and `CheeseCollectible.cs`; removed the dead cheese branch in
  `InteractableObject` and the dead forwarders in `UIHandler`.
- All new player behaviour is a plain MonoBehaviour added at runtime, so the only scene edit is one
  component on the Game Manager in `MainMenuScene.unity`.

**Needs an Editor pass** — things only a real two-client run can settle:

- Open the project once so Unity generates `.meta` files for the new scripts/asmdefs, then commit them.
- Cheese on table tops: the offset is +0.3 above the table's renderer bounds; eyeball it.
- Under-table camera height is `table floor + 0.45`; the capsule is disabled while hiding.
- `InteractionUI` shows every interactable's prompt in red when it can't be used; the new ones opt out
  via `IConditionalPrompt`. Confirm no other prompt regressed.
- Round length (300s), bleedout (45s), revive (5s) and safe-zone charge (20s) are first guesses.

**Balance pass, 18 Sep 2026** — the hunter shipped at `moveSpeed 5 / sprintSpeed 8` against the
survivors' `15 / 20`, so it could never catch anyone.

- `Rat.prefab` — 5/8 → 16/18, staminaRegen 20 → 25, sprintDrain 25 → 20
- `Player.prefab` — `minStaminaToSprint` 10 → 45
- `Movement.cs` — `minStaminaToSprint` now gates *starting* a sprint. It was re-checked every frame,
  so it acted as a floor: sprint flickered on/off at the threshold, and `canSprint` plus the
  out-of-stamina branch were both unreachable.
- `GameUI.cs` — `cheeseNeededForDoor` 9 → 6 (the scene has exactly 9 cheese; 9/9 meant sweeping
  every room with zero slack)

Guarded by `python Tools/check_balance.py` — asserts hunter walk > survivor walk, survivor sprint >
hunter sprint, sustained chase speeds within 5%, and cheese threshold ≤ scene cheese with slack.

## Scene inventory

`GameScene.unity` — 9 cheese, 4 keys, 4 doors, 11 tables, 2 safe zones, 1 exit.
Hunter spawns at `(-26.55, 1, -59.71)`; survivors round-robin six points around `(8..17, 1, -2.7)`.
