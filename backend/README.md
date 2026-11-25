# CookCoach 导师后端（backend/）

FastAPI 服务提供健康检查、导师指导和语音合成。契约、验收与分工见
`../shared/*.example.json`、`../docs/组员联调交接.md`、`../docs/后端问题定义.md`。

## 本地启动（Windows）

1. 建虚拟环境并安装依赖：
   `python -m venv .venv`
   `.venv\Scripts\activate`
   `pip install -r requirements.txt`
2. 复制 `.env.example` 为 `.env`，填入 `DEEPSEEK_API_KEY`（真实密钥只在本机，绝不提交）
3. 启动：`uvicorn app.main:app --host 127.0.0.1 --port 8000`
4. 验证：浏览器打开 `http://127.0.0.1:8000/health`，应返回 `{"status":"ok"}`

## 接口

| 接口 | 说明 |
|---|---|
| GET /health | 检查服务是否启动，返回 `{"status":"ok"}`（不含模型可用性） |
| POST /api/v1/guidance | 导师接口；请求/回复格式照 `../shared/` 两份样例 |
| POST /api/v1/tts | 接收提示文本并返回 MP3。可用 `TTS_ENABLED` 关闭合成，或用 `TTS_VOICE` 指定声音。请求样例和响应约定见 `../shared/tts.*`。 |

## 关键约束（实现前必读，摘自 docs/后端问题定义.md）

- 原样回传 `request_id` / `session_id` / `state_version`
- `source`：真实调用 = `llm`；超时/不可用 = `fallback`（不是 5xx）
- `highlight_target_id` 只能从本次请求的 `allowed_highlight_targets` 选，否则留空
- `hint_level` ∈ {check, explain, direct}；`trigger` ∈ {error, hint, question}
- 模型调用内部超时 10 秒，总响应 < 20 秒（客户端超时）
- S6 必须按 `snapshot.finish_phase` 区分 熟蛋回锅/合炒/关火/装盘
- JSONL 日志在 `logs/`（已 gitignore），每请求一行，按 `request_id` 与客户端记录核对

## 目录

| 路径 | 职责 |
|---|---|
| app/main.py | FastAPI 入口，注册路由 |
| app/config.py | 读环境变量（密钥/模型/超时/端口/日志目录） |
| app/schemas.py | 请求/回复模型，对齐 shared 样例 |
| app/routers/health.py | GET /health |
| app/routers/guidance.py | POST /api/v1/guidance 主流程 |
| app/services/llm_client.py | OpenAI 兼容模型调用（10s 超时） |
| app/services/prompt.py | 上下文组装与提示词 |
| app/services/fallback.py | 按步骤规则表备用提示 |
| app/services/validate.py | 输出校验（高亮钳制/message 非空/枚举） |
| app/logging_util.py | JSONL 请求日志 |
| tests/ | 契约测试（以 shared 样例为 fixture） |

## 联调交付包（给朱玉）

- 启动命令：`uvicorn app.main:app --host 127.0.0.1 --port 8000`
- 接口地址：`http://127.0.0.1:8000`
- 一次真实的「出错→追问」请求与回复（按 `request_id` 与客户端导出记录核对）
