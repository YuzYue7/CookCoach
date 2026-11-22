using System;
using System.Collections.Generic;
using System.Linq;

namespace CookCoach.Tutoring
{
    public static class TomatoEggLesson
    {
        public static readonly string[] Ids = { "S1", "S2", "S3", "S4", "S5", "S6" };
        public static readonly string[] Actions = { "cut_tomato", "beat_egg", "turn_on_stove", "fry_egg", "fry_tomato", "finish_dish" };
        public static readonly string[] Titles = { "切番茄", "打散鸡蛋", "开启炉灶", "炒蛋并盛出", "炒番茄", "合炒、关火与装盘" };
        public static readonly string[] Instructions = {
            "将番茄切块，放在一旁备用。", "将鸡蛋打入碗中，搅打均匀。", "确认锅和油已准备好，打开炉灶。",
            "倒入蛋液，炒至凝固后盛出。", "将番茄倒入锅中，翻炒至出汁。", "把鸡蛋倒回锅里合炒，关火后装盘。"
        };
        public static bool Matches(KitchenContext c) =>
            c != null && c.recipe_id == "tomato_egg" && c.snapshot.step_count == Ids.Length;

        public static string ActionName(string id)
        {
            int index = Array.IndexOf(Actions, id);
            return index < 0 ? id : Titles[index];
        }

        public static string ReasonName(string id)
        {
            switch (id) {
                case "stove_off": return "炉灶未开启";
                case "prep_incomplete": return "备料尚未完成";
                case "previous_step": return "前一步尚未完成";
                case "already_done": return "这项操作已完成";
                default: return id;
            }
        }

        public static bool Complete(KitchenContext c) => Matches(c) &&
            c.snapshot.current_step == "done" && Ids.All(id => Array.IndexOf(c.snapshot.completed_steps, id) >= 0);
    }
}
