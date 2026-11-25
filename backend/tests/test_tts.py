"""POST /api/v1/tts 契约测试：合成函数打桩，不访问外网。"""
import pytest
from fastapi.testclient import TestClient

from app.config import settings
from app.main import app
from app.services import tts_service

client = TestClient(app)
FAKE_MP3 = b"ID3\x03\x00fake-mp3-bytes"


@pytest.fixture(autouse=True)
def _clear_cache():
    tts_service._cache.clear()
    yield


def _post(text="先点炉灶旋钮开火。"):
    return client.post("/api/v1/tts", json={"text": text})


def test_success_returns_audio_mpeg(monkeypatch):
    async def fake(text, voice):
        assert text == "先点炉灶旋钮开火。"
        assert voice == settings.tts_voice
        return FAKE_MP3

    monkeypatch.setattr(tts_service, "synthesize", fake)
    response = _post()
    assert response.status_code == 200
    assert response.headers["content-type"].startswith("audio/mpeg")
    assert response.content == FAKE_MP3


def test_missing_text_is_400():
    assert client.post("/api/v1/tts", json={}).status_code == 400


def test_blank_text_is_400():
    assert _post("   ").status_code == 400


def test_too_long_text_is_400():
    assert _post("长" * 501).status_code == 400


def test_disabled_returns_503(monkeypatch):
    monkeypatch.setattr(settings, "tts_enabled", False)
    try:
        assert _post().status_code == 503
    finally:
        monkeypatch.setattr(settings, "tts_enabled", True)


def test_synthesis_failure_returns_502(monkeypatch):
    async def boom(text, voice):
        raise RuntimeError("network down")

    monkeypatch.setattr(tts_service, "synthesize", boom)
    response = _post()
    assert response.status_code == 502
    assert response.headers["content-type"].startswith("application/json")
