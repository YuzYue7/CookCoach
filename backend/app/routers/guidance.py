"""POST /api/v1/guidance：核心导师接口。

流程：校验请求 → prompt 组装 → 调 llm_client（10s 超时）→
校验/钳制输出 → 原样回传 request_id/session_id/state_version → 写 JSONL 日志。
模型超时/不可用/输出不可用：走 services/fallback 规则表，source="fallback"（不是 5xx）。
总响应 < 20 秒（客户端超时即丢弃）。
"""
import time

from fastapi import APIRouter

from ..logging_util import log_guidance
from ..schemas import Guidance, GuidanceRequest, GuidanceResponse
from ..services import fallback, llm_client, prompt, validate

router = APIRouter()


@router.post("/api/v1/guidance", response_model=GuidanceResponse)
def create_guidance(req: GuidanceRequest) -> GuidanceResponse:
    started = time.perf_counter()
    messages = prompt.build_messages(req)
    raw_output = ""
    model_latency_ms = 0.0
    try:
        raw_output, model_latency_ms = llm_client.call_model(messages)
        parsed = validate.parse_model_output(raw_output)
        guidance_dict, issues = validate.clamp_guidance(parsed, req)
        source = "llm"
        validation_report = {"ok": True, "issues": issues}
    except Exception as exc:
        # 模型超时/不可用/输出不可用 → fallback 规则表，绝不 5xx
        source = "fallback"
        guidance_dict = fallback.fallback_guidance(req)
        validation_report = {"ok": False, "error": f"{type(exc).__name__}: {str(exc)[:300]}"}
    latency_ms = (time.perf_counter() - started) * 1000
    log_guidance({
        "ts": time.strftime("%Y-%m-%dT%H:%M:%S"),
        "request_id": req.request_id,
        "session_id": req.session_id,
        "state_version": req.state_version,
        "trigger": req.trigger,
        "current_step": req.snapshot.current_step,
        "source": source,
        "latency_ms": round(latency_ms, 1),
        "model_latency_ms": round(model_latency_ms, 1),
        "model_input": messages,
        "raw_output": raw_output,
        "validation": validation_report,
    })
    return GuidanceResponse(
        request_id=req.request_id,
        session_id=req.session_id,
        state_version=req.state_version,
        source=source,
        guidance=Guidance(**guidance_dict),
    )
