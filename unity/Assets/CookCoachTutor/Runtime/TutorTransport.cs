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
        static readonly (string id, string label, string[] names)[] KitchenObjects = {
            ("stove_switch", "炉灶旋钮", new[]{"开关","旋钮","炉灶","switch"}),
            ("egg_bowl", "蛋碗", new[]{"碗","打蛋盆","bowl"}),
            ("plate", "上菜盘", new[]{"盘","碟","plate"}),
            ("pan", "平底锅", new[]{"锅","合炒","翻炒","pan"}),
            ("tomato", "切番茄的砧板", new[]{"番茄","西红柿","砧板","案板","tomato"}),
            (TomatoEggLesson.OilBottle, "食用油瓶", new[]{"油","oil"}),
            (TomatoEggLesson.SaltShaker, "盐罐", new[]{"盐","salt"})
        };

        static string RequestedObject(TutorRequest request, out string label)
        {
            string question=request.user_question.ToLowerInvariant();
            bool cooked=question.Contains("熟蛋") || question.Contains("熟鸡蛋");
            string phase=TomatoEggLesson.FinishPhase(request.snapshot);
            string cookedTarget=request.snapshot.current_step=="S5" || phase==TomatoEggLesson.ReturnEgg
                ? "plate" : phase!="" ? "pan" : "";
            string target="";
            label="";
            foreach (var item in KitchenObjects) {
                if (!(cooked && item.id==cookedTarget) && !Array.Exists(item.names,question.Contains)) continue;
                if (target!="") { label=""; return ""; }
                target=item.id;
                label=item.label;
            }
            return target;
        }

        public static IEnumerator Preview(TutorRequest request, Action<TutorReply> done)
        {
            yield return new WaitForSecondsRealtime(0.85f);
            var question=request.user_question;
            bool location = question.Contains("哪里") || question.Contains("在哪") || question.Contains("哪儿")
                || question.Contains("哪边") || question.Contains("找不到") || question.Contains("位置")
                || question.IndexOf("where", StringComparison.OrdinalIgnoreCase) >= 0;
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
                if(oil || salt) {
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
            if (request.recipe_id=="tomato_egg" && request.snapshot.step_count==6 && location &&
                request.snapshot.current_step!="done") {
                highlight=RequestedObject(request,out string label);
                point=highlight!="" && Array.IndexOf(request.allowed_highlight_targets,highlight)>=0;
                message=point ? "金色圆圈标出的是"+label+"。" :
                    label!="" ? "当前场景无法标记"+label+"。" : "你想找哪件道具？请一次问一个。";
                if (point && highlight=="pan" && TomatoEggLesson.FinishPhase(request.snapshot)==TomatoEggLesson.Mix &&
                    (question.Contains("合炒") || question.Contains("翻炒")))
                    message=TomatoEggLesson.FinishCue(TomatoEggLesson.Mix);
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
