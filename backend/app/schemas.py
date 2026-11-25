"""请求/回复 Pydantic 模型，字段对齐 shared/ 两份样例。

请求：request_id、session_id、recipe_id、recipe_version、trigger、
user_question、state_version、snapshot、recent_events(≤10)、
conversation(≤6)、allowed_highlight_targets。
S6 时 snapshot 额外带 finish_phase，未知额外字段容忍（extra="ignore"）。
回复：request_id、session_id、state_version 原样回传；
source ∈ {llm, fallback, mock}；guidance.message 非空；
hint_level ∈ {check, explain, direct}；
highlight_target_id 只能取自本次请求的 allowed_highlight_targets，否则留空。
"""
from typing import Literal, Optional

from pydantic import BaseModel, ConfigDict, Field, field_validator


class Event(BaseModel):
    model_config = ConfigDict(extra="ignore")

    action_id: str
    target_id: str = ""
    outcome: Literal["ok", "blocked"] = "ok"
    reason: str = ""


class ConversationTurn(BaseModel):
    model_config = ConfigDict(extra="ignore")

    role: Literal["user", "assistant"]
    content: str


class Snapshot(BaseModel):
    model_config = ConfigDict(extra="ignore")

    completed_steps: list[str] = Field(default_factory=list)
    stove_on: bool = False
    oil_added: Optional[bool] = None
    salt_added: Optional[bool] = None
    related_error_count: int = 0
    current_step: str = ""
    current_step_title: str = ""
    step_number: int = 0
    step_count: int = 6
    finish_phase: Optional[str] = None  # 仅 S6 出现：回锅/合炒/关火/装盘


class GuidanceRequest(BaseModel):
    model_config = ConfigDict(extra="ignore")

    request_id: str
    session_id: str
    recipe_id: str = "tomato_egg"
    recipe_version: str = "1"
    trigger: Literal["error", "hint", "question"] = "question"
    user_question: str = ""
    state_version: int = 0
    snapshot: Snapshot = Field(default_factory=Snapshot)
    recent_events: list[Event] = Field(default_factory=list)
    conversation: list[ConversationTurn] = Field(default_factory=list)
    allowed_highlight_targets: list[str] = Field(default_factory=list)


class Guidance(BaseModel):
    message: str
    hint_level: Literal["check", "explain", "direct"] = "check"
    highlight_target_id: str = ""


class GuidanceResponse(BaseModel):
    request_id: str
    session_id: str
    source: Literal["llm", "fallback", "mock"]
    state_version: int
    guidance: Guidance


class TtsRequest(BaseModel):
    """POST /api/v1/tts 请求：{"text": "..."}，≤500 字符。

    缺失/空白/超长由全局校验处理器转 400 {"detail": "invalid request"}。
    """

    model_config = ConfigDict(extra="ignore")

    text: str = Field(min_length=1, max_length=500)

    @field_validator("text")
    @classmethod
    def _strip_nonempty(cls, v: str) -> str:
        v = v.strip()
        if not v:
            raise ValueError("text must not be blank")
        return v
