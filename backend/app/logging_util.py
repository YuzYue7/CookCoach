"""JSONL 请求日志，backend/logs/ 下每请求一行。

每行含：时间戳、request_id、模型输入、原始输出、校验结果、耗时(ms)、source。
按 request_id 与客户端导出的 JSON 记录核对（组员联调交接.md 要求）。
日志目录运行时自动创建，已 gitignore。
"""
import datetime
import json
import threading
from pathlib import Path

from .config import log_dir

_lock = threading.Lock()


def log_guidance(record: dict) -> None:
    """追加一行 JSONL；目录按需创建，多请求并发安全。"""
    directory = log_dir()
    directory.mkdir(parents=True, exist_ok=True)
    path: Path = directory / f"tutor-{datetime.date.today():%Y%m%d}.jsonl"
    line = json.dumps(record, ensure_ascii=False, default=str)
    with _lock:
        with path.open("a", encoding="utf-8") as f:
            f.write(line + "\n")
