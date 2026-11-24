"""契约测试：以 ../shared/*.example.json 打真实服务。

断言：回传 request_id/session_id/state_version 原样；source 合法；
guidance.message 非空；highlight_target_id ∈ allowed_highlight_targets 或为空；
hint_level 合法。
"""
import datetime
import json
from pathlib import Path

from fastapi.testclient import TestClient

from app.logging_util import log_dir
from app.main import app
from app.services import llm_client

SHARED_DIR = Path(__file__).resolve().parents[2] / "shared"
REQUEST = json.loads((SHARED_DIR / "guidance.request.example.json").read_text(encoding="utf-8"))

client = TestClient(app)

GOOD_LLM = (
    '{"message": "先点炉灶旋钮开火。", '
    '"hint_level": "direct", "highlight_target_id": "stove_switch"}'
)


def _post():
    return client.post("/api/v1/guidance", json=REQUEST)


def _assert_contract(body: dict):
    assert body["request_id"] == REQUEST["request_id"]
    assert body["session_id"] == REQUEST["session_id"]
    assert body["state_version"] == REQUEST["state_version"]
    assert body["source"] in {"llm", "fallback", "mock"}
    assert body["guidance"]["message"].strip()
    assert body["guidance"]["hint_level"] in {"check", "explain", "direct"}
    assert body["guidance"]["highlight_target_id"] in REQUEST["allowed_highlight_targets"] + [""]


def test_llm_success_echoes_ids(monkeypatch):
    monkeypatch.setattr(llm_client, "call_model", lambda messages: (GOOD_LLM, 12.3))
    body = _post().json()
    _assert_contract(body)
    assert body["source"] == "llm"
    assert body["guidance"]["highlight_target_id"] == "stove_switch"


def test_model_failure_uses_fallback(monkeypatch):
    def boom(messages):
        raise TimeoutError("model timeout")

    monkeypatch.setattr(llm_client, "call_model", boom)
    body = _post().json()
    _assert_contract(body)
    assert body["source"] == "fallback"


def test_unparseable_output_uses_fallback(monkeypatch):
    monkeypatch.setattr(llm_client, "call_model", lambda messages: ("not-json", 1.0))
    body = _post().json()
    _assert_contract(body)
    assert body["source"] == "fallback"


def test_highlight_clamped_to_allowed(monkeypatch):
    monkeypatch.setattr(
        llm_client,
        "call_model",
        lambda messages: (
            '{"message": "ok", "hint_level": "direct", "highlight_target_id": "pan"}',
            1.0,
        ),
    )
    body = _post().json()
    # pan 不在样例请求的 allowed_highlight_targets 里 → 留空，direct 降级 check
    assert body["guidance"]["highlight_target_id"] == ""
    assert body["guidance"]["hint_level"] == "check"


def test_invalid_body_is_400():
    response = client.post("/api/v1/guidance", json={"request_id": "missing-fields"})
    assert response.status_code == 400


def test_log_line_written(monkeypatch):
    monkeypatch.setattr(llm_client, "call_model", lambda messages: (GOOD_LLM, 1.0))
    _post().json()
    log_file = log_dir() / f"tutor-{datetime.date.today():%Y%m%d}.jsonl"
    lines = [
        json.loads(line)
        for line in log_file.read_text(encoding="utf-8").strip().splitlines()
    ]
    assert any(entry["request_id"] == REQUEST["request_id"] for entry in lines)
