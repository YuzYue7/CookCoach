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
            BeginRehearsal();
        }

        void TogglePractice()
        {
            if(Rehearsal.Active) Restart(); else BeginRehearsal();
        }
        public void BeginRehearsal()
        {
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
            Rehearsal.Close(); tutor.InputSource="stove_sample";
            switchPractice.text="切换六步联调";
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
            if (doc==null || tutor.IsWaiting) return;
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

        void OnDestroy()
        {
            if (tutor != null) tutor.UnregisterTarget("stove_switch");
            Rehearsal?.Dispose(); switchPractice?.RemoveFromHierarchy(); action?.RemoveFromHierarchy();
        }
    }
}
