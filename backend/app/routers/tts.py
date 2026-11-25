"""POST /api/v1/tts：文本转语音（可选增强）。

请求 {"text": "..."}（≤500 字符），成功 200 audio/mpeg mp3 二进制。
TTS_ENABLED=false → 503；合成失败 → 502；校验失败沿用全局 400 处理。
与 /api/v1/guidance 契约互不影响；客户端对任何非 200 静默降级纯文本。
"""
import asyncio
import time

from fastapi import APIRouter
from fastapi.responses import JSONResponse, Response

from ..config import settings
from ..logging_util import log_tts
from ..schemas import TtsRequest
from ..services import tts_service

router = APIRouter()


@router.post("/api/v1/tts")
async def create_tts(req: TtsRequest) -> Response:
    started = time.perf_counter()
    if not settings.tts_enabled:
        return JSONResponse(status_code=503, content={"detail": "tts disabled"})
    text = req.text
    if len(text) > settings.tts_max_chars:
        return JSONResponse(status_code=400, content={"detail": "invalid request"})
    try:
        audio = await asyncio.wait_for(tts_service.synthesize(text, settings.tts_voice), timeout=10)
    except Exception as exc:
        log_tts({
            "ts": time.strftime("%Y-%m-%dT%H:%M:%S"), "ok": False,
            "text_chars": len(text), "voice": settings.tts_voice,
            "error": type(exc).__name__,
            "latency_ms": round((time.perf_counter() - started) * 1000, 1),
        })
        return JSONResponse(status_code=502, content={"detail": "tts synthesis failed"})
    log_tts({
        "ts": time.strftime("%Y-%m-%dT%H:%M:%S"), "ok": True,
        "text_chars": len(text), "voice": settings.tts_voice, "bytes": len(audio),
        "latency_ms": round((time.perf_counter() - started) * 1000, 1),
    })
    return Response(content=audio, media_type="audio/mpeg",
                    headers={"X-TTS-Voice": settings.tts_voice})
