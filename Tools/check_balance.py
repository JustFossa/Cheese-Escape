"""Balance invariants for Cheese Escape. Run: python Tools/check_balance.py

These are asset-file values Unity never validates. They broke once already: the hunter
shipped at 5/8 while survivors ran 15/20, so the hunter could not catch anyone.
"""
import re
import sys
import pathlib

ROOT = pathlib.Path(__file__).resolve().parent.parent
CHEESE_PREFAB_GUID = "b3cd627e1fbd4184ba972e99a3777dbb"


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


def cheese_in_scene():
    text = (ROOT / "Assets/Scenes/GameScene.unity").read_text(encoding="utf-8")
    return len(re.findall(
        rf"m_SourcePrefab: {{fileID: 100100000, guid: {CHEESE_PREFAB_GUID}", text))


def cheese_needed():
    text = (ROOT / "Assets/Script/UI/GameUI.cs").read_text(encoding="utf-8")
    return int(re.search(r"cheeseNeededForDoor = (\d+)", text).group(1))


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

    # The door must be reachable, with slack for cheese the hunter camps.
    total, needed = cheese_in_scene(), cheese_needed()
    assert needed <= total, f"door needs {needed} cheese but GameScene has {total}"
    assert total - needed >= 2, f"only {total - needed} spare cheese - no route choice"

    print(f"OK  hunter {hunter['moveSpeed']}/{hunter['sprintSpeed']} "
          f"survivor {survivor['moveSpeed']}/{survivor['sprintSpeed']}  "
          f"sustained {h:.1f} vs {s:.1f}  cheese {needed}/{total}")


if __name__ == "__main__":
    try:
        main()
    except AssertionError as e:
        sys.exit(f"BALANCE FAIL: {e}")
