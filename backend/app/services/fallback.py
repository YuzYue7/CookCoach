"""fallback 规则表：模型超时/不可用时返回备用提示。

按 current_step 与 S6 finish_phase 分支给出针对性提示
（参考 tools/tutor_fixture.py 思路）。
输出同样过 validate，source="fallback"，message 非空。
"""
from ..schemas import GuidanceRequest

DEFAULT_TEXT = "先检查炉灶是否已经开启。"
STOVE_ON_TEXT = "火已经打开，可以倒入蛋液。"
WHERE_SWITCH_TEXT = "看金色圆圈，点击它指向的炉灶旋钮。"

S6_PHASES = {
    "return_cooked_egg": ("把盘里的熟鸡蛋拖回锅里，和番茄一起炒。", "plate"),
    "stir_fry": ("按住锅内绕一圈，把熟蛋和番茄炒匀。", "pan"),
    "turn_off_stove": ("食材已经炒匀，点炉灶旋钮关火。", "stove_switch"),
    "finish_dish": ("炉火已关，把锅中的成菜装入空盘。", "plate"),
}
S6_DEFAULT = ("请查看当前步骤。", "")
ACTION_NAMES = {
    "return_cooked_egg": "熟蛋回锅",
    "stir_fry": "合炒",
    "turn_off_stove": "关火",
    "finish_dish": "装盘",
}


def fallback_guidance(req: GuidanceRequest) -> dict:
    snap = req.snapshot
    question = req.user_question or ""
    allowed = req.allowed_highlight_targets
    target = ""
    if snap.current_step == "S6":
        phase = snap.finish_phase or "return_cooked_egg"
        text, focus = S6_PHASES.get(phase, S6_DEFAULT)
        if "哪里" in question and focus in allowed:
            target = focus
    elif "哪里" in question and not snap.stove_on and "stove_switch" in allowed:
        text, target = WHERE_SWITCH_TEXT, "stove_switch"
    elif snap.stove_on:
        text = STOVE_ON_TEXT
    else:
        text = DEFAULT_TEXT
    if req.trigger == "error" and req.recent_events:
        attempt = req.recent_events[-1]
        name = ACTION_NAMES.get(attempt.action_id)
        if name and attempt.outcome == "blocked":
            suffix = "已经完成。" if attempt.reason == "already_done" else "未执行。"
            text = name + suffix + text
    return {
        "message": text,
        "hint_level": "direct" if target else "check",
        "highlight_target_id": target,
    }
