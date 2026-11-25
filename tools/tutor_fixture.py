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

        snap = req["snapshot"]
        events = req.get("recent_events") or []
        step = snap.get("current_step")
        stove_on = snap.get("stove_on")
        target = ""

        step_texts = {
            "S1": "拿起刀，把番茄切成块备用。",
            "S2": "把鸡蛋打进碗里，搅打均匀。",
            "S3": {False: "先点炉灶旋钮开火。", True: "火已经打开，把蛋液倒入锅中。"},
            "S4": {False: "先点旋钮开火，再倒入蛋液。", True: "倒入蛋液，炒至凝固后盛到盘里。"},
            "S5": {False: "先点旋钮开火，再把番茄倒入锅中翻炒。", True: "把番茄倒入锅中，翻炒至出汁。"},
        }
        text = step_texts.get(step, "请查看当前步骤。")
        if isinstance(text, dict):
            text = text[stove_on]
        if step == "S4" and stove_on and (
            "S4" in (snap.get("completed_steps") or [])
            or any(e.get("action_id") == "fry_egg" and e.get("outcome") == "ok" for e in events)
        ):
            text = "鸡蛋已经炒好盛出，把番茄倒入锅中翻炒。"
        if step == "S6":
            phase = snap.get("finish_phase") or "return_cooked_egg"
            text, focus = {
                "return_cooked_egg": ("把盘里的熟鸡蛋拖回锅里，和番茄一起炒。", "plate"),
                "stir_fry": ("按住锅内绕一圈，把熟蛋和番茄炒匀。", "pan"),
                "turn_off_stove": ("食材已经炒匀，点炉灶旋钮关火。", "stove_switch"),
                "finish_dish": ("炉火已关，把锅中的成菜装入空盘。", "plate"),
            }.get(phase, ("请查看当前步骤。", ""))
            target = focus if "哪里" in question and focus in req["allowed_highlight_targets"] else ""
        if "哪里" in question and step != "S6" and not stove_on \
                and "stove_switch" in req["allowed_highlight_targets"]:
            text, target = "看金色圆圈，点击它指向的炉灶旋钮。", "stove_switch"
        if req.get("trigger") == "error" and events:
            attempt = events[-1]
            action = {
                "cut_tomato": "切番茄", "beat_egg": "打鸡蛋", "turn_on_stove": "开火",
                "fry_egg": "炒蛋", "fry_tomato": "炒番茄", "finish_dish": "装盘",
                "return_cooked_egg": "熟蛋回锅", "stir_fry": "合炒", "turn_off_stove": "关火",
            }.get(attempt.get("action_id"))
            if action and attempt.get("outcome") == "blocked":
                if attempt.get("reason") == "already_done":
                    first = f"刚才{action}已经完成，不用重复。"
                else:
                    reason = {
                        "stove_off": "炉灶还没打开", "previous_step": "上一步还没完成",
                        "prep_incomplete": "备料还没完成",
                    }.get(attempt.get("reason"))
                    first = f"刚才{action}没执行：{reason}。" if reason else f"刚才{action}没执行。"
                text = first + text
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

