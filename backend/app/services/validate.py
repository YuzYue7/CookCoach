"""模型输出结构校验与钳制。

- guidance.message 非空（为空抛 ValueError，调用方走 fallback）
- hint_level ∈ {check, explain, direct}，非法降级 check；direct 无高亮降级 check
- highlight_target_id 从请求 allowed_highlight_targets 中取，否则留空
- 校验失败时修正到合法值（宁可保守也不 500）
"""
import json

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


def clamp_guidance(parsed: dict, allowed_highlight_targets: list[str]) -> tuple[dict, list[str]]:
    """钳制模型输出到合法范围；message 为空时抛 ValueError。返回 (guidance, issues)。"""
    issues: list[str] = []
    message = str(parsed.get("message", "")).strip()
    if not message:
        raise ValueError("guidance.message is empty")
    hint_level = parsed.get("hint_level")
    if hint_level not in HINT_LEVELS:
        issues.append(f"hint_level {hint_level!r} → check")
        hint_level = "check"
    target = str(parsed.get("highlight_target_id") or "").strip()
    if target and target not in allowed_highlight_targets:
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
