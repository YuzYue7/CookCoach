"""环境变量配置（pydantic-settings）。

变量名见 .env.example：
DEEPSEEK_API_KEY / DEEPSEEK_BASE_URL / DEEPSEEK_MODEL /
TUTOR_MODEL_TIMEOUT_SECONDS(默认 10) / TUTOR_HOST(默认 127.0.0.1) /
TUTOR_PORT(默认 8000) / TUTOR_LOG_DIR(默认 logs) /
TTS_ENABLED(默认 true) / TTS_VOICE(默认 zh-CN-YunxiNeural) /
TTS_MAX_CHARS(默认 500)。
真实密钥只在本地 .env，绝不入库。
"""
from pathlib import Path

from pydantic import Field, field_validator
from pydantic_settings import BaseSettings, SettingsConfigDict

BACKEND_DIR = Path(__file__).resolve().parent.parent


class Settings(BaseSettings):
    model_config = SettingsConfigDict(env_file=str(BACKEND_DIR / ".env"), extra="ignore")

    deepseek_api_key: str = ""
    deepseek_base_url: str = "https://api.deepseek.com"
    deepseek_model: str = "deepseek-chat"
    tutor_model_timeout_seconds: float = 10.0
    tutor_host: str = "127.0.0.1"
    tutor_port: int = 8000
    tutor_log_dir: str = "logs"
    # 语音朗读（微软 edge-tts，免费无密钥；失败时客户端自动降级为纯文本）
    tts_enabled: bool = True
    tts_voice: str = "zh-CN-YunxiNeural"
    tts_max_chars: int = Field(default=500, ge=1, le=500)

    @field_validator("deepseek_api_key")
    @classmethod
    def _clean_key(cls, v: str) -> str:
        # 粘贴密钥时常带引号/空白包裹，统一剥掉
        return v.strip().strip("'\"‘’“”")

    @field_validator("deepseek_base_url", "deepseek_model")
    @classmethod
    def _trim(cls, v: str) -> str:
        return v.strip()


settings = Settings()


def log_dir() -> Path:
    """日志目录：相对路径按 backend/ 解析。"""
    path = Path(settings.tutor_log_dir)
    return path if path.is_absolute() else BACKEND_DIR / path
