using System;
using System.Collections.Generic;
using System.Linq;

namespace CookCoach.Tutoring
{
    public static class TomatoEggLesson
    {
        public static readonly string[] Ids = { "S1", "S2", "S3", "S4", "S5", "S6" };
        public static readonly string[] Targets = { "tomato", "egg_bowl", "stove_switch", "pan", "pan", "plate" };
        public static readonly string[] Actions = { "cut_tomato", "beat_egg", "turn_on_stove", "fry_egg", "fry_tomato", "finish_dish" };
        public static readonly string[] Titles = { "切番茄", "打散鸡蛋", "开启炉灶", "炒蛋并盛出", "炒番茄", "合炒、关火与装盘" };
        public static readonly string[] Instructions = {
            "将番茄切块，放在一旁备用。", "将鸡蛋打入碗中，搅打均匀。", "备料后打开炉灶。",
            "先倒油，再倒入蛋液炒熟盛出。", "将番茄倒入锅中，翻炒至出汁。", "把鸡蛋倒回锅中，可撒盐调味，再合炒、关火并装盘。"
        };
        public const string Oil = "add_oil", Salt = "add_salt";
        public const string OilBottle = "oil_bottle", SaltShaker = "salt_shaker";
        public const string ReturnEgg = "return_cooked_egg", Mix = "stir_fry", HeatOff = "turn_off_stove", Serve = "finish_dish";
        public static readonly string[] FinishActions = { ReturnEgg, Mix, HeatOff, Serve };

        public static string FinishPhase(KitchenSnapshot snapshot) =>
            snapshot.current_step == "S6" ? (string.IsNullOrEmpty(snapshot.finish_phase) ? ReturnEgg : snapshot.finish_phase) : "";

        public static string FinishTarget(string action)
        {
            switch(action) {
                case ReturnEgg: case Mix: return "pan";
                case HeatOff: return "stove_switch";
                default: return "plate";
            }
        }

        public static string FinishCue(string action)
        {
            switch(action) {
                case ReturnEgg: return "番茄炒好了。把右侧盘里的熟鸡蛋拖回锅中。";
                case Mix: return "熟蛋已经回锅。按住锅里绕一圈，让鸡蛋和番茄一起翻炒。";
                case HeatOff: return "番茄和鸡蛋已炒匀。点炉台前方靠左的旋钮关火。";
                default: return "炉火已关。把锅拖向右侧空盘，装入炒好的番茄和鸡蛋。";
            }
        }

        public static string BlockedFinishCue(KitchenSnapshot snapshot,KitchenEvent attempt)
        {
            if(attempt==null || attempt.outcome!="blocked" || Array.IndexOf(FinishActions,attempt.action_id)<0) return "";
            string phase=FinishPhase(snapshot);
            string reason=attempt.reason=="already_done" ? ActionName(attempt.action_id)+"已经完成。" :
                ActionName(attempt.action_id)+"未执行。";
            return reason+(phase!="" ? FinishCue(phase) : "先完成当前步骤："+snapshot.current_step_title+"。");
        }

        public static bool Matches(KitchenContext c) =>
            c != null && c.recipe_id == "tomato_egg" && c.snapshot.step_count == Ids.Length;

        public static string ActionName(string id)
        {
            switch(id) {
                case Oil: return "倒油";
                case Salt: return "撒盐";
                case ReturnEgg: return "熟蛋回锅";
                case Mix: return "锅中合炒";
                case HeatOff: return "关闭炉火";
                case Serve: return "装盘";
            }
            int index = Array.IndexOf(Actions, id);
            return index < 0 ? id : Titles[index];
        }

        public static string ReasonName(string id)
        {
            switch (id) {
                case "oil_missing": return "锅里尚未倒油";
                case "salt_window": return "应在熟蛋回锅后、合炒前撒盐";
                case "oil_window": return "应在开火后、炒蛋前倒油";
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
