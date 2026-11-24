"""GET /health：检查后端是否启动，返回 {"status":"ok"}。

注意：真实模型可用性不在这里体现，由一次指导请求确认（README 约定）。
"""
from fastapi import APIRouter

router = APIRouter()


@router.get("/health")
def health_check() -> dict:
    return {"status": "ok"}
