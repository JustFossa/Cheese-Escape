"""Balance and wiring invariants for Cheese Escape. Run: python Tools/check_balance.py

These are asset-file values Unity never validates. They broke once already: the hunter
shipped at 5/8 while survivors ran 15/20, so the hunter could not catch anyone. The wiring
checks guard the round system: it is added to a scene by GUID, and a wrong GUID is silent.
"""
import re
import sys
import pathlib

ROOT = pathlib.Path(__file__).resolve().parent.parent
CHEESE_PREFAB_GUID = "b3cd627e1fbd4184ba972e99a3777dbb"
TABLE_PREFAB_GUID = "05ecc7940ec9a8a49a2f7765e732a05b"
# Deleted on purpose (Cheese.cs, CheeseCollectible.cs). If one shows up in a scene or prefab again
# it is a dead reference that silently does nothing.
DELETED_SCRIPT_GUIDS = ("63492133105819f4dbceabca32bfe3d5", "667134cd84637dd42a0804032a753a1b")


def stats(prefab):
    text = (ROOT / "Assets/Assets/Prefabs" / prefab).read_text(encoding="utf-8")
    keys = ("moveSpeed", "sprintSpeed", "maxStamina",
            "staminaRegenRate", "sprintStaminaDrain", "minStaminaToSprint")
    return {k: float(re.search(rf"^  {k}: ([-\d.]+)$", text, re.M).group(1)) for k in keys}


def sustained_speed(s):
    """Average speed over a long chase: sprint until empty, then walk until regen clears
    minStaminaToSprint, repeat. Mirrors HandleStamina in Movement.cs - keep them in step."""
    sprint_time = s["maxStamina"] / s["sprintStaminaDrain"]
    recover_time = s["minStaminaToSprint"] / s["staminaRegenRate"]
    dist = s["sprintSpeed"] * sprint_time + s["moveSpeed"] * recover_time
    return dist / (sprint_time + recover_time)


def prefab_count(guid):
    text = (ROOT / "Assets/Scenes/GameScene.unity").read_text(encoding="utf-8")
    return len(re.findall(rf"m_SourcePrefab: {{fileID: 100100000, guid: {guid}", text))


def cheese_in_scene():
    return prefab_count(CHEESE_PREFAB_GUID)


def cheese_needed():
    text = (ROOT / "Assets/Script/Network/RoundManager.cs").read_text(encoding="utf-8")
    return int(re.search(r"cheeseNeededForDoor = (\d+)", text).group(1))


def rage_boost():
    text = (ROOT / "Assets/Script/Rules/RoundRules.cs").read_text(encoding="utf-8")
    return float(re.search(r"MaxRageSpeedBoost = ([\d.]+)f", text).group(1))


def safe_zones_in_scene():
    """Cheese is carried and only counts once banked in a safe zone (SafeZones -> Deposit)."""
    meta = (ROOT / "Assets/Script/SafeZones.cs.meta").read_text(encoding="utf-8")
    guid = re.search(r"guid: ([0-9a-f]{32})", meta).group(1)
    text = (ROOT / "Assets/Scenes/GameScene.unity").read_text(encoding="utf-8")
    return len(re.findall(rf"m_Script: {{fileID: 11500000, guid: {guid}", text))


def check_wiring(total_cheese):
    # The round must never tear the session down: that was the bug (every catch and every escape
    # shut the network down and dumped everyone at the main menu).
    for f in ("Assets/Script/Movement.cs", "Assets/Script/Network/PlayerData.cs",
              "Assets/Script/Network/RoundManager.cs"):
        text = (ROOT / f).read_text(encoding="utf-8")
        code = "\n".join(line.split("//")[0] for line in text.splitlines())  # comments may mention it
        assert ".Shutdown(" not in code, f"{f} calls Shutdown() - a round must not end the session"

    # RoundManager is attached to the Game Manager in MainMenuScene by GUID, hand-written.
    meta = (ROOT / "Assets/Script/Network/RoundManager.cs.meta").read_text(encoding="utf-8")
    guid = re.search(r"guid: ([0-9a-f]{32})", meta).group(1)
    menu = (ROOT / "Assets/Scenes/MainMenuScene.unity").read_text(encoding="utf-8")
    assert guid in menu, "RoundManager is not attached to any object in MainMenuScene"

    for scene_or_prefab in list((ROOT / "Assets").rglob("*.unity")) + list((ROOT / "Assets").rglob("*.prefab")):
        text = scene_or_prefab.read_text(encoding="utf-8")
        for dead in DELETED_SCRIPT_GUIDS:
            assert dead not in text, f"{scene_or_prefab.name} references deleted script {dead}"

    # Shuffled cheese pick from every authored cheese spot plus a spot on each table.
    pool = total_cheese + prefab_count(TABLE_PREFAB_GUID)
    assert pool >= total_cheese, f"spawn pool {pool} smaller than the {total_cheese} cheese to place"
    return pool


def main():
    hunter, survivor = stats("Rat.prefab"), stats("Player.prefab")

    # A survivor out of stamina must lose ground, or the hunter never closes a chase.
    assert hunter["moveSpeed"] > survivor["moveSpeed"], \
        f'hunter walk {hunter["moveSpeed"]} must beat survivor walk {survivor["moveSpeed"]}'

    # But a fresh sprint must still break contact, or being spotted is an automatic death.
    assert survivor["sprintSpeed"] > hunter["sprintSpeed"], \
        f'survivor sprint {survivor["sprintSpeed"]} must beat hunter sprint {hunter["sprintSpeed"]}'

    # Over a long chase neither side should simply outrun the other; corners decide it.
    h, s = sustained_speed(hunter), sustained_speed(survivor)
    assert abs(h - s) / max(h, s) < 0.05, \
        f"sustained chase speeds too far apart (>5%): hunter {h:.1f} vs survivor {s:.1f}"

    # Rage speeds the hunter up as the round drags on. At full rage a fresh survivor sprint must
    # still break contact, or the last minute of every round is an automatic catch.
    boost = rage_boost()
    assert hunter["sprintSpeed"] * (1 + boost) < survivor["sprintSpeed"], \
        f'raged hunter sprint {hunter["sprintSpeed"] * (1 + boost):.1f} must stay under survivor sprint {survivor["sprintSpeed"]}'

    # No safe zone means carried cheese can never be banked, so the door can never open.
    zones = safe_zones_in_scene()
    assert zones >= 1, "GameScene has no SafeZones - carried cheese could never be banked"

    # The door must be reachable, with slack for cheese the hunter camps.
    total, needed = cheese_in_scene(), cheese_needed()
    assert needed <= total, f"door needs {needed} cheese but GameScene has {total}"
    assert total - needed >= 2, f"only {total - needed} spare cheese - no route choice"

    pool = check_wiring(total)

    print(f"OK  hunter {hunter['moveSpeed']}/{hunter['sprintSpeed']} "
          f"survivor {survivor['moveSpeed']}/{survivor['sprintSpeed']}  "
          f"sustained {h:.1f} vs {s:.1f}  raged sprint {hunter['sprintSpeed'] * (1 + boost):.1f}  "
          f"safe zones {zones}  cheese {needed}/{total} (spawn pool {pool})")


if __name__ == "__main__":
    try:
        main()
    except AssertionError as e:
        sys.exit(f"BALANCE FAIL: {e}")
