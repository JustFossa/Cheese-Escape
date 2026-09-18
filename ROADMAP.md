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

- [ ] **01. Networked round state, results, return to lobby**
      One `RoundManager : NetworkBehaviour` with a `NetworkVariable<RoundState>`, survivor count and
      timer. Ends on: all survivors escaped / all caught / timer expiry (hunter wins). Results
      screen, then back to `LobbyScene` with the connection **intact** — no shutdown.
- [ ] **02. Spectate on death**
      Don't despawn on catch. Disable collider + `Movement`, keep the camera, attach to another
      player's view. The object is already spawned and networked.
- [ ] **03. Cheese counter onto the network**
      `GameUI` is a plain MonoBehaviour singleton; `AddCheese` runs per-client via ClientRpc and each
      client destroys its own `cheeseDoor`. Make it a `NetworkVariable<int>` on the round manager.
      **Do this with 01 — it's one piece of work.**

## Tier 2 — the hunter is a movement controller, not a character

Survivors have sprint, keys, doors, cheese, safe zones. The hunter has walking into people.

- [ ] **04. One real hunter ability**
      A movement-ping on cooldown, lock-a-door-behind-you, a short lunge, or a trap on a cheese
      pickup. Biggest *content* gap in the game. (`Rat.prefab` already has `interactionRange: 10`
      vs the survivor's 4 — someone was already thinking about this.)
- [ ] **05. Proximity audio**
      Footsteps, heartbeat on approach, audible pickups. Primary information channel in this genre,
      not polish. Every script already declares `AudioSource`/`AudioClip` fields; almost none wired.
      `Assets/Assets/Music/` is untracked — may already be in progress.
- [ ] **06. Hiding spots**
      It's hide-and-seek with nowhere to hide. A `HidingSpot : IInteractable` — hold E to enter,
      camera moves inside, hunter has to check it. Reuses the existing interaction system and turns
      the 11 tables into gameplay.

## Tier 3 — depth, once the loop holds

- [ ] **07. Rescue mechanic**
      No reason for two survivors to share a room today. Downed-instead-of-dead + teammate revive.
      Same plumbing as 02.
- [ ] **08. Randomized cheese and key spawns**
      9 cheese / 4 keys at fixed positions = round two is round one. Server picks 9 of ~20 candidate
      points at round start and replicates.

## Cleanup — before building on any of it

- [ ] **Three cheese implementations.** `Cheese.cs` (trigger, networked), `CheeseInteractable.cs`
      (hold E, networked — the live one, used by `cheese.prefab`), `CheeseCollectible.cs` (hold E,
      *not* networked). Delete two before someone places the wrong one and gets a silent no-op.
- [ ] **Safe zones are free and unlimited.** No timer, no cost — a survivor can stand in one forever.
      Becomes a stalemate hole the moment a round timer exists.

---

## Sequencing

01 + 03 together first. Then 02, then 05 — those buy the most feeling per hour. 04 is what makes the
hunter fun rather than merely functional. Tier 3 waits for a version people have played twice.

---

## Done

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
