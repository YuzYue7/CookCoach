"""fallback 规则表：模型超时/不可用时返回备用提示。

决策顺序（先步骤、后错误）：
1. 按 current_step 查步骤表（S1–S5；S6 走 finish_phase 分支；done 收尾），
   必要时结合 stove_on / 最近事件微调，得到「现在该做什么」（句二）；
2. trigger=error 且最近一条事件为 blocked 时，按该事件的 action_id + reason
   生成「刚才为什么没执行」（句一）；
3. 玩家问「哪里」时给出位置提示，并在允许时高亮对应物体。
最终 message = "句一。句二"（error 时）或仅句二；
输出同样过 validate，source="fallback"，message 非空。
"""
from ..schemas import GuidanceRequest

WHERE_SWITCH_TEXT = "看金色圆圈，点击它指向的炉灶旋钮。"
DONE_TEXT = "菜已经做好了，可以关火装盘享用。"
UNKNOWN_STEP_TEXT = "请查看当前步骤。"
S4_FRIED_TEXT = "鸡蛋已经炒好盛出，把番茄倒入锅中翻炒。"

# 「现在该做什么」按步骤分支；与炉火相关的步骤分 未开火/已开火 两种文本。
STEP_TEXTS = {
    "S1": "拿起刀，把番茄切成块备用。",
    "S2": "把鸡蛋打进碗里，搅打均匀。",
    "S3": {False: "先点炉灶旋钮开火。", True: "火已经打开，把蛋液倒入锅中。"},
    "S4": {False: "先点旋钮开火，再倒入蛋液。", True: "倒入蛋液，炒至凝固后盛到盘里。"},
    "S5": {False: "先点旋钮开火，再把番茄倒入锅中翻炒。", True: "把番茄倒入锅中，翻炒至出汁。"},
}

S6_PHASES = {
    "return_cooked_egg": ("把盘里的熟鸡蛋拖回锅里，和番茄一起炒。", "plate"),
    "stir_fry": ("按住锅内绕一圈，把熟蛋和番茄炒匀。", "pan"),
    "turn_off_stove": ("食材已经炒匀，点炉灶旋钮关火。", "stove_switch"),
    "finish_dish": ("炉火已关，把锅中的成菜装入空盘。", "plate"),
}
S6_DEFAULT = ("请查看当前步骤。", "")

ACTION_NAMES = {
    "cut_tomato": "切番茄",
    "beat_egg": "打鸡蛋",
    "turn_on_stove": "开火",
    "fry_egg": "炒蛋",
    "fry_tomato": "炒番茄",
    "finish_dish": "装盘",
    "return_cooked_egg": "熟蛋回锅",
    "stir_fry": "合炒",
    "turn_off_stove": "关火",
}
REASON_TEXT = {
    "stove_off": "炉灶还没打开",
    "previous_step": "上一步还没完成",
    "prep_incomplete": "备料还没完成",
}


def _egg_already_fried(snap, events) -> bool:
    """S4 是否已完成：completed_steps 含 S4，或最近有成功的炒蛋操作。"""
    return "S4" in snap.completed_steps or any(
        ev.action_id == "fry_egg" and ev.outcome == "ok" for ev in events
    )


def _step_text(snap, events) -> tuple[str, str]:
    """句二「现在该做什么」+ 可高亮的物体编号。"""
    step = snap.current_step
    if step == "done":
        return DONE_TEXT, ""
    if step == "S6":
        phase = snap.finish_phase or "return_cooked_egg"
        return S6_PHASES.get(phase, S6_DEFAULT)
    text = STEP_TEXTS.get(step)
    if text is None:
        return UNKNOWN_STEP_TEXT, ""
    if isinstance(text, dict):
        text = text[snap.stove_on]
    if step == "S4" and snap.stove_on and _egg_already_fried(snap, events):
        text = S4_FRIED_TEXT
    return text, ""


def _error_sentence(events) -> str:
    """句一「刚才的操作为什么没执行」；无受阻事件返回空串。"""
    if not events:
        return ""
    attempt = events[-1]
    if attempt.outcome != "blocked":
        return ""
    name = ACTION_NAMES.get(attempt.action_id)
    if not name:
        return ""
    if attempt.reason == "already_done":
        return f"刚才{name}已经完成，不用重复。"
    reason = REASON_TEXT.get(attempt.reason)
    return f"刚才{name}没执行：{reason}。" if reason else f"刚才{name}没执行。"


def fallback_guidance(req: GuidanceRequest) -> dict:
    snap = req.snapshot
    question = req.user_question or ""
    allowed = req.allowed_highlight_targets
    target = ""
    text, focus = _step_text(snap, req.recent_events)
    # 玩家问「哪里」：句二替换为位置提示，并在允许时高亮。
    if "哪里" in question:
        if snap.current_step == "S6" and focus in allowed:
            target = focus
        elif not snap.stove_on and "stove_switch" in allowed:
            text, target = WHERE_SWITCH_TEXT, "stove_switch"
    # trigger=error：句一（刚才为什么没执行）+ 句二（现在该做什么）。
    if req.trigger == "error":
        first = _error_sentence(req.recent_events)
        if first:
            text = first + text
    return {
        "message": text,
        "hint_level": "direct" if target else "check",
        "highlight_target_id": target,
    }
