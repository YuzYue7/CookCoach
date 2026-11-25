"""fallback 规则表测试：模型不可用时的备用提示分支。

决策顺序：先看 current_step（句二「现在该做什么」），
再看最近一条受阻事件（句一「刚才为什么没执行」）。
"""
import pytest

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


def test_stove_on_s4_pours_egg():
    # S4 火开且鸡蛋未炒 → 倒蛋液
    req = _req(snapshot=Snapshot(stove_on=True, current_step="S4"))
    g = fallback.fallback_guidance(req)
    assert "蛋液" in g["message"]
    assert g["highlight_target_id"] == ""


def test_stove_on_s5_pours_tomato_not_egg():
    # 核心回归：S5 火开（鸡蛋已炒完）→ 提示倒番茄，绝不能提示倒蛋液
    req = _req(
        snapshot=Snapshot(
            stove_on=True, current_step="S5",
            completed_steps=["S1", "S2", "S3", "S4"],
        )
    )
    g = fallback.fallback_guidance(req)
    assert "番茄" in g["message"]
    assert "蛋液" not in g["message"]


def test_s4_egg_already_fried_moves_on_to_tomato():
    # S4 但最近 fry_egg 已成功（或 completed_steps 已含 S4）→ 不再提示倒蛋液
    for snap, events in (
        (Snapshot(stove_on=True, current_step="S4"),
         [Event(action_id="fry_egg", target_id="pan", outcome="ok")]),
        (Snapshot(stove_on=True, current_step="S4",
                  completed_steps=["S1", "S2", "S3", "S4"]),
         []),
    ):
        g = fallback.fallback_guidance(_req(snapshot=snap, recent_events=events))
        assert "蛋液" not in g["message"]
        assert "番茄" in g["message"]


def test_s2_stove_early_on_still_beats_egg():
    # 提前开火不改变当前步骤的提示：S2 → 打鸡蛋
    req = _req(snapshot=Snapshot(stove_on=True, current_step="S2"))
    g = fallback.fallback_guidance(req)
    assert "鸡蛋" in g["message"]
    assert "蛋液" not in g["message"]


def test_stove_off_s3_says_turn_on():
    req = _req(snapshot=Snapshot(stove_on=False, current_step="S3"))
    g = fallback.fallback_guidance(req)
    assert "开火" in g["message"]


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


@pytest.mark.parametrize("question", ["本轮记录在哪里？", "需要加盐吗？"])
def test_finished_lesson_returns_review_without_highlight(question):
    req = _req(snapshot=Snapshot(current_step="done", stove_on=False),
               user_question=question, allowed_highlight_targets=["plate", "stove_switch"])
    g = fallback.fallback_guidance(req)
    assert "已完成" in g["message"] and "回顾" in g["message"]
    assert all(word not in g["message"] for word in ("关火", "开火", "装盘"))
    assert g["highlight_target_id"] == ""


@pytest.mark.parametrize("step,question,allowed,target,word", [
    ("S2", "蛋碗在哪里？", ["egg_bowl", "stove_switch"], "egg_bowl", "蛋碗"),
    ("S6", "平底锅在哪儿？", ["pan", "plate"], "pan", "平底锅"),
    ("S2", "找不到蛋碗", ["stove_switch"], "", "无法标记"),
    ("S2", "盐在哪里？", ["stove_switch", "pan"], "", "哪件道具"),
    ("S3", "锅和开关在哪里？", ["stove_switch", "pan"], "", "哪件道具"),
])
def test_location_uses_the_requested_and_registered_object(step, question, allowed, target, word):
    req = _req(snapshot=Snapshot(current_step=step, stove_on=False),
               user_question=question, allowed_highlight_targets=allowed)
    g = fallback.fallback_guidance(req)
    assert g["highlight_target_id"] == target
    assert word in g["message"]
    assert g["hint_level"] == ("direct" if target else "check")


def test_unknown_step_safe_text():
    req = _req(snapshot=Snapshot(current_step="S9", stove_on=True))
    g = fallback.fallback_guidance(req)
    assert g["message"].strip()
    assert "蛋液" not in g["message"]


def test_error_trigger_two_sentences_with_reason():
    # 句一：刚才的操作为什么没执行（结合 reason）；句二：现在该做什么
    req = _req(
        trigger="error",
        snapshot=Snapshot(current_step="S3", stove_on=False),
        recent_events=[
            Event(action_id="fry_egg", target_id="pan",
                  outcome="blocked", reason="stove_off")
        ],
    )
    g = fallback.fallback_guidance(req)
    assert g["message"].startswith("刚才炒蛋没执行：炉灶还没打开。")
    assert "开火" in g["message"]


def test_error_trigger_previous_step_reason():
    req = _req(
        trigger="error",
        snapshot=Snapshot(current_step="S6", finish_phase="turn_off_stove"),
        recent_events=[
            Event(action_id="turn_off_stove", target_id="stove_switch",
                  outcome="blocked", reason="previous_step")
        ],
    )
    g = fallback.fallback_guidance(req)
    assert g["message"].startswith("刚才关火没执行：上一步还没完成。")
    assert "关火" in g["message"]


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
    assert g["message"].startswith("刚才装盘已经完成，不用重复。")
    assert "空盘" in g["message"]


def test_question_trigger_has_no_error_prefix():
    # 非 error 触发即使最近有受阻事件，也不拼句一
    req = _req(
        trigger="question",
        snapshot=Snapshot(current_step="S3", stove_on=False),
        recent_events=[
            Event(action_id="fry_egg", target_id="pan",
                  outcome="blocked", reason="stove_off")
        ],
    )
    g = fallback.fallback_guidance(req)
    assert not g["message"].startswith("刚才")
