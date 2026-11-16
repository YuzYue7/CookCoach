# CookCoach
COMP5140 group project

## 开发环境

- Unity Editor：以 `unity/ProjectSettings/ProjectVersion.txt` 为准。
  开题 Demo 使用 `6000.6.4f1`，正式版本由两位 Unity 负责人确认。
- Python：计划使用 3.12。后端依赖由 `backend/requirements.txt` 记录。
- 首版以桌面鼠标和键盘操作为共同开发、测试入口。

## 本地启动

1. 克隆仓库，在 Unity Hub 中打开 `unity/`。
2. 打开主场景并点击 Play。若沿用开题 Demo，场景是 `Assets/Scenes/CookCoach.unity`。
3. 后端接通后，按 `backend/README.md` 启动 FastAPI。
   环境变量名称见 `backend/.env.example`，实际密钥由开发者在本地配置。
4. `/health` 用于检查后端服务。真实模型是否可用，通过一次指导请求确认。

## 目录与负责人

- `unity/`：丁书萱负责场景和道具；朱玉负责流程和主工程集成；
  郭冀康负责导师 UI、通信和交互记录。
- `backend/`：魏伊林负责 API、模型调用和日志。
- `shared/`：统一维护菜谱、动作编号、物体编号及接口样例。
- `docs/`：项目计划、联调说明和测试记录。

## 协作约定

- 从 `main` 拉任务分支，自测后发 PR，请相关组员检查再合并。
- 修改主场景前先和朱玉确认，避免两人同时编辑同一个 `.unity` 文件。
- 提交 `Assets/` 及对应的 `.meta`，同时保留 `Packages/` 和 `ProjectSettings/`。
- 缓存、打包产物、`.venv/`、真实 `.env` 和 API 密钥不提交。
- 修改接口字段、步骤编号或道具编号时，同步更新 `shared/` 并告知相关负责人。
