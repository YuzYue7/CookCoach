"""TTS 合成服务：微软 edge-tts（免费、无密钥、神经网络语音，官方非公开 API）。

换官方 provider（Azure Speech REST / 火山引擎）时只改本模块内部实现，
保持同一签名 synthesize(text, voice) -> bytes；路由、契约与 Unity 客户端都不动。
失败向上抛，由路由转 502，客户端静默降级为纯文本。
"""
from collections import OrderedDict

from ..config import settings

# 相同提示反复出现（同一错误重试）时省一次外网调用；短消息 64 条约几 MB 封顶
_cache: OrderedDict[tuple[str, str], bytes] = OrderedDict()
_CACHE_MAX = 64


async def synthesize(text: str, voice: str) -> bytes:
    """文本 -> mp3 字节。任何失败向上抛。"""
    text = text.strip()
    if not text:
        raise ValueError("empty text")
    if len(text) > settings.tts_max_chars:
        raise ValueError("text too long")
    key = (voice, text)
    cached = _cache.get(key)
    if cached is not None:
        _cache.move_to_end(key)
        return cached

    import edge_tts  # 惰性导入：包缺失不影响 guidance 主链路

    communicate = edge_tts.Communicate(text, voice)
    parts = []
    async for chunk in communicate.stream():
        if chunk["type"] == "audio":
            parts.append(chunk["data"])
    audio = b"".join(parts)
    if not audio:
        raise RuntimeError("edge-tts returned no audio")
    _cache[key] = audio
    while len(_cache) > _CACHE_MAX:
        _cache.popitem(last=False)
    return audio
