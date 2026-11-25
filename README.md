# CookCoach

COMP5140 Group 8 的桌面 3D 厨房。玩家在 Unity 里做一道番茄炒蛋，导师根据炉火、食材、最近操作和提问给出中文提示。询问道具位置时，导师可以标记厨房里的具体物品。连接后端后可以朗读提示。

## 启动厨房

1. 安装 Git LFS，从本仓库克隆工程，在仓库根目录执行 `git lfs pull`，下载厨房模型与界面图片。
2. 用 Unity Hub 打开 `unity/`。仓库指定编辑器为 `2022.3.62f1`，本地桌面预览使用 `6000.6.4f1`。打开 `Assets/Scenes/KitchenTutorPractice.unity`。
3. 直接点 **Play** 即可使用默认的 **Local Preview** 模式完成练习。使用在线导师时，先启动下方的后端，在场景的 **KitchenTutor** 组件上把 **Mode** 设为 **Http**，将 **Endpoint** 填为 `http://127.0.0.1:8000/api/v1/guidance`，再进入 Play。

### 后端与语音

在仓库根目录打开 PowerShell，使用 Python 3.12 创建环境。

```powershell
cd backend
py -3.12 -m venv .venv
.\.venv\Scripts\python.exe -m pip install -r requirements.txt
if (!(Test-Path .env)) { Copy-Item .env.example .env }
```

在本机的 `backend/.env` 中填写 `DEEPSEEK_API_KEY`。默认模型为 `deepseek-chat`。语音可单独配置，`TTS_ENABLED=true` 启用朗读，`TTS_VOICE` 默认使用 `zh-CN-YunxiNeural`。Edge TTS 通过网络合成，只发送要朗读的提示文本，无需另配语音密钥。玩家可以在导师卡片上关闭朗读，偏好保存在本机。

```powershell
.\.venv\Scripts\python.exe -m uvicorn app.main:app --host 127.0.0.1 --port 8000
```

打开 `http://127.0.0.1:8000/health` 可检查服务是否启动。在 Unity 点「提问」，然后打开「交互记录」检查回复来源。`llm` 表示实际模型回复，`fallback` 表示后端根据当前菜谱状态提供提示。两者都支持朗读。语音服务不可用时，文字、高亮和烹饪仍可使用。

## 玩家操作

**WASD 或方向键**移动，**按住鼠标右键**环视，**F** 回到灶台，**Esc** 取消拖动。鼠标停在道具上可查看动作。

1. 点刀，再点番茄三次切块。按住蛋碗边缘绕圈打蛋。这两项可以交换顺序。
2. 点炉灶旋钮开火，把黑色橄榄油瓶拖到锅口松开，再把蛋碗拖入锅中。炒好的蛋会暂时盛到右侧盘里。
3. 把番茄块拖入锅，再把盘中的熟蛋拖回锅中。此时可把白色盐罐拖到锅口撒一次盐，也可直接合炒。
4. 按住锅内绕圈合炒，点旋钮关火，再把锅拖向空盘装盘。提前装盘会被拦住。完成后可复制做饭清单、查看受阻和求助次数，或重新练习。

## 项目架构

| 位置 | 职责 |
| --- | --- |
| `unity/Assets/Scenes/` | 厨房场景、相机和导师实例。 |
| `unity/Assets/CookCoachTutor/Runtime/` | `KitchenWalk`、`CookingHands` 和 `KitchenWorkbench` 处理走动、手势与道具。`StovePractice`、`RecipeRehearsal` 和 `TomatoEggLesson` 管理练习与菜谱规则。`KitchenTutor` 展示文字、定位道具和播放语音，`TutorJournal` 保存本轮记录。 |
| `unity/Assets/CookCoachTutor/Art/` | UI Toolkit 导师卡片、样式和头像。 |
| `backend/app/` | FastAPI 路由、数据校验与配置。`services/` 负责提示词、DeepSeek 调用、回复校验、备用提示和 Edge TTS。 |
| `shared/` | 指导与语音请求样例、指导响应样例、语音二进制响应说明。 |
| `backend/tests/`、`tools/`、`docs/` | 后端测试、本地 HTTP 模拟服务、客户端验收记录和组员联调说明。 |

玩家操作后，Unity 判定动作并更新食材与炉火状态。遇到错误或主动求助时，Unity 将状态、近期操作、对话和可高亮物体发送给 FastAPI。Unity 按请求编号、会话和状态版本核对回复，再显示文字并执行有效的高亮。开启朗读时，Unity 将已经显示的提示单独提交给语音接口，收到 MP3 后播放。语音不阻塞下一次操作。

| 接口 | 输入与结果 |
| --- | --- |
| `GET /health` | 返回服务运行状态。 |
| `POST /api/v1/guidance` | 接收练习快照、事件、问题与目标列表。返回导师文字、提示级别、可选高亮目标及回复来源。 |
| `POST /api/v1/tts` | 接收 `{"text":"先点炉灶旋钮开火。"}`。成功返回 `audio/mpeg` 的 MP3。文本无效返回 400，语音关闭返回 503，合成失败或超时返回 502。文本上限默认 500 字符，可通过 `TTS_MAX_CHARS` 调低。 |

## 验证与记录

- 在 `backend/` 运行 `.\.venv\Scripts\python.exe -m pytest -q -rs`。真实模型验收用例需要有效的 DeepSeek 密钥，测试输出会列出跳过原因。
- 验证朗读时，在 **Http** 模式提问并等待文字与声音。播放中关闭语音应立即停止，再开始新一轮时，上一轮的提示应当失效。
- 仓库根目录运行 `python tools/tutor_fixture.py` 可单独检查 Unity 的 HTTP 指导链路，地址为 `http://127.0.0.1:18741/api/v1/guidance`。模拟服务返回 `source=mock`，语音请求返回 501。
- 从 Unity「交互记录」导出本轮 JSON，按 `request_id` 对照 `backend/logs/tutor-YYYYMMDD.jsonl` 中的模型输入、原始输出和校验结果。语音请求的合成耗时与结果保存在 `tts-YYYYMMDD.jsonl`。
- 协作时保留 Unity 资源对应的 `.meta`。变更接口后同步更新 `shared/` 的样例，密钥保存在本机 `backend/.env`。更多用例见[客户端验收说明](docs/导师客户端说明与测试.md)和[联调交接](docs/组员联调交接.md)。
