"""Local HTTP contract fixture for the Unity client. Uses Python standard library only."""
import json
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer


class Fixture(BaseHTTPRequestHandler):
    def respond(self, status, value):
        body = json.dumps(value, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        try:
            self.wfile.write(body)
        except (BrokenPipeError, ConnectionResetError, ConnectionAbortedError):
            pass

    def do_GET(self):
        if self.path == "/health":
            self.respond(200, {"status": "ok", "source": "mock"})
        else:
            self.respond(404, {})

    def do_POST(self):
        if self.path != "/api/v1/guidance":
            return self.respond(404, {})
        size = int(self.headers.get("Content-Length", "0"))
        if not 0 < size <= 131072:
            return self.respond(400, {"detail": "invalid body length"})
        try:
            req = json.loads(self.rfile.read(size))
            question = req["user_question"]
            reply = {key: req[key] for key in ("session_id", "request_id", "state_version")}
            on = req["snapshot"]["stove_on"]
            point = not on and "哪里" in question and "stove_switch" in req["allowed_highlight_targets"]
        except (ValueError, TypeError, KeyError):
            return self.respond(400, {"detail": "invalid fixture request"})

        if question == "test:500":
            return self.respond(503, {"detail": "fixture unavailable"})
        if question == "test:bad-json":
            self.send_response(200)
            self.end_headers()
            self.wfile.write(b"not-json")
            return
        time.sleep(24 if question == "test:timeout" else 1 if question == "test:slow" else 0.15)
        target = "stove_switch" if point else ""
        text = "先检查炉灶是否已经开启。"
        if point:
            text = "看金色圆圈，点击它指向的炉灶旋钮。"
        elif on:
            text = "火已经打开，可以倒入蛋液。"
        if req["snapshot"].get("current_step") == "S6":
            phase = req["snapshot"].get("finish_phase") or "return_cooked_egg"
            text, focus = {
                "return_cooked_egg": ("把盘里的熟鸡蛋拖回锅里，和番茄一起炒。", "plate"),
                "stir_fry": ("按住锅内绕一圈，把熟蛋和番茄炒匀。", "pan"),
                "turn_off_stove": ("食材已经炒匀，点左侧旋钮关火。", "stove_switch"),
                "finish_dish": ("炉火已关，把锅中的成菜装入空盘。", "plate"),
            }.get(phase, ("请查看当前步骤。", ""))
            target = focus if "哪里" in question and focus in req["allowed_highlight_targets"] else ""
        if req.get("trigger") == "error" and req.get("recent_events"):
            attempt = req["recent_events"][-1]
            action = {"return_cooked_egg": "熟蛋回锅", "stir_fry": "合炒", "turn_off_stove": "关火", "finish_dish": "装盘"}.get(attempt.get("action_id"))
            if action and attempt.get("outcome") == "blocked":
                suffix = "已经完成。" if attempt.get("reason") == "already_done" else "未执行。"
                text = action + suffix + text
        if question == "test:target":
            target = "unregistered_fixture_target"
        if question == "test:stale":
            reply["state_version"] -= 1
        reply["source"] = "fallback" if question == "test:fallback" else "mock"
        reply["guidance"] = {"message": text, "hint_level": "direct" if target else "check",
                             "highlight_target_id": target}
        self.respond(200, reply)


if __name__ == "__main__":
    with ThreadingHTTPServer(("127.0.0.1", 18741), Fixture) as server:
        print("Tutor HTTP fixture listening on 127.0.0.1:18741", flush=True)
        server.serve_forever()

