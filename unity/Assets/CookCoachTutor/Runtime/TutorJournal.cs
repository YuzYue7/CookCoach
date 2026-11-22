using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace CookCoach.Tutoring
{
    // Rows keep the whole session. Only the on-screen tail and the model context are bounded.
    [Serializable]
    public sealed class TutorJournal
    {
        [Serializable] public sealed class Row {
            public string utc, kind, request_id, previous_guidance_id, step, detail, payload, source, target_id;
            public int state_version, elapsed_ms;
        }
        public string session_id, recipe_id, recipe_version, input_source, review;
        public int blocked_actions, help_requests;
        public List<Row> rows = new List<Row>();

        public void Add(KitchenContext c, string kind, string detail, string payload = null,
                        string request = null, string source = null, string target = null, int ms = 0)
        {
            rows.Add(new Row { utc=DateTime.UtcNow.ToString("o"), kind=kind, step=c.snapshot.current_step,
                state_version=c.state_version, request_id=request, source=source, target_id=target,
                elapsed_ms=ms, detail=detail, payload=payload });
        }

        public void RecordAction(KitchenContext c, KitchenEvent e, string previousGuidanceId)
        {
            if (e.outcome == "blocked") blocked_actions++;
            string detail = TomatoEggLesson.ActionName(e.action_id);
            if (!string.IsNullOrEmpty(e.reason)) detail += " · " + TomatoEggLesson.ReasonName(e.reason);
            Add(c, "action", detail, JsonUtility.ToJson(e));
            rows[rows.Count-1].previous_guidance_id=previousGuidanceId;
        }

        public string Review()
        {
            var lines = new List<string>();
            for (int i=0; i<TomatoEggLesson.Ids.Length; i++)
                lines.Add((i+1) + ". " + TomatoEggLesson.Instructions[i]);
            lines.Add("\n本轮记录");
            lines.Add("受阻操作 " + blocked_actions + " 次，主动求助 " + help_requests + " 次。");
            foreach (var group in rows.Where(r => r.kind == "action")
                .Where(r => JsonUtility.FromJson<KitchenEvent>(r.payload).outcome == "blocked").GroupBy(r => r.detail))
                lines.Add("· " + group.Key + "（" + group.Count() + " 次）");
            return string.Join("\n", lines);
        }

        public string Export(string directory)
        {
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "cookcoach-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0,8) + ".json");
            string temporary = path + ".tmp";
            try {
                File.WriteAllText(temporary, JsonUtility.ToJson(this,true), new UTF8Encoding(false));
                File.Move(temporary,path);
            } finally {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
            return path;
        }
    }
}
