"""FastAPI 应用入口。

启动命令：`uvicorn app.main:app --host 127.0.0.1 --port 8000`。
约束：总响应 < 20 秒（客户端超时）；密钥只从环境变量读取。
"""
from fastapi import FastAPI, Request
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse

from .routers import guidance, health, tts

app = FastAPI(title="CookCoach Tutor", version="0.1.0")


@app.exception_handler(RequestValidationError)
async def handle_validation_error(request: Request, exc: RequestValidationError):
    # 与 fixture 行为一致：非法请求返回 400 + detail
    return JSONResponse(status_code=400, content={"detail": "invalid request"})


app.include_router(health.router)
app.include_router(guidance.router)
app.include_router(tts.router)
