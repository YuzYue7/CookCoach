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
from __future__ import annotations

from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from ..schemas import GuidanceRequest

DONE_TEXT = "本轮已完成，可以打开本轮回顾，查看做饭清单和操作记录。"

# 只标记用户明确询问、且本轮 Unity 已登记的物体。
TARGET_NAMES = {
    "oil_bottle": ("油瓶", "食用油", "油壶", "倒油"),
    "salt_shaker": ("盐罐", "盐瓶", "盐"),
    "egg_bowl": ("蛋碗", "碗", "打蛋盆"),
    "stove_switch": ("旋钮", "炉灶", "开关", "灶台开关"),
    "plate": ("盘", "碟"),
    "pan": ("锅", "合炒", "翻炒"),
    "tomato": ("番茄", "西红柿", "砧板", "案板"),
}
TARGET_LABELS = {
    "oil_bottle": "食用油瓶", "salt_shaker": "盐罐",
    "egg_bowl": "蛋碗", "stove_switch": "炉灶旋钮", "plate": "上菜盘",
    "pan": "平底锅", "tomato": "切番茄的砧板",
}
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
    "return_cooked_egg": "把盘里的熟鸡蛋拖回锅里，和番茄一起炒。",
    "stir_fry": "按住锅内绕一圈，把熟蛋和番茄炒匀。",
    "turn_off_stove": "食材已经炒匀，点炉灶旋钮关火。",
    "finish_dish": "炉火已关，把锅中的成菜装入空盘。",
}

ACTION_NAMES = {
    "add_oil": "倒油", "add_salt": "撒盐",
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
    "oil_missing": "需要先往锅里倒油",
    "oil_window": "只有开火后、炒蛋前可以倒油",
    "salt_window": "只有熟蛋回锅后、合炒前可以撒盐",
    "stove_off": "炉灶还没打开",
    "previous_step": "上一步还没完成",
    "prep_incomplete": "备料还没完成",
}


def _egg_already_fried(snap: dict, events: list) -> bool:
    """S4 是否已完成：completed_steps 含 S4，或最近有成功的炒蛋操作。"""
    return "S4" in (snap.get("completed_steps") or []) or any(
        ev.get("action_id") == "fry_egg" and ev.get("outcome") == "ok" for ev in events
    )


def _step_text(snap: dict, events: list) -> str:
    """句二「现在该做什么」。"""
    step = snap.get("current_step")
    stove_on = bool(snap.get("stove_on"))
    if step == "S6":
        phase = snap.get("finish_phase") or "return_cooked_egg"
        if phase == "stir_fry" and snap.get("salt_added") is False:
            return "可把盐罐拖到锅里撒盐，也可直接绕圈合炒。"
        return S6_PHASES.get(phase, UNKNOWN_STEP_TEXT)
    text = STEP_TEXTS.get(step)
    if text is None:
        return UNKNOWN_STEP_TEXT
    if isinstance(text, dict):
        text = text[stove_on]
    if step == "S3" and stove_on and snap.get("oil_added") is False:
        return "炉火已开启，先把油瓶拖到锅里倒油。"
    if step == "S4" and stove_on and _egg_already_fried(snap, events):
        text = S4_FRIED_TEXT
    elif step == "S4" and stove_on and snap.get("oil_added") is False:
        text = "先把食用油瓶拖到锅上倒油，再倒入蛋液。"
    return text


def _error_sentence(events) -> str:
    """句一「刚才的操作为什么没执行」；无受阻事件返回空串。"""
    if not events:
        return ""
    attempt = events[-1]
    if attempt.get("outcome") != "blocked":
        return ""
    name = ACTION_NAMES.get(attempt.get("action_id"))
    if not name:
        return ""
    if attempt.get("reason") == "already_done":
        return f"刚才{name}已经完成，不用重复。"
    reason = REASON_TEXT.get(attempt.get("reason"))
    return f"刚才{name}没执行：{reason}。" if reason else f"刚才{name}没执行。"


def _location_guidance(question: str, allowed: list, snap: dict) -> tuple[str, str]:
    matches = {target for target, names in TARGET_NAMES.items()
               if any(name in question for name in names)}
    if "熟鸡蛋" in question or "熟蛋" in question:
        if snap.get("current_step") == "S5":
            matches.add("plate")
        elif snap.get("current_step") == "S6":
            phase = snap.get("finish_phase") or "return_cooked_egg"
            matches.add("plate" if phase == "return_cooked_egg" else "pan")
    if len(matches) != 1:
        return "你想找哪件道具？可以直接问我蛋碗、砧板、锅、盘子或炉灶旋钮的位置。", ""
    target = matches.pop()
    label = TARGET_LABELS[target]
    if target not in allowed:
        return f"当前场景无法标记{label}，可以询问其他已登记的道具。", ""
    return f"看金色圆圈，标出的位置就是{label}。", target


def offline_guidance(req: dict) -> dict:
    """供 FastAPI 和无第三方依赖的本地模拟服务共用。"""
    snap = req.get("snapshot") or {}
    events = req.get("recent_events") or []
    question = req.get("user_question") or ""
    status = []
    if req.get("recipe_version") != "1" and "油" in question and snap.get("oil_added") is not None:
        status.append("本轮已倒油。" if snap["oil_added"] else "本轮还没倒油。")
    if req.get("recipe_version") != "1" and "盐" in question and snap.get("salt_added") is not None:
        status.append("本轮已撒盐。" if snap["salt_added"] else "本轮未撒盐，撒盐是可选的。")
    target = ""
    if snap.get("current_step") == "done":
        text = "".join(status) + DONE_TEXT
    elif any(word in question for word in ("在哪", "哪里", "哪儿", "哪边", "位置", "找不到")):
        text, target = _location_guidance(question, req.get("allowed_highlight_targets") or [], snap)
    else:
        text = "".join(status) + _step_text(snap, events)
        if req.get("trigger") == "error":
            text = _error_sentence(events) + text
    return {
        "message": text,
        "hint_level": "direct" if target else "check",
        "highlight_target_id": target,
    }


def fallback_guidance(req: GuidanceRequest) -> dict:
    return offline_guidance(req.model_dump())
