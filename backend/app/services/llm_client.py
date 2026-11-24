"""OpenAI 兼容的模型调用层（DeepSeek），内部超时 10 秒。

约束：超时/异常向上抛，由调用方走 fallback，不在这里吞掉或返回 5xx。
换 provider 只改配置（base_url/model），不动本模块。
"""
import functools
import time

import httpx
from openai import OpenAI

from ..config import settings


@functools.lru_cache(maxsize=1)
def _client() -> OpenAI:
    return OpenAI(api_key=settings.deepseek_api_key, base_url=settings.deepseek_base_url)


def call_model(messages: list[dict]) -> tuple[str, float]:
    """调用模型，返回 (原始文本, 耗时 ms)。超时/异常向上抛。

    模型偶发返回空输出时重试一次；仅当首次耗时 < 5s 才重试，
    保证总耗时仍远低于客户端 20s 超时。
    """
    latency_ms = 0.0
    for attempt in (1, 2):
        started = time.perf_counter()
        response = _client().chat.completions.create(
            model=settings.deepseek_model,
            messages=messages,
            temperature=0.2,
            max_tokens=300,
            response_format={"type": "json_object"},
            # 单次预算 10s：连接 5s 兜底，其余 10s
            timeout=httpx.Timeout(settings.tutor_model_timeout_seconds, connect=5.0),
        )
        latency_ms = (time.perf_counter() - started) * 1000
        text = (response.choices[0].message.content or "").strip()
        if text or attempt == 2 or latency_ms >= 5000:
            return text, latency_ms
    return "", latency_ms
