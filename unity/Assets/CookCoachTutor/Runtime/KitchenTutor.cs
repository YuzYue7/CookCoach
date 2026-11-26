#if UNITY_6000_0_OR_NEWER && ENABLE_LEGACY_INPUT_MANAGER && (UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR))
#define WINDOWS_TUTOR_IME
#endif
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;

namespace CookCoach.Tutoring
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class KitchenTutor : MonoBehaviour
    {
        public TutorMode mode = TutorMode.LocalPreview;
        public string endpoint = "http://127.0.0.1:8000/api/v1/guidance";
        public bool startExpanded = true;
        public bool IsWaiting { get; private set; }
        public string ActiveHighlight { get; private set; } = "";
        public string LastMessage => message.text;
        public event Action<string> HighlightChanged;
        public event Action<KitchenContext> ContextChanged;

        VisualElement root, card, marker, log, ticks;
        Label message, source, trace, targetName;
        TextField question;
        Button send, tip, locate, retry, askToggle;
        bool asking;
        readonly Dictionary<string, (Transform anchor, string title)> targets = new();
        readonly List<TutorTurn> history = new();
        readonly HashSet<string> retiredSessions = new();
        public TutorJournal Journal { get; private set; }
        public event Action<bool> WaitingChanged;
        float requestStarted;
        VisualElement reviewPanel;
        Label reviewText, exportStatus;
        bool showPayload;
        Button reviewButton;
        public string InputSource { get; set; } = "kitchen";
        public string LastExportPath { get; private set; }
        public string ExportDirectory => Path.Combine(Application.persistentDataPath, "CookCoach-Evidence");
        KitchenContext context;
        Coroutine pending;
        UnityWebRequest http;
        AudioSource voiceSource;
        AudioClip voiceClip;
        UnityWebRequest voiceHttp;
        bool voiceMuted;
        Coroutine voicePending;
        Button voiceButton;
        string latestId, lastQuestion, lastTrigger, lastDeliveredId;
        bool expanded;
        Font uiFont;
#if WINDOWS_TUTOR_IME
        readonly StringBuilder committedCharacters = new();
        string compositionPreview = "";
        string draftBeforeCommit;
        int commitStart, commitEnd;
        int commitFrame = -1;
#endif
        static void Show(VisualElement element, bool on) =>
            element.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;

        void Start()
        {
            root = GetComponent<UIDocument>().rootVisualElement;
            root.pickingMode = PickingMode.Ignore;
            uiFont = Font.CreateDynamicFontFromOSFont(new[] {"Microsoft YaHei UI","Microsoft YaHei","PingFang SC","Noto Sans CJK SC","Arial"}, 18);
            if (uiFont != null) {
                root.style.unityFontDefinition = FontDefinition.FromFont(uiFont);
                root.style.unityFont = uiFont;
            }
            card=root.Q("card"); marker=root.Q("beacon"); ticks=root.Q("ticks"); log=root.Q("log");
            message=root.Q<Label>("hint"); source=root.Q<Label>("origin"); trace=root.Q<Label>("trace");
            targetName=root.Q<Label>("targetName"); question=root.Q<TextField>("question");
            #if UNITY_6000_0_OR_NEWER
            question.textEdition.placeholder="问问导师…";
#else
            question.label="提问";
            question.labelElement.style.minWidth=32;
            question.labelElement.style.width=32;
#endif
            send=root.Q<Button>("send"); tip=root.Q<Button>("tip"); locate=root.Q<Button>("locate"); retry=root.Q<Button>("retry");
            askToggle=root.Q<Button>("askToggle");
            askToggle.clicked+=()=>{ if(IsWaiting) Cancel(); else SetAsking(!asking); };
            voiceSource = gameObject.AddComponent<AudioSource>();
            voiceSource.playOnAwake = false;
            voiceSource.spatialBlend = 0f;
            voiceMuted = PlayerPrefs.GetInt("tutorVoiceMuted", 0) == 1;
            voiceButton = root.Q<Button>("voiceToggle");
            if (voiceButton != null) {
                voiceButton.text = voiceMuted ? "语音：关" : "语音：开";
                voiceButton.clicked += ToggleVoice;
            }
            SetAsking(false);
            send.clicked += () => { if(IsWaiting) Cancel(); else SubmitTyped(); };
            tip.clicked += () => Ask("hint","");
            locate.clicked += () => Ask("question","开关在哪里？");
            retry.clicked += Retry;
#if WINDOWS_TUTOR_IME
            question.RegisterCallback<InputEvent>(_ => compositionPreview=Input.compositionString ?? "");
            question.RegisterCallback<FocusOutEvent>(_ => compositionPreview="");
#endif
            question.RegisterCallback<KeyDownEvent>(OnQuestionKey, TrickleDown.TrickleDown);
            root.Q<Button>("fold").clicked+=()=>SetExpanded(false);
            root.Q<Button>("expand").clicked+=()=>SetExpanded(true);
            root.Q<Button>("logButton").clicked+=()=>Show(log,log.resolvedStyle.display==DisplayStyle.None);
            root.Q<Button>("closeLog").clicked+=()=>Show(log,false);

            SetExpanded(startExpanded); Show(marker,false); Show(log,false); Show(retry,false);
            reviewPanel=root.Q("reviewPanel"); reviewText=root.Q<Label>("reviewText");
            reviewButton=root.Q<Button>("reviewButton"); exportStatus=root.Q<Label>("exportStatus");
            reviewButton.clicked+=()=>{Show(log,false);Show(reviewPanel,true);};
            root.Q<Button>("closeReview").clicked+=()=>Show(reviewPanel,false);
            root.Q<Button>("export").clicked+=ExportFromPanel;
            root.Q<Button>("payload").clicked+=()=>{
                showPayload=!showPayload;root.Q<Button>("payload").text=showPayload?"显示摘要":"查看请求与回复"; RenderTrace();
            };
            root.Q<Button>("copyReview").clicked+=()=>GUIUtility.systemCopyBuffer=reviewText.text;
            root.Q<Button>("logButton").clicked+=()=>Show(reviewPanel,false);
            Show(reviewPanel,false); Show(reviewButton,false);
            if(context!=null) UpdateReview(false);
            RefreshContextView();
            if(context==null) message.text="等待厨房开始练习。";
            else message.text="食材准备好了。需要帮助时，可以给我提示或直接提问。";
            RenderTrace();
        }

        public bool InteractingWithPanel => asking && expanded
            || log!=null && log.style.display.value==DisplayStyle.Flex
            || reviewPanel!=null && reviewPanel.style.display.value==DisplayStyle.Flex;

        void SetAsking(bool open)
        {
            asking=open;
            Show(root.Q("askPanel"),open);
            Show(root.Q("actionSlot"),!open);
            askToggle.text=IsWaiting?"取消请求":open?"返回":"提问";
            if(open) question.Focus();
        }

        public void SetExpanded(bool value)
        {
            expanded=value;
            if(card!=null){Show(card,value);Show(root.Q("expand"),!value);}
        }

        // The kitchen owns session and state versions. Send the latest snapshot after each action.
        public void SetContext(KitchenContext next)
        {
            if(next==null || next.snapshot==null || string.IsNullOrWhiteSpace(next.session_id))
                throw new ArgumentException("A kitchen session and snapshot are required.");
            bool fresh = context == null || context.session_id != next.session_id;
            if (retiredSessions.Contains(next.session_id) ||
                (!fresh && next.state_version <= context.state_version)) return;
            bool wasComplete = context != null && TomatoEggLesson.Complete(context);
            StopVoice();
            StopPending("状态已更新"); SetHighlight("");
            if (fresh) {
                if(context != null) retiredSessions.Add(context.session_id);
                history.Clear(); LastExportPath=null; lastDeliveredId=null;
                Journal=new TutorJournal {session_id=next.session_id, recipe_id=next.recipe_id,
                    recipe_version=next.recipe_version, input_source=InputSource};
                if(reviewPanel!=null) {Show(reviewPanel,false); Show(log,false); exportStatus.text="";}
            }
            context=JsonUtility.FromJson<KitchenContext>(JsonUtility.ToJson(next));
            context.snapshot.completed_steps ??= Array.Empty<string>();
            context.recent_events ??= Array.Empty<KitchenEvent>();
            if(context.recent_events.Length>10) context.recent_events=context.recent_events.Skip(context.recent_events.Length-10).ToArray();
            ContextChanged?.Invoke(context);
            Journal.Add(context,"state",context.snapshot.current_step_title,JsonUtility.ToJson(context.snapshot));
            if(message!=null) {
                message.text=fresh?"练习已开始。需要帮助时，可以问我。":"当前步骤 · "+context.snapshot.current_step_title;
#if WINDOWS_TUTOR_IME
                committedCharacters.Clear();
                compositionPreview="";
#endif
                question.SetValueWithoutNotify(""); Show(retry,false);
                if(fresh) { SetAsking(false); SetExpanded(startExpanded); }
            }
            RefreshContextView(); UpdateReview(!wasComplete || fresh); RenderTrace();
        }

        // Call once per accepted or blocked attempt, with a monotonically increasing revision.
        public void ReportAction(KitchenContext next, KitchenEvent action)
        {
            if (action==null || next==null) throw new ArgumentNullException();
            if (retiredSessions.Contains(next.session_id) ||
                (context!=null && context.session_id==next.session_id && next.state_version<=context.state_version)) return;
            SetContext(next);
            Journal.RecordAction(context,action,lastDeliveredId);
            lastDeliveredId=null;
            UpdateReview(false); RenderTrace();
            if(action.outcome=="blocked") Ask("error","");
        }

        void UpdateReview(bool open)
        {
            bool complete=TomatoEggLesson.Complete(context);
            if(complete) Journal.review=Journal.Review();
            if(reviewPanel==null) return;
            Show(reviewButton,complete);
            if(complete) {
                reviewText.text=Journal.review;
                if(open) { Show(log,false); Show(reviewPanel,true); }
            }
        }

        public string ExportEvidence()
        {
            if(Journal==null) throw new InvalidOperationException("请先开始练习。");
            LastExportPath=Journal.Export(ExportDirectory);
            return LastExportPath;
        }
        void ExportFromPanel()
        {
            try { exportStatus.text="已导出 · "+ExportEvidence(); }
            catch(Exception e) { exportStatus.text="导出失败 · "+e.Message; }
        }

        void RefreshContextView()
        {
            if(card==null) return;
            source.text=mode==TutorMode.LocalPreview?"本地模拟":"后端服务";
            if(context!=null){
                root.Q<Label>("recipe").text=context.recipe_title;
                root.Q<Label>("step").text=context.snapshot.current_step_title;
                int total=Mathf.Clamp(context.snapshot.step_count,1,12);
                int n=Mathf.Clamp(context.snapshot.step_number,1,total);
                root.Q<Label>("count").text=n+" / "+total;
                ticks.Clear();
                for(int i=1;i<=total;i++){var t=new VisualElement();t.AddToClassList("tick");bool complete=TomatoEggLesson.Matches(context)
                    ? Array.IndexOf(context.snapshot.completed_steps,TomatoEggLesson.Ids[i-1])>=0 : context.snapshot.current_step=="done" || i<n;
                    if(complete)t.AddToClassList("done");else if(i==n)t.AddToClassList("active");ticks.Add(t);}
            }
            SetBusy(IsWaiting);
        }
        // Scene rules can give immediate feedback even when the network coach is offline.
        public void ShowSceneCue(string text, string target = "", bool reveal = true)
        {
            if (message == null) return;
            StopPending();
            if (reveal) SetExpanded(true);
            message.text = text;
            Speak(text);
            source.text = "练习提示";
            Show(retry, false);
            SetHighlight(target);
            Journal?.Add(context,"cue",text,source:"scene",target:ActiveHighlight); RenderTrace();
        }

        void ToggleVoice()
        {
            voiceMuted = !voiceMuted;
            PlayerPrefs.SetInt("tutorVoiceMuted", voiceMuted ? 1 : 0);
            voiceButton.text = voiceMuted ? "语音：关" : "语音：开";
            if (voiceMuted) StopVoice();
        }

        // 朗读任意消息（仅 Http 模式且未静音）；任何失败静默降级纯文本。
        public void Speak(string text)
        {
            StopVoice();
            if (voiceMuted || mode != TutorMode.Http || voiceSource == null || string.IsNullOrWhiteSpace(text)) return;
            if (!Uri.TryCreate(endpoint?.TrimEnd('/'), UriKind.Absolute, out var guidanceUrl) ||
                (guidanceUrl.Scheme != Uri.UriSchemeHttp && guidanceUrl.Scheme != Uri.UriSchemeHttps)) return;
            voicePending = StartCoroutine(FetchVoice(text, new Uri(guidanceUrl, "tts").AbsoluteUri));
        }

        void StopVoice()
        {
            if (voicePending != null) { StopCoroutine(voicePending); voicePending = null; }
            if (voiceHttp != null) { voiceHttp.Abort(); voiceHttp.Dispose(); voiceHttp = null; }
            if (voiceSource != null) { voiceSource.Stop(); voiceSource.clip = null; }
            if (voiceClip != null) { Destroy(voiceClip); voiceClip = null; }
        }

        IEnumerator FetchVoice(string text, string url)
        {
            var req = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.MPEG);
            req.method = UnityWebRequest.kHttpVerbPOST;
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(
                JsonUtility.ToJson(new TtsRequest { text = text })));
            req.SetRequestHeader("Content-Type", "application/json; charset=utf-8");
            req.timeout = 12;
            voiceHttp = req;
            yield return req.SendWebRequest();
            voiceHttp = null;
            var clip = req.result == UnityWebRequest.Result.Success
                ? DownloadHandlerAudioClip.GetContent(req) : null;
            req.Dispose();
            if (clip == null) { voicePending = null; yield break; }
            voiceClip = clip;
            voiceSource.clip = clip;
            voiceSource.Play();
            yield return new WaitUntil(() => voiceSource == null || !voiceSource.isPlaying);
            voiceSource.clip = null;
            Destroy(clip);
            voiceClip = null;
            voicePending = null;
        }

        public void RegisterTarget(string id, Transform anchor, string label)
        {
            if(string.IsNullOrWhiteSpace(id)||anchor==null) throw new ArgumentException("Target id and anchor are required.");
            targets[id]=(anchor,label);
        }
        public void UnregisterTarget(string id) { targets.Remove(id); if(ActiveHighlight==id) SetHighlight(""); }
        void SetHighlight(string id)
        {
            if(string.IsNullOrEmpty(id)||!targets.TryGetValue(id,out var entry)||entry.anchor==null || !entry.anchor.gameObject.activeInHierarchy) id="";
            ActiveHighlight=id;
            if(marker!=null) Show(marker,false);
            HighlightChanged?.Invoke(id);
        }
        void LateUpdate()
        {
            if (voiceButton != null) {
                var display = mode == TutorMode.Http ? DisplayStyle.Flex : DisplayStyle.None;
                if (voiceButton.style.display.value != display) voiceButton.style.display = display;
            }
            if(marker==null || ActiveHighlight=="") return;
            if(!targets.TryGetValue(ActiveHighlight,out var t)||t.anchor==null || !t.anchor.gameObject.activeInHierarchy){SetHighlight("");return;}
            var cam=Camera.main;
            if(cam==null || root.panel==null){Show(marker,false);return;}
            var vp=cam.WorldToViewportPoint(t.anchor.position);
            bool visible=vp.z>0 && vp.x>=0 && vp.x<=1 && vp.y>=0 && vp.y<=1;
            Show(marker,visible);
            if(!visible) return;
            var coord=RuntimePanelUtils.CameraTransformWorldToPanel(root.panel,t.anchor.position,cam);
            targetName.text=t.title; marker.style.left=coord.x-13;marker.style.top=coord.y-13;
        }

        void SubmitTyped()
        {
#if WINDOWS_TUTOR_IME
            // A click can move focus to Send before the panel has replayed the committed word.
            if (committedCharacters.Length>0) ApplyCommittedDraft();
#endif
            var text=question.value?.Trim();
            if(string.IsNullOrEmpty(text)) {question.Focus();return;}
            Ask("question",text);
        }
        void OnQuestionKey(KeyDownEvent e)
        {
#if WINDOWS_TUTOR_IME
            // Windows can deliver a whole candidate before UI Toolkit removes its pinyin preview.
            // Let that removal restore the selection, then insert through the normal text editor.
            if ((compositionPreview.Length>0 || committedCharacters.Length>0) && !ComposingText() &&
                e.keyCode==KeyCode.None && !char.IsControl(e.character) && !e.actionKey) {
                if (committedCharacters.Length==0) {
                    draftBeforeCommit=question.text;
                    commitEnd=Mathf.Clamp(question.cursorIndex,0,draftBeforeCommit.Length);
                    // At the field's limit only the beginning of the pinyin may be visible.
                    int length=Math.Min(commitEnd,compositionPreview.Length);
                    while (length>0 && string.CompareOrdinal(draftBeforeCommit,commitEnd-length,compositionPreview,0,length)!=0)
                        length--;
                    commitStart=commitEnd-length;
                    question.schedule.Execute(InsertCommittedCharacters);
                }
                commitFrame=Time.frameCount;
                committedCharacters.Append(e.character);
                e.StopImmediatePropagation();
                return;
            }
            if (commitFrame==Time.frameCount || compositionPreview.Length>0) return;
#endif
            if ((e.keyCode==KeyCode.Return || e.keyCode==KeyCode.KeypadEnter) && !IsWaiting && !ComposingText()) {
                e.StopImmediatePropagation();
                SubmitTyped();
            }
        }
#if WINDOWS_TUTOR_IME
        void ApplyCommittedDraft()
        {
            int room=question.maxLength<0 ? committedCharacters.Length :
                Math.Max(0,question.maxLength-(draftBeforeCommit.Length-(commitEnd-commitStart)));
            int count=Math.Min(room,committedCharacters.Length);
            if (count>0 && count<committedCharacters.Length && char.IsHighSurrogate(committedCharacters[count-1])) count--;
            string word=committedCharacters.ToString(0,count);
            committedCharacters.Clear();
            compositionPreview="";
            question.value=draftBeforeCommit.Remove(commitStart,commitEnd-commitStart).Insert(commitStart,word);
            question.SelectRange(commitStart+word.Length,commitStart+word.Length);
        }

        void InsertCommittedCharacters()
        {
            if (committedCharacters.Length==0) return;
            if (question.enabledInHierarchy && question.focusController?.focusedElement!=question &&
                asking && expanded) {
                ApplyCommittedDraft();
                return;
            }
            string text=committedCharacters.ToString();
            committedCharacters.Clear();
            compositionPreview="";
            if (!asking || !expanded || !question.enabledInHierarchy) return;
            var editor=(TextElement)question.textSelection;
            foreach (char character in text) {
                using var key=KeyDownEvent.GetPooled(character, KeyCode.None, EventModifiers.None);
                key.target=editor;
                editor.SendEvent(key);
            }
        }
#endif
        static bool ComposingText()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return !string.IsNullOrEmpty(Input.compositionString);
#else
            return false;
#endif
        }
        public void Ask(string trigger,string text) => BeginRequest(trigger,text,false);
        public void Retry()
        {
            if(retry!=null && retry.style.display.value==DisplayStyle.Flex) BeginRequest(lastTrigger,lastQuestion,true);
        }
        void BeginRequest(string trigger,string text,bool retrying)
        {
            if(context==null || card==null || IsWaiting) return;
            StopVoice();
            SetExpanded(true);Show(retry,false);SetHighlight("");
            lastTrigger=trigger;lastQuestion=text??"";
            latestId=Guid.NewGuid().ToString("N");
            var ids=new List<string>(); foreach(var kv in targets) if(kv.Value.anchor!=null && kv.Value.anchor.gameObject.activeInHierarchy)ids.Add(kv.Key);
            var request=new TutorRequest{request_id=latestId,session_id=context.session_id,
                recipe_id=context.recipe_id,recipe_version=context.recipe_version,state_version=context.state_version,
                trigger=trigger,user_question=lastQuestion,snapshot=context.snapshot,recent_events=context.recent_events,
                conversation=history.ToArray(),allowed_highlight_targets=ids.ToArray()};
            var payload=JsonUtility.ToJson(request);
            if(!retrying && (trigger=="question" || trigger=="hint")) Journal.help_requests++;
            requestStarted=Time.realtimeSinceStartup;
            string detail=trigger=="error"?"操作受阻，自动求助":trigger=="question"?"提问 · "+lastQuestion:"主动索取提示";
            Journal.Add(context,"request",(retrying?"重试 · ":"")+detail,payload,latestId,mode.ToString());
            UpdateReview(false); RenderTrace();
            SetAsking(false);
            string blocked=trigger=="error"?TomatoEggLesson.BlockedFinishCue(context.snapshot,context.recent_events.LastOrDefault()):"";
            message.text=blocked.Length>0?blocked:"导师正在思考…";
            SetBusy(true);
            pending=StartCoroutine(Receive(request,payload));
        }

        IEnumerator Receive(TutorRequest req,string payload)
        {
            TutorReply reply=null;
            if(mode==TutorMode.LocalPreview)
                yield return TutorTransport.Preview(req,value=>reply=value);
            else {
                string failure=null;
                UnityWebRequestAsyncOperation op=null;
                try {http=TutorTransport.CreateHttp(endpoint,payload);op=http.SendWebRequest();}
                catch(Exception e){failure=e.Message;}
                if(op!=null) yield return op;
                if(failure==null && http.result!=UnityWebRequest.Result.Success) failure=http.error;
                if(failure==null) {
                    try{reply=JsonUtility.FromJson<TutorReply>(http.downloadHandler.text);}
                    catch(Exception e){failure=e.Message;}
                }
                ReleaseHttp();
                if(failure!=null){Fail("导师连接失败，请重试。",failure);yield break;}
            }
            if(req.request_id!=latestId || req.session_id!=context.session_id || req.state_version!=context.state_version) yield break;
            if(reply==null || reply.request_id!=latestId || reply.session_id!=req.session_id ||
                reply.state_version!=req.state_version || !(reply.source=="llm" || reply.source=="fallback" || reply.source=="mock") ||
                reply.guidance==null || string.IsNullOrWhiteSpace(reply.guidance.message) ||
                !(reply.guidance.hint_level=="check" || reply.guidance.hint_level=="explain" || reply.guidance.hint_level=="direct")){
                Fail("回复与本轮练习不匹配，请重试。","回复编号、状态版本或内容无效");yield break;
            }
            SetBusy(false); pending=null;
            message.text=reply.guidance.message;
            Speak(message.text);
            source.text=reply.source=="mock"?"模拟回复":reply.source=="fallback"?"备用提示":"模型指导";
            SetHighlight(Array.IndexOf(req.allowed_highlight_targets,reply.guidance.highlight_target_id)>=0 ? reply.guidance.highlight_target_id : "");
            string result=ActiveHighlight!=""?"已标记 "+ActiveHighlight:
                !string.IsNullOrEmpty(reply.guidance.highlight_target_id)?"目标未注册，已忽略":"无需高亮";
            Journal.Add(context,"reply",message.text,JsonUtility.ToJson(reply),req.request_id,reply.source,reply.guidance.highlight_target_id,ElapsedMs());
            Journal.Add(context,"execution",result,request:req.request_id,source:reply.source,target:ActiveHighlight);
            lastDeliveredId=req.request_id;
            RenderTrace();
            history.Add(new TutorTurn("user",string.IsNullOrEmpty(req.user_question)?(req.trigger=="error"?"操作受阻，请解释原因":"给我提示"):req.user_question));
            history.Add(new TutorTurn("assistant",message.text));
            if(history.Count>6)history.RemoveRange(0,history.Count-6);
            question.SetValueWithoutNotify("");
        }
        void SetBusy(bool busy)
        {
            bool changed=IsWaiting!=busy; IsWaiting=busy; if(changed) WaitingChanged?.Invoke(busy); if(card==null)return;
            askToggle.text=busy?"取消请求":asking?"返回":"提问";
            send.text=busy?"取消":"发送";send.SetEnabled(context!=null);
            question.SetEnabled(!busy && context!=null);tip.SetEnabled(!busy && context!=null);
            Show(locate,!busy && context!=null && !context.snapshot.stove_on && targets.ContainsKey("stove_switch") && (context.snapshot.current_step=="ignite" || context.snapshot.current_step=="S3"));
        }
        void Fail(string msg,string detail)
        {
            pending=null;SetBusy(false);message.text=msg;Show(retry,true);Journal.Add(context,"failure",detail,request:latestId,ms:ElapsedMs()); RenderTrace();
        }
        int ElapsedMs() => Mathf.RoundToInt(1000*(Time.realtimeSinceStartup-requestStarted));
        void RenderTrace()
        {
            if(trace==null) return;
            if(Journal==null){trace.text="练习开始后将显示操作和导师记录。";return;}
            var tail=Journal.rows.Skip(Math.Max(0,Journal.rows.Count-8));
            if(showPayload) {
                var req=Journal.rows.LastOrDefault(r=>r.kind=="request");
                if(req==null){trace.text="本轮还没有指导请求。";return;}
                trace.text=string.Join("\n\n",Journal.rows.Where(r=>r.request_id==req.request_id)
                    .Select(r=>r.kind+" · "+r.request_id+"\n"+r.detail+"\n"+r.payload));
                return;
            }
            trace.text="步骤 · "+context.snapshot.current_step_title+"   版本 "+context.state_version+"\n"+
                string.Join("\n\n",tail.Select(r=>{
                    string id=string.IsNullOrEmpty(r.request_id)?"":" · "+r.request_id.Substring(0,8);
                    return r.kind+id+"\n"+r.detail+(r.kind=="reply"?" · "+r.source+" · "+r.elapsed_ms+" ms":"");
                }));
        }
        void ReleaseHttp(){if(http!=null){http.Dispose();http=null;}}
        void StopPending(string reason="请求中断")
        {
            if(IsWaiting && context!=null && Journal!=null) Journal.Add(context,"cancel",reason,request:latestId,ms:ElapsedMs());
            latestId=null;if(pending!=null){StopCoroutine(pending);pending=null;}
            if(http!=null){http.Abort();ReleaseHttp();}
            SetBusy(false);
        }
        public void Cancel(){if(!IsWaiting)return; StopPending("用户取消");message.text="已取消，可以重新提问。";RenderTrace();}
        void OnDisable(){StopPending(); StopVoice();}
        void OnDestroy(){if(uiFont!=null) Destroy(uiFont); if(voiceClip!=null){ if(voiceSource!=null) voiceSource.clip=null; Destroy(voiceClip); voiceClip=null; }}
    }
}
