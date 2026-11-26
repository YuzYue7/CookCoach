"""模型输出结构校验与钳制。

- guidance.message 非空（为空抛 ValueError，调用方走 fallback）
- hint_level ∈ {check, explain, direct}，非法降级 check；direct 无高亮降级 check
- highlight_target_id 从请求 allowed_highlight_targets 中取，否则留空
- 校验失败时修正到合法值（宁可保守也不 500）
"""
import json

from ..schemas import GuidanceRequest

HINT_LEVELS = {"check", "explain", "direct"}


def parse_model_output(raw: str) -> dict:
    """把模型原始输出解析为 dict；失败抛 ValueError（调用方走 fallback）。"""
    text = (raw or "").strip()
    if text.startswith("```"):
        text = text.strip("`")
        if text.lower().startswith("json"):
            text = text[4:]
    parsed = json.loads(text)
    if not isinstance(parsed, dict):
        raise ValueError("model output is not a JSON object")
    return parsed


def clamp_guidance(parsed: dict, req: GuidanceRequest) -> tuple[dict, list[str]]:
    """钳制模型输出到合法范围；message 为空时抛 ValueError。返回 (guidance, issues)。"""
    issues: list[str] = []
    message = parsed.get("message")
    if not isinstance(message, str) or not message.strip():
        raise ValueError("guidance.message must be non-empty text")
    message = message.strip()
    hint_level = parsed.get("hint_level")
    if hint_level not in HINT_LEVELS:
        issues.append(f"hint_level {hint_level!r} → check")
        hint_level = "check"
    target = str(parsed.get("highlight_target_id") or "").strip()
    snap = req.snapshot
    if req.recipe_id == "tomato_egg" and target:
        if snap.current_step == "done":
            raise ValueError("completed lesson cannot direct the player to a scene target")
        if (snap.current_step == "S6" and snap.finish_phase in (None, "", "return_cooked_egg")
                and target == "egg_bowl" and "碗" not in req.user_question
                and (req.trigger in ("hint", "error") or "熟蛋" in message or "熟鸡蛋" in message)):
            if "碗" in message:
                raise ValueError("cooked eggs are on the plate")
            issues.append("熟蛋回锅时，熟蛋在上菜盘")
            target = "plate"
    if target and target not in req.allowed_highlight_targets:
        issues.append(f"highlight_target_id {target!r} 不在 allowed 列表 → 留空")
        target = ""
    if hint_level == "direct" and not target:
        issues.append("direct 无高亮 → check")
        hint_level = "check"
    return {
        "message": message,
        "hint_level": hint_level,
        "highlight_target_id": target,
    }, issues
