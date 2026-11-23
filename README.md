# CookCoach
COMP5140 group project

## 开发环境

- Unity Editor：以 `unity/ProjectSettings/ProjectVersion.txt` 为准。
  开题 Demo 使用 `6000.6.4f1`，正式版本由两位 Unity 负责人确认。
- Python：计划使用 3.12。后端依赖由 `backend/requirements.txt` 记录。
- 首版以桌面鼠标和键盘操作为共同开发、测试入口。

## 本地启动

1. 克隆仓库，在 Unity Hub 中打开 `unity/`。
2. 本分支打开 `Assets/Scenes/KitchenTutorPractice.unity` 并点击 Play。默认进入桌面厨房。WASD 或方向键走动，按住鼠标右键环视，F 回到灶台。先点刀，再分三次点番茄；按住蛋碗边缘，用鼠标连续绕一圈。两项备料可交换顺序。点炉灶旋钮开火，将蛋碗拖到锅上松手，再拖番茄块入锅，随后将右侧盘中的熟蛋拖回锅中，按住锅内绕圈合炒，点旋钮关火，再把锅拖向空盘装盘。熟蛋回锅也可先点熟蛋、再点锅。提前装盘会提示原因，并在本轮记录中记为一次受阻。拖错可松手重来，Esc 可取消拖动。鼠标停在道具上显示动作。打开“交互记录”可重新开始，或切换模拟按钮、热锅小样。
3. 当前可使用本地提示，也可以运行 `python tools/tutor_fixture.py` 验证 HTTP。团队 FastAPI 服务交付后，将 KitchenTutor 的 Mode 设为 Http 并填写 Endpoint。
   环境变量名称见 `backend/.env.example`，实际密钥由开发者在本地配置。
4. `/health` 用于检查后端服务。真实模型是否可用，通过一次指导请求确认。

客户端验收与复验见 [导师客户端说明与测试](docs/导师客户端说明与测试.md)。接口、物体编号和分工见 [组员联调交接](docs/组员联调交接.md)。

## 目录与负责人

- `unity/`：沿用丁书萱提供的厨房，郭冀康现负责场景道具、导师 UI、客户端通信与交互记录。朱玉负责正式流程和主工程集成。
- `backend/`：魏伊林负责 API、模型调用和日志。
- `shared/`：统一维护菜谱、动作编号、物体编号及接口样例。
- `docs/`：项目计划、联调说明和测试记录。

## 协作约定

- 从 `main` 拉任务分支，自测后发 PR，请相关组员检查再合并。
- 修改主场景前先和朱玉确认，避免两人同时编辑同一个 `.unity` 文件。
- 提交 `Assets/` 及对应的 `.meta`，同时保留 `Packages/` 和 `ProjectSettings/`。
- 缓存、打包产物、`.venv/`、真实 `.env` 和 API 密钥不提交。
- 修改接口字段、步骤编号或道具编号时，同步更新 `shared/` 并告知相关负责人。
