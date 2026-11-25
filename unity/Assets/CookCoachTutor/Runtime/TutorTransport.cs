using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace CookCoach.Tutoring
{
    public enum TutorMode { LocalPreview, Http }

    public static class TutorTransport
    {
        public static IEnumerator Preview(TutorRequest request, Action<TutorReply> done)
        {
            yield return new WaitForSecondsRealtime(0.85f);
            bool location = request.user_question.IndexOf("哪里", StringComparison.Ordinal) >= 0
                || request.user_question.Contains("哪") || request.user_question.Contains("找")
                || request.user_question.Contains("位置") || request.user_question.Contains("开关")
                || request.user_question.IndexOf("where", StringComparison.OrdinalIgnoreCase) >= 0;
            bool on = request.snapshot.stove_on;
            bool point = !on && location && Array.IndexOf(request.allowed_highlight_targets, "stove_switch") >= 0;
            point = point && request.snapshot.current_step != "place_pan";
            string message = request.snapshot.current_step == "place_pan" ? "先点右下角的「放锅上灶」，把平底锅放到炉架上。"
                : request.snapshot.current_step == "done" ? "锅已经就位，炉火也点着了。这一轮热锅练习完成。"
                : request.snapshot.current_step=="S5" ? "鸡蛋已盛出。下一步是炒番茄。"
                : on ? "炉灶已经打开，现在可以把蛋液倒入锅中。"
                : point ? "找到金色圆圈里的炉灶开关，点一下就能开火。"
                : "鸡蛋已经打散了。先检查炉灶是否打开，再试着炒蛋。";
            string highlight="stove_switch";
            int step=Array.IndexOf(TomatoEggLesson.Ids,request.snapshot.current_step);
            if(request.snapshot.step_count==6) {
                point=point && request.snapshot.current_step=="S3";
                if(request.snapshot.current_step=="done") message="这轮已完成，可以在回顾里查看清单和本轮操作。";
                else if(request.snapshot.current_step=="S6") {
                    string action=TomatoEggLesson.FinishPhase(request.snapshot);
                    message=TomatoEggLesson.FinishCue(action);
                    highlight=action==TomatoEggLesson.ReturnEgg?"plate":TomatoEggLesson.FinishTarget(action);
                    point=location && Array.IndexOf(request.allowed_highlight_targets,highlight)>=0;
                }
                else if(point) message="看金色标记，点炉台前面靠左的旋钮。";
                else if(request.trigger=="error" && request.recent_events.Length>0) {
                    var last=request.recent_events[request.recent_events.Length-1];
                    if(last.reason=="stove_off") message="锅还没热，先检查炉灶有没有打开。";
                    else if(step>=0) message="先"+TomatoEggLesson.Titles[step]+"，再"+TomatoEggLesson.ActionName(last.action_id)+"。";
                } else if(step>=0) message=TomatoEggLesson.Instructions[step];
            }
            if(request.trigger=="error" && request.recent_events.Length>0) {
                string blocked=TomatoEggLesson.BlockedFinishCue(request.snapshot,request.recent_events[request.recent_events.Length-1]);
                if(blocked.Length>0) message=blocked;
            }
            if(request.recipe_version=="2" && request.recipe_id=="tomato_egg") {
                var snap=request.snapshot;
                bool oil=request.user_question.Contains("油"), salt=request.user_question.Contains("盐");
                if(snap.current_step=="S4") message=snap.oil_added?"油已倒好，把蛋液倒入锅中。":"先把油瓶拖到锅上倒油，再倒蛋液。";
                if(snap.finish_phase==TomatoEggLesson.Mix && !location)
                    message=snap.salt_added?"盐已撒好，按住锅内绕圈合炒。":"可以先撒盐，也可以直接绕圈合炒。";
                if((oil || salt) && location && snap.current_step!="done") {
                    highlight=oil?TomatoEggLesson.OilBottle:TomatoEggLesson.SaltShaker;
                    point=oil!=salt && Array.IndexOf(request.allowed_highlight_targets,highlight)>=0;
                    message=point?(oil?"金色圆圈标出的是油瓶。":"金色圆圈标出的是盐罐。"):"请指定一个当前可以标记的道具。";
                } else if(oil || salt) {
                    point=false;
                    message=(oil?(snap.oil_added?"本轮已倒油。":"本轮还没倒油。"):"")+
                        (salt?(snap.salt_added?"本轮已撒盐。":"本轮未撒盐。"):"")+message;
                }
                if(request.trigger=="error" && request.recent_events.Length>0) {
                    var last=request.recent_events[request.recent_events.Length-1];
                    if(last.action_id==TomatoEggLesson.Oil || last.action_id==TomatoEggLesson.Salt || last.reason=="oil_missing")
                        message=TomatoEggLesson.ActionName(last.action_id)+"未执行："+TomatoEggLesson.ReasonName(last.reason)+"。"+message;
                }
            }
            done(new TutorReply { request_id = request.request_id, session_id = request.session_id,
                state_version = request.state_version, source = "mock",
                guidance = new Advice { message = message, hint_level = point ? "direct" : "check",
                    highlight_target_id = point ? highlight : "" } });
        }

        public static UnityWebRequest CreateHttp(string endpoint, string json)
        {
            var http = new UnityWebRequest(endpoint, "POST");
            http.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            http.downloadHandler = new DownloadHandlerBuffer();
            http.SetRequestHeader("Content-Type", "application/json; charset=utf-8");
            http.timeout = 20;
            return http;
        }
    }
}
