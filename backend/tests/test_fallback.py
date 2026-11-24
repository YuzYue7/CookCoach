"""fallback 规则表测试：模型不可用时的备用提示分支。"""
from app.schemas import Event, GuidanceRequest, Snapshot
from app.services import fallback


def _req(**overrides) -> GuidanceRequest:
    base = dict(
        request_id="r",
        session_id="s",
        state_version=1,
        snapshot=Snapshot(),
        recent_events=[],
        conversation=[],
        allowed_highlight_targets=[],
    )
    base.update(overrides)
    return GuidanceRequest(**base)


def test_stove_off_question_highlights_switch():
    req = _req(
        user_question="开关在哪里？",
        trigger="question",
        snapshot=Snapshot(stove_on=False, current_step="S3"),
        allowed_highlight_targets=["stove_switch"],
    )
    g = fallback.fallback_guidance(req)
    assert g["highlight_target_id"] == "stove_switch"
    assert g["hint_level"] == "direct"
    assert "旋钮" in g["message"]


def test_stove_on_gives_pour_hint():
    req = _req(snapshot=Snapshot(stove_on=True, current_step="S4"))
    g = fallback.fallback_guidance(req)
    assert "蛋液" in g["message"]
    assert g["highlight_target_id"] == ""


def test_s6_phases():
    expected = {
        "return_cooked_egg": "熟鸡蛋",
        "stir_fry": "炒匀",
        "turn_off_stove": "关火",
        "finish_dish": "空盘",
    }
    for phase, keyword in expected.items():
        req = _req(
            snapshot=Snapshot(current_step="S6", finish_phase=phase),
            allowed_highlight_targets=["pan", "stove_switch", "plate"],
        )
        g = fallback.fallback_guidance(req)
        assert keyword in g["message"], phase
        assert g["message"].strip()


def test_error_trigger_prepends_blocked_action():
    req = _req(
        trigger="error",
        snapshot=Snapshot(current_step="S6", finish_phase="turn_off_stove"),
        recent_events=[
            Event(action_id="turn_off_stove", target_id="stove_switch",
                  outcome="blocked", reason="stove_on")
        ],
    )
    g = fallback.fallback_guidance(req)
    assert g["message"].startswith("关火未执行。")


def test_already_done_reason():
    req = _req(
        trigger="error",
        snapshot=Snapshot(current_step="S6", finish_phase="finish_dish"),
        recent_events=[
            Event(action_id="finish_dish", target_id="plate",
                  outcome="blocked", reason="already_done")
        ],
    )
    g = fallback.fallback_guidance(req)
    assert g["message"].startswith("装盘已经完成。")
