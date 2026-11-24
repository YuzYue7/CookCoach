"""真实模型验收轨迹（需要 backend/.env 里的 DEEPSEEK_API_KEY，否则整体跳过）。

对应 docs/后端问题定义.md「什么叫完成」第 2 条：
打蛋→切番茄→未开火炒蛋受阻→提示先开火；追问「开关在哪」→高亮旋钮；
开火后再问→提示倒蛋；S6 四个 finish_phase 各对。
运行：pytest tests/test_acceptance_trajectory.py -s
"""
import time
import uuid

import pytest
from fastapi.testclient import TestClient

from app.config import settings
from app.main import app

pytestmark = pytest.mark.skipif(
    not settings.deepseek_api_key,
    reason="DEEPSEEK_API_KEY not configured in backend/.env",
)

client = TestClient(app)
SESSION_ID = uuid.uuid4().hex


def _ask(**overrides) -> dict:
    base = dict(
        request_id=uuid.uuid4().hex,
        session_id=SESSION_ID,
        recipe_id="tomato_egg",
        recipe_version="1",
        trigger="question",
        user_question="",
        state_version=1,
        snapshot=dict(
            completed_steps=[], stove_on=False, related_error_count=0,
            current_step="S1", current_step_title="", step_number=1, step_count=6,
        ),
        recent_events=[],
        conversation=[],
        allowed_highlight_targets=[],
    )
    base.update(overrides)
    started = time.perf_counter()
    response = client.post("/api/v1/guidance", json=base)
    elapsed = time.perf_counter() - started
    assert response.status_code == 200
    assert elapsed < 20, f"总响应 {elapsed:.1f}s 超过客户端 20s 超时"
    return response.json()


def _print_qa(step: str, question: str, body: dict):
    g = body["guidance"]
    print(f"\n[{step}] 问：{question or '(自动触发)'}")
    print(f"  答({body['source']})：{g['message']} | 高亮={g['highlight_target_id'] or '无'}"
          f" | hint={g['hint_level']}")


def test_acceptance_trajectory():
    snap_s3 = dict(
        completed_steps=["S1", "S2"], stove_on=False, related_error_count=1,
        current_step="S3", current_step_title="开启炉灶", step_number=3, step_count=6,
    )
    blocked_fry = [dict(action_id="fry_egg", target_id="pan", outcome="blocked", reason="stove_off")]
    conversation = []

    # 1) 未开火尝试炒蛋 → 受阻，应提示先开火
    body = _ask(
        trigger="error",
        snapshot=snap_s3,
        recent_events=blocked_fry,
        conversation=conversation,
        allowed_highlight_targets=["stove_switch"],
    )
    msg1 = body["guidance"]["message"]
    assert body["source"] == "llm", "第一次出错必须由真实模型回答"
    assert any(k in msg1 for k in ("开火", "炉灶", "旋钮")), f"未提示先开火：{msg1}"
    _print_qa("出错提示", "操作受阻，请解释原因", body)
    conversation += [
        {"role": "user", "content": "操作受阻，请解释原因"},
        {"role": "assistant", "content": msg1},
    ]

    # 2) 追问「开关在哪里？」→ 结合历史，高亮 stove_switch
    body = _ask(
        trigger="question",
        user_question="开关在哪里？",
        snapshot=snap_s3,
        recent_events=blocked_fry,
        conversation=conversation,
        allowed_highlight_targets=["stove_switch"],
    )
    msg2 = body["guidance"]["message"]
    assert body["source"] == "llm"
    assert body["guidance"]["highlight_target_id"] == "stove_switch", \
        f"未高亮旋钮：{body['guidance']}"
    _print_qa("追问位置", "开关在哪里？", body)
    conversation += [
        {"role": "user", "content": "开关在哪里？"},
        {"role": "assistant", "content": msg2},
    ]

    # 3) 已开火，再问「现在呢？」→ 应按新炉火状态提示倒蛋
    body = _ask(
        trigger="question",
        user_question="现在呢？",
        snapshot=dict(
            completed_steps=["S1", "S2"], stove_on=True, related_error_count=1,
            current_step="S4", current_step_title="炒蛋", step_number=4, step_count=6,
        ),
        conversation=conversation,
        allowed_highlight_targets=["pan", "stove_switch"],
    )
    msg3 = body["guidance"]["message"]
    assert body["source"] == "llm"
    assert "蛋" in msg3, f"未提示倒蛋/炒蛋：{msg3}"
    assert "开火" not in msg3, f"炉火已开仍让玩家开火：{msg3}"
    _print_qa("开火后追问", "现在呢？", body)


def test_s6_finish_phases():
    keywords = {
        "return_cooked_egg": ("熟蛋", "回锅", "盘"),
        "stir_fry": ("炒", "绕圈"),
        "turn_off_stove": ("关火", "旋钮"),
        "finish_dish": ("装盘", "盘"),
    }
    stove_by_phase = {
        "return_cooked_egg": True,
        "stir_fry": True,
        "turn_off_stove": True,
        "finish_dish": False,
    }
    for phase, kws in keywords.items():
        body = _ask(
            trigger="question",
            user_question="然后呢？",
            snapshot=dict(
                completed_steps=["S1", "S2", "S3", "S4", "S5"],
                stove_on=stove_by_phase[phase], related_error_count=0,
                current_step="S6", current_step_title="装盘",
                step_number=6, step_count=6, finish_phase=phase,
            ),
            allowed_highlight_targets=["pan", "stove_switch", "plate"],
        )
        msg = body["guidance"]["message"]
        assert body["source"] == "llm", f"{phase} 未走真实模型"
        assert any(k in msg for k in kws), f"{phase} 提示不符：{msg}"
        _print_qa(f"S6/{phase}", "然后呢？", body)
