using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using CookCoach.Tutoring;

namespace CookCoach.Checks
{
    public static class TutorClientCheck
    {
        const string PathName = "CookCoach/验证导师客户端（本地 HTTP 测试服务）";
        static bool running;
        [MenuItem(PathName, true)] static bool Available() =>
            EditorApplication.isPlaying && !running && SceneManager.GetActiveScene().name=="KitchenTutorPractice";

        [MenuItem(PathName)] public static void Run()
        {
            if (!Available()) return;
            var stove=UnityEngine.Object.FindFirstObjectByType<StovePractice>();
            if(stove==null || stove.Rehearsal==null) return;
            running=true; stove.StartCoroutine(Guard(stove));
        }
        static IEnumerator Guard(StovePractice stove)
        {
            var tutor=stove.tutor; var mode=tutor.mode; var endpoint=tutor.endpoint;
            bool background=Application.runInBackground; Application.runInBackground=true;
            var routine=Checks(stove);
            try {
                while (true) {
                    object wait;
                    try { if(!routine.MoveNext()) break; wait=routine.Current; }
                    catch(Exception e) { Debug.LogError("[TutorCheck] FAIL "+e); break; }
                    yield return wait;
                }
            } finally {
                running=false; Application.runInBackground=background; tutor.mode=mode;tutor.endpoint=endpoint; stove.BeginRehearsal();
            }
        }
        static void Expect(bool ok,string name)
        {
            if(!ok) throw new InvalidOperationException(name);
            Debug.Log("[TutorCheck] PASS "+name);
        }
        static IEnumerator Checks(StovePractice stove)
        {
            var tutor=stove.tutor; var practice=stove.Rehearsal;
            WaitUntil Ready() => new WaitUntil(()=>!tutor.IsWaiting);
            TutorJournal.Row Last(string kind) => tutor.Journal.rows.Last(r=>r.kind==kind);
            TutorRequest Request() => JsonUtility.FromJson<TutorRequest>(Last("request").payload);
            tutor.mode=TutorMode.LocalPreview;
            stove.BeginRehearsal();
            for(int i=0;i<5;i++) { if(i==3) practice.TrySeason(TomatoEggLesson.Oil); practice.TryStep(i); }
            foreach(string action in TomatoEggLesson.FinishActions) practice.TryFinish(action);
            Expect(tutor.Journal.review.Contains("受阻操作 0 次，主动求助 0 次"),"clean six-step completion");
            practice.Start();
            practice.TrySeason(TomatoEggLesson.Oil);yield return Ready();
            Expect(Request().recent_events.Last().reason=="oil_window" && !Request().snapshot.oil_added,"early oil rejected");
            practice.TryStep(0);practice.TryStep(1);practice.TryStep(2);
            practice.TryStep(3);yield return Ready();
            Expect(Request().recent_events.Last().reason=="oil_missing" && !Request().snapshot.oil_added,"egg requires oil");
            practice.TrySeason(TomatoEggLesson.Oil);practice.TrySeason(TomatoEggLesson.Oil);yield return Ready();
            Expect(Request().recent_events.Last().reason=="already_done" && Request().snapshot.oil_added,"oil counted once");
            practice.TryStep(3);practice.TryStep(4);
            practice.TrySeason(TomatoEggLesson.Salt);yield return Ready();
            Expect(Request().recent_events.Last().reason=="salt_window" && !Request().snapshot.salt_added,"salt waits for egg return");
            practice.TryFinish(TomatoEggLesson.ReturnEgg);practice.TrySeason(TomatoEggLesson.Salt);
            practice.TrySeason(TomatoEggLesson.Salt);yield return Ready();
            Expect(Request().recent_events.Last().reason=="already_done" && Request().snapshot.salt_added,"salt counted once");
            practice.Start();practice.TrySeason(TomatoEggLesson.Salt);yield return Ready();
            Expect(!Request().snapshot.salt_added && !Request().snapshot.oil_added,"reset clears oil and salt");
            tutor.mode=TutorMode.Http; tutor.endpoint="http://127.0.0.1:18741/api/v1/guidance";
            practice.Start(); practice.TryStep(1);practice.TryStep(0);practice.TryStep(0);
            Expect(tutor.Journal.rows.Count(r=>r.kind=="action")==2,"either prep order and duplicate suppression");
            practice.TryStep(3); practice.TryStep(2);
            Expect(tutor.IsWaiting && tutor.Journal.rows.Count(r=>r.kind=="action")==3,"blocked action and input lock");
            yield return Ready();
            Expect(tutor.Journal.rows.Any(r=>r.kind=="reply" && r.source=="mock"),"HTTP fixture response");
            Expect(Request().recent_events.Last().reason=="stove_off" && !Request().snapshot.stove_on,"actual failure and snapshot sent");
            tutor.Ask("question","开关在哪里？");yield return Ready();
            Expect(Request().conversation.Length==2 && tutor.ActiveHighlight=="stove_switch","follow-up history and registered highlight");
            Expect(Last("request").request_id==Last("execution").request_id,"request/reply/execution correlation");
            string pointedBy=Last("reply").request_id;
            practice.TryStep(2); Expect(tutor.ActiveHighlight=="" && Last("action").previous_guidance_id==pointedBy,"correction linked to guidance and clears highlight");
            tutor.Ask("question","现在呢？");yield return Ready();
            Expect(Request().snapshot.stove_on && !Request().snapshot.oil_added && tutor.LastMessage.Contains("油瓶"),"post-correction guidance");
            practice.TrySeason(TomatoEggLesson.Oil);practice.TryStep(3);practice.TryStep(4);
            foreach(string action in TomatoEggLesson.FinishActions) practice.TryFinish(action);
            Expect(tutor.Journal.review.Contains("受阻操作 1 次，主动求助 2 次") && tutor.Journal.review.Contains("炉灶未开启"),"review from actual events");
            string file=tutor.ExportEvidence();var saved=JsonUtility.FromJson<TutorJournal>(File.ReadAllText(file));
            Expect(saved.rows.Count==tutor.Journal.rows.Count && saved.review==tutor.Journal.review && saved.input_source=="simulated_actions","full-session JSON round trip");
            Debug.Log("[TutorCheck] EVIDENCE "+file);
            practice.Start();
            tutor.Ask("question","test:target");yield return Ready();
            Expect(tutor.ActiveHighlight=="" && Last("execution").detail.Contains("忽略"),"reject unknown target");
            tutor.Ask("question","test:stale");yield return Ready();
            Expect(Last("failure").request_id==Last("request").request_id,"reject mismatched revision");
            tutor.Ask("question","test:500");yield return Ready();
            string failedId=Last("request").request_id; int help=tutor.Journal.help_requests;
            tutor.Retry();yield return Ready();
            Expect(Last("failure").request_id!=failedId && tutor.Journal.help_requests==help,"service error and idempotent help count on retry");
            tutor.Ask("question","test:bad-json");yield return Ready();
            Expect(Last("failure").request_id==Last("request").request_id,"reject malformed HTTP body");
            tutor.Ask("question","test:fallback");yield return Ready();
            Expect(Last("reply").source=="fallback","explicit fallback provenance");
            tutor.Ask("question","test:slow");tutor.Cancel();
            yield return new WaitForSecondsRealtime(1.2f);
            Expect(!tutor.IsWaiting && tutor.LastMessage.Contains("取消") && Last("cancel").request_id==Last("request").request_id,"cancel ignores late response");
            tutor.Ask("question","test:slow");practice.Start();
            yield return new WaitForSecondsRealtime(1.2f);
            Expect(tutor.Journal.rows.All(r=>r.kind!="reply") && tutor.Journal.help_requests==0,"reset isolates session");
            tutor.Ask("question","test:timeout");yield return Ready();
            Expect(Last("failure").request_id==Last("request").request_id && Last("failure").elapsed_ms>=18000,"network timeout releases input");
            Debug.Log("[TutorCheck] COMPLETE");
        }
    }
}
