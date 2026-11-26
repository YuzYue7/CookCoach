"""上下文组装与提示词。

输入：snapshot（步骤/炉火/related_error_count/finish_phase）+
recent_events(≤10) + conversation(≤6) + user_question + recipe 版本。
要求：导师口吻、一句中文短句；受阻时结合 reason 讲清错在哪；
S6 按 finish_phase 区分 熟蛋回锅/合炒/关火/装盘；
追问结合历史；防御性截断（事件≤10、对话≤6、单条≤500 字）。
"""
from ..schemas import GuidanceRequest

SYSTEM_PROMPT = (
    "你是 CookCoach 厨房教学游戏的导师，指导玩家完成「番茄炒蛋」菜谱。\n"
    "菜谱步骤：S1 切番茄与 S2 打散鸡蛋可交换顺序，两项都完成后进入 S3 开启炉灶→S4 炒蛋并盛出→"
    "S5 炒番茄→S6 合炒、关火与装盘（按 finish_phase 区分）。\n"
    "收到调味状态时，开火后必须先倒油再炒蛋；熟蛋回锅后、合炒前可以撒一次盐，也可以直接合炒。未收到调味状态的旧版练习沿用原流程。current_step=done 表示已经关火并装盘，"
    "此时可据已记录的状态回答问题并提示查看回顾，highlight_target_id 留空。\n"
    "规则：\n"
    "1. 先根据当前步骤 current_step 决定「现在该做什么」，再看最近操作判断玩家"
    "刚才哪里做错了；绝不能只凭炉火状态猜测下一步（例如火开着但当前是 S5，"
    "应提示倒番茄而不是倒蛋液；S4 且鸡蛋已炒好盛出时同样不要再提示倒蛋液）。\n"
    "2. trigger=error（玩家操作受阻）时回复两句，用句号分隔：第一句解释刚才的"
    "操作为什么没执行（结合 reason：stove_off=炉灶未开启、previous_step=上一步"
    "未完成、prep_incomplete=备料未完成、already_done=已完成不用重复），"
    "第二句告诉玩家现在该做什么；其余情况只回复一句话。\n"
    "3. 中文，导师口吻；玩家追问时要延续之前的语境。\n"
    "4. 玩家问某个物体在哪里时，给出简短位置描述，并从 allowed_highlight_targets "
    "里选一个编号填入 highlight_target_id；只选玩家询问的物体；目标不明或未注册就留空，不要默认指向旋钮。\n"
    "5. hint_level 三档：check=提醒自查，explain=解释原因，direct=直接指路；"
    "填了高亮编号时用 direct，否则用 check。\n"
    "6. 当前步骤为 S6 装盘阶段时，按 finish_phase 区分："
    "return_cooked_egg=熟鸡蛋在右侧盘子（plate）上，把它拖回锅中，此时蛋碗（egg_bowl）已空；stir_fry=按住锅内绕圈合炒；"
    "turn_off_stove=点旋钮关火；finish_dish=把成菜装入空盘。\n"
    "7. 只输出一个 JSON 对象：{\"message\": \"...\", \"hint_level\": \"...\", "
    "\"highlight_target_id\": \"...\"}，不要输出其他任何内容。"
)


def _format_events(events) -> str:
    lines = []
    for ev in events[-10:]:
        result = "成功" if ev.outcome == "ok" else f"受阻(reason: {ev.reason})"
        lines.append(f"- {ev.action_id} → {ev.target_id}：{result}")
    return "\n".join(lines) or "- （无）"


def build_messages(req: GuidanceRequest) -> list[dict]:
    snap = req.snapshot
    question = req.user_question.strip()
    if not question and req.trigger == "error":
        question = "操作受阻，请解释原因"
    state_lines = [
        f"菜谱：{req.recipe_id}（版本 {req.recipe_version}）",
        f"当前步骤：{snap.current_step} {snap.current_step_title}"
        f"（第 {snap.step_number}/{snap.step_count} 步），已完成："
        f"{'、'.join(snap.completed_steps) or '无'}",
        f"炉火：{'已开启' if snap.stove_on else '未开启'}；受阻次数：{snap.related_error_count}",
    ]
    for label, added in (("食用油", snap.oil_added), ("盐", snap.salt_added)):
        if req.recipe_version != "1" and added is not None:
            state_lines.append(f"{label}：{'已加入' if added else '未加入'}")
    if snap.current_step == "S6":
        state_lines.append(f"finish_phase（S6 子阶段）：{snap.finish_phase or 'return_cooked_egg'}")
    context_block = "\n".join([
        "[当前游戏状态]",
        *state_lines,
        "[最近操作]",
        _format_events(req.recent_events),
        f"[可高亮的物体编号] {', '.join(req.allowed_highlight_targets) or '（无）'}",
        f"[trigger] {req.trigger}",
        f"[玩家问题] {question or '（无）'}",
    ])
    # 对话历史内联为文本：DeepSeek 的 response_format=json_object 与
    # 多轮 messages 组合会偶发输出空串（实测），单条 user 消息可规避。
    history_lines = [
        f"{'导师' if turn.role == 'assistant' else '玩家'}：{turn.content[:500]}"
        for turn in req.conversation[-6:]
    ]
    if history_lines:
        context_block = "[对话历史]\n" + "\n".join(history_lines) + "\n\n" + context_block
    return [
        {"role": "system", "content": SYSTEM_PROMPT},
        {"role": "user", "content": context_block},
    ]
