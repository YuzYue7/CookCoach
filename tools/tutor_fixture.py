"""Local HTTP contract fixture for the Unity client. Uses Python standard library only."""
import json
import time
import sys
from pathlib import Path
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer


sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "backend"))
from app.services.fallback import offline_guidance


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

        guidance = offline_guidance(req)
        if question == "test:target":
            guidance.update(highlight_target_id="unregistered_fixture_target", hint_level="direct")
        if question == "test:stale":
            reply["state_version"] -= 1
        reply["source"] = "fallback" if question == "test:fallback" else "mock"
        reply["guidance"] = guidance
        self.respond(200, reply)


if __name__ == "__main__":
    with ThreadingHTTPServer(("127.0.0.1", 18741), Fixture) as server:
        print("Tutor HTTP fixture listening on 127.0.0.1:18741", flush=True)
        server.serve_forever()

