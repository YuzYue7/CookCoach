using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace CookCoach.Tutoring
{
    [DefaultExecutionOrder(50)]
    public sealed class StovePractice : MonoBehaviour
    {
        public KitchenTutor tutor;
        public Camera lessonCamera;
        public Renderer knob;
        public Transform pan, panRest, burner;
        public GameObject flame;
        [Min(0.1f)] public float reminderSeconds = 5f;
        public GameObject workbenchPrefab;
        [Tooltip("可选：实现 IKitchenWorkflow 的正式流程组件")]
        public MonoBehaviour workflow;
        IKitchenWorkflow Rules => workflow as IKitchenWorkflow;
        KitchenWorkbench workbench;
        bool desktop;
        public TextAsset walkableFloor;
        KitchenWalk walk;
        CookingHands hands;
        Label walkHints;
        const string WalkHelp="WASD 移动　·　按住右键环视　·　F 回到灶台";
        Button simulator, reset;
        Label hover;
        Vector3 sampleCameraPosition;
        Quaternion sampleCameraRotation;
        float sampleField;
        enum Stage { PlacePan, Ignite, Done }
        Stage stage;
        string session;
        int revision;
        float elapsed;
        bool reminded;
        Transform marker;
        Button action, switchPractice;
        public RecipeRehearsal Rehearsal { get; private set; }
        UIDocument doc;
        KitchenEvent[] events;

        void Start()
        {
            doc = tutor.GetComponent<UIDocument>();
            marker = new GameObject("Switch label anchor").transform;
            marker.SetParent(transform, false);
            marker.position = knob.bounds.center;
            tutor.RegisterTarget("stove_switch", marker, "炉灶开关");
            action = new Button(OnAction) { name = "lessonAction" };
            doc.rootVisualElement.Q("hud").Add(action);
            var hud=doc.rootVisualElement.Q("hud");
            Rehearsal=new RecipeRehearsal(tutor,hud,lit=>flame.SetActive(lit));
            switchPractice=new Button(TogglePractice){name="switchPractice"};
            switchPractice.AddToClassList("chip");hud.Q("log").Add(switchPractice);
            simulator = LogButton(hud,"切换模拟动作",ToggleSimulator);
            reset = LogButton(hud,"重新开始本轮",ResetCurrent);
            var again = new Button(ResetCurrent) {text="再练一次"};
            again.AddToClassList("chip");
            hud.Q("reviewPanel").Add(again);
            hover = new Label {name="objectVerb",pickingMode=PickingMode.Ignore};
            hud.Add(hover);
            hover.style.display=DisplayStyle.None;
            sampleCameraPosition=lessonCamera.transform.position;
            sampleCameraRotation=lessonCamera.transform.rotation;
            sampleField=lessonCamera.fieldOfView;
            var props=Instantiate(workbenchPrefab,transform);
            workbench=new KitchenWorkbench(props,pan,knob,flame,tutor);
            walk=new KitchenWalk(lessonCamera,walkableFloor);
            hands=new CookingHands(workbench,props.transform,pan,lessonCamera,SubmitFromHands,SubmitFinish);
            walkHints=new Label {name="walkHints",pickingMode=PickingMode.Ignore};
            hud.Add(walkHints);
            tutor.ContextChanged+=hands.Sync;
            tutor.ContextChanged+=workbench.Render;
            BeginDesktop();
        }

        static Button LogButton(VisualElement hud,string title,Action click)
        {
            var button=new Button(click){text=title};button.AddToClassList("chip");hud.Q("log").Add(button);
            return button;
        }

        void ResetCurrent()
        {
            if(desktop) BeginDesktop();
            else if(Rehearsal.Active) BeginRehearsal();
            else Restart();
        }

        void ToggleSimulator()
        {
            if(desktop) BeginRehearsal(); else BeginDesktop();
        }

        public void BeginDesktop()
        {
            desktop=true;
            hands.CancelGesture();
            pan.position=burner.position;
            walk.Home();
            walkHints.text=WalkHelp;
            walkHints.style.display=DisplayStyle.Flex;
            workbench.Show(true);
            action.style.display=DisplayStyle.None;
            simulator.text="切换模拟动作";
            switchPractice.text="切换热锅小样";
            if(workflow==null) Rehearsal.Begin(true);
            else {
                Rehearsal.Close();
                if(Rules==null) throw new InvalidOperationException("Workflow must implement IKitchenWorkflow.");
                tutor.InputSource="kitchen";
                Rules.BeginLesson(tutor);
            }
        }

        void LeaveDesktop()
        {
            hands.CancelGesture();
            walkHints.style.display=DisplayStyle.None;
            desktop=false;
            workbench.Show(false);
            tutor.RegisterTarget("stove_switch",marker,"炉灶开关");
            hover.style.display=DisplayStyle.None;
            lessonCamera.transform.SetPositionAndRotation(sampleCameraPosition,sampleCameraRotation);
            lessonCamera.fieldOfView=sampleField;
            simulator.text="返回厨房练习";
        }

        void TogglePractice()
        {
            if(desktop || Rehearsal.Active) Restart(); else BeginDesktop();
        }
        public void BeginRehearsal()
        {
            LeaveDesktop();
            action.style.display=DisplayStyle.None;
            pan.position=burner.position;
            switchPractice.text="切换热锅小样";
            Rehearsal.Start();
        }

        void OnAction()
        {
            if (stage == Stage.PlacePan) PlacePan();
            else if (stage == Stage.Done) Restart();
        }

        public void Restart()
        {
            LeaveDesktop();
            Rehearsal.Close(); tutor.InputSource="stove_sample";
            switchPractice.text="返回厨房练习";
            session = Guid.NewGuid().ToString("N");
            revision = 0;
            stage = Stage.PlacePan;
            events = Array.Empty<KitchenEvent>();
            elapsed = 0; reminded = false;
            pan.position = panRest.position;
            flame.SetActive(false);
            Sync();
            action.text = "放锅上灶";
            action.style.display = DisplayStyle.Flex;
            tutor.ShowSceneCue("先把平底锅放上炉架。点一下锅，或选「放锅上灶」。");
        }

        public void PlacePan()
        {
            if (tutor.IsWaiting || Rehearsal.Active || stage != Stage.PlacePan) return;
            stage = Stage.Ignite;
            revision++;
            events = new[] { new KitchenEvent { action_id="place_pan", outcome="ok" } };
            pan.position = burner.position;
            action.style.display = DisplayStyle.None;
            Sync();
            tutor.ShowSceneCue("锅放稳了。接下来，试着自己点火。", reveal:false);
        }

        public void Ignite()
        {
            if (tutor.IsWaiting || Rehearsal.Active || stage != Stage.Ignite) return;
            stage = Stage.Done;
            revision++;
            events = new[] { events[0], new KitchenEvent { action_id="turn_on_stove", outcome="ok" } };
            flame.SetActive(true);
            Sync();
            tutor.ShowSceneCue("火点着了，热锅这一步完成。接下来就可以炒蛋了。");
            action.text = "再练一次";
            action.style.display = DisplayStyle.Flex;
        }

        void Sync()
        {
            bool placed = stage != Stage.PlacePan, done = stage == Stage.Done;
            tutor.SetContext(new KitchenContext {
                session_id=session, state_version=revision, recipe_id="tomato_egg", recipe_version="1",
                recipe_title="番茄炒蛋", recent_events=events,
                snapshot=new KitchenSnapshot {
                    current_step=done?"done":placed?"ignite":"place_pan",
                    current_step_title=done?"热锅练习完成":placed?"打开炉灶":"放锅上灶",
                    step_count=2, step_number=placed?2:1, stove_on=done,
                    completed_steps=done?new[]{"place_pan","ignite"}:placed?new[]{"place_pan"}:Array.Empty<string>()
                }
            });
        }

        void Update()
        {
            if (doc==null) return;
            if (desktop) {
                if(!Application.isFocused || tutor.InteractingWithPanel || tutor.IsWaiting) {
                    if(hands.Busy) hands.CancelGesture();
                    hover.style.display=DisplayStyle.None;
                    walkHints.style.display=DisplayStyle.None;
                    return;
                }
                walkHints.style.display=DisplayStyle.Flex;
                if(Input.GetKeyDown(KeyCode.F)) { hands.CancelGesture(); walk.Home(); }
                if(Input.GetKeyDown(KeyCode.Escape)) hands.CancelGesture();
                bool looking=Input.GetMouseButton(1);
                if(looking && hands.Busy) hands.CancelGesture();
                if(!hands.Busy) {
                    Vector2 move=new Vector2(Input.GetAxisRaw("Horizontal"),Input.GetAxisRaw("Vertical"));
                    Vector2 look=looking?new Vector2(Input.GetAxis("Mouse X"),Input.GetAxis("Mouse Y")):Vector2.zero;
                    walk.Move(move,look,Time.unscaledDeltaTime);
                }
                hands.Animate();
                if(looking) hover.style.display=DisplayStyle.None;
                else if(!hands.Busy) HandlePointer(Input.mousePosition,HandInput.Hover);
                string status=hands.Status;
                walkHints.text=string.IsNullOrEmpty(status)?WalkHelp:status;
                return;
            }
            if (tutor.IsWaiting) return;
            if (!Rehearsal.Active && stage == Stage.Ignite && !reminded && !tutor.IsWaiting) {
                elapsed += Time.deltaTime;
                if (elapsed >= reminderSeconds) {
                    reminded = true;
                    tutor.ShowSceneCue("锅底还没热。点一下金色圆圈里的开关，把炉火打开。", "stove_switch");
                }
            }
            if (!Input.GetMouseButtonDown(0) || (!Rehearsal.Active && stage == Stage.Done)) return;
            Vector3 mouse = Input.mousePosition;
            var panel = doc.rootVisualElement.panel;
            if (panel == null) return;
            var point = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(mouse.x, Screen.height - mouse.y));
            for (var e=panel.Pick(point); e!=null; e=e.parent)
                if (e is Button || e.name=="card" || e.name=="log" || e.name=="progress" || e.name=="rehearsal" || e.name=="reviewPanel") return;
            var ray = lessonCamera.ScreenPointToRay(mouse);
            if (!Rehearsal.Active && stage == Stage.PlacePan) {
                if (pan.GetComponentInChildren<Renderer>().bounds.IntersectRay(ray)) PlacePan();
            } else {
                Bounds hit = knob.bounds;
                hit.Expand(.085f);
                if (hit.IntersectRay(ray)) { if(Rehearsal.Active) Rehearsal.TryStep(2); else Ignite(); }
            }
        }

        void OnGUI()
        {
            if(!desktop || hands==null || !Application.isFocused || tutor.IsWaiting ||
                tutor.InteractingWithPanel || Input.GetMouseButton(1)) return;
            var e=Event.current;
            if(e.button!=0) return;
            HandInput input;
            switch(e.rawType) {
                case EventType.MouseDown: input=HandInput.Press; break;
                case EventType.MouseDrag: input=HandInput.Hold; break;
                case EventType.MouseUp: input=HandInput.Release; break;
                default: return;
            }
            HandlePointer(new Vector2(e.mousePosition.x,Screen.height-e.mousePosition.y),input);
        }

        void OnApplicationFocus(bool focus)
        {
            if(!focus) hands?.CancelGesture();
        }

        void SubmitFromHands(int step)
        {
            if(workflow==null) Rehearsal.TryStep(step);
            else Rules.Attempt(TomatoEggLesson.Actions[step],TomatoEggLesson.Targets[step],tutor);
        }

        void SubmitFinish(string action)
        {
            if(workflow==null) Rehearsal.TryFinish(action);
            else Rules.Attempt(action,TomatoEggLesson.FinishTarget(action),tutor);
        }

        public bool HandlePointer(Vector2 screen,HandInput input)
        {
            var panel=doc.rootVisualElement.panel;
            hover.style.display=DisplayStyle.None;
            if(!desktop || panel==null || tutor.IsWaiting || tutor.InteractingWithPanel) return false;
            var point=RuntimePanelUtils.ScreenToPanel(panel,new Vector2(screen.x,Screen.height-screen.y));
            for(var e=panel.Pick(point); e!=null; e=e.parent) {
                if(e is Button || e.name=="card" || e.name=="progress" || e.name=="log" || e.name=="reviewPanel") {
                    if(input==HandInput.Release) hands.CancelGesture();
                    return false;
                }
            }
            string id=workbench.Pick(lessonCamera.ScreenPointToRay(screen),!hands.Equipped);
            string verb=hands.Verb(id);
            hands.Pointer(id,screen,input);
            if(!hands.Busy && input==HandInput.Hover && verb.Length>0 && tutor.ActiveHighlight!=id) {
                hover.text=verb;
                hover.style.left=Mathf.Clamp(point.x+15,0,Mathf.Max(0,panel.visualTree.layout.width-168));
                hover.style.top=Mathf.Clamp(point.y+14,0,Mathf.Max(0,panel.visualTree.layout.height-38));
                hover.style.display=DisplayStyle.Flex;
            }
            return verb.Length>0;
        }

        void OnDestroy()
        {
            if(tutor!=null && workbench!=null) {
                tutor.ContextChanged-=hands.Sync;
                tutor.ContextChanged-=workbench.Render;
                hands.CancelGesture();
                foreach(string id in TomatoEggLesson.Targets) tutor.UnregisterTarget(id);
            }
            Rehearsal?.Dispose(); switchPractice?.RemoveFromHierarchy(); action?.RemoveFromHierarchy();
        }
    }
}
