using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;

namespace CookCoach.Tutoring
{
    // Explicit fixture for the tutor until the authoritative kitchen controller is connected.
    // Its buttons inject workflow events, and do not simulate cutting or ingredient physics.
    public sealed class RecipeRehearsal
    {
        readonly KitchenTutor tutor;
        readonly Action<bool> showFlame;
        readonly HashSet<string> completed = new HashSet<string>();
        readonly List<KitchenEvent> recent = new List<KitchenEvent>();
        readonly VisualElement controls, actions;
        readonly List<Button> stepButtons = new List<Button>();
        readonly Label title;
        string session;
        int version, errors;
        bool on;
        public bool Active { get; private set; }

        public RecipeRehearsal(KitchenTutor tutor, VisualElement hud, Action<bool> showFlame)
        {
            this.tutor=tutor; this.showFlame=showFlame;
            controls=new VisualElement {name="rehearsal"}; controls.AddToClassList("dark"); hud.Add(controls);
            title=new Label {name="rehearsalTitle"}; controls.Add(title);
            actions=new VisualElement {name="rehearsalActions"}; controls.Add(actions);
            for(int i=0;i<TomatoEggLesson.Actions.Length;i++) {
                int index=i;
                var button=new Button(()=>TryStep(index)){text=TomatoEggLesson.Titles[i]};
                button.AddToClassList("chip"); actions.Add(button); stepButtons.Add(button);
            }
            var reset=new Button(Start){text="重新开始"};reset.AddToClassList("chip");controls.Add(reset);
            tutor.WaitingChanged+=OnWaiting;
            Close();
        }

        public void Start()
        {
            Active=true; tutor.InputSource="simulated_actions";
            completed.Clear(); recent.Clear(); errors=0; version=0; on=false;
            session=Guid.NewGuid().ToString("N"); showFlame(false);
            controls.style.display=DisplayStyle.Flex;
            tutor.SetContext(Context());
            tutor.ShowSceneCue("锅和油已备好。先切番茄或打蛋，两种顺序都可以。");
            OnWaiting(false);
        }

        public void Close()
        {
            Active=false; controls.style.display=DisplayStyle.None;
        }
        public void Dispose() { tutor.WaitingChanged-=OnWaiting; controls.RemoveFromHierarchy(); }
        void OnWaiting(bool busy)
        {
            actions.SetEnabled(!busy);
            if(!Active) return;
            title.text=busy?"动作模拟 · 等待导师回复":"六步联调 · 模拟动作";
            for(int i=0;i<stepButtons.Count;i++) stepButtons[i].SetEnabled(!completed.Contains(TomatoEggLesson.Ids[i]));
        }

        public void TryStep(int i)
        {
            if(!Active || tutor.IsWaiting || i<0 || i>=TomatoEggLesson.Ids.Length || completed.Count==6) return;
            string id=TomatoEggLesson.Ids[i];
            if(completed.Contains(id)) return;
            string reason="";
            if(i==3 && !on) reason="stove_off";
            else if(i>=2 && !(completed.Contains("S1") && completed.Contains("S2"))) reason="prep_incomplete";
            else if(i>=3 && !completed.Contains(TomatoEggLesson.Ids[i-1])) reason="previous_step";
            bool ok=reason.Length==0;
            if(ok) {completed.Add(id); if(i==2)on=true; if(i==5)on=false; showFlame(on);}
            else errors++;
            version++;
            var ev=new KitchenEvent {action_id=TomatoEggLesson.Actions[i], target_id=Target(i),outcome=ok?"ok":"blocked",reason=reason};
            recent.Add(ev); if(recent.Count>10) recent.RemoveAt(0);
            var next=Context();
            tutor.ReportAction(next,ev);
            if(ok) tutor.ShowSceneCue(completed.Count==6?"已关火装盘。可以打开本轮回顾，带走做饭清单。":
                TomatoEggLesson.Titles[i]+"已完成。接下来可以"+next.snapshot.current_step_title+"。",reveal:false);
            OnWaiting(tutor.IsWaiting);
        }

        static string Target(int i) => new[]{"tomato","egg_bowl","stove_switch","pan","pan","plate"}[i];
        KitchenContext Context()
        {
            int next=Array.FindIndex(TomatoEggLesson.Ids,id=>!completed.Contains(id));
            return new KitchenContext {session_id=session,recipe_id="tomato_egg",recipe_version="1",recipe_title="番茄炒蛋",
                state_version=version,recent_events=recent.ToArray(),snapshot=new KitchenSnapshot {
                    completed_steps=TomatoEggLesson.Ids.Where(completed.Contains).ToArray(),stove_on=on,related_error_count=errors,
                    current_step=next<0?"done":TomatoEggLesson.Ids[next],current_step_title=next<0?"练习完成":TomatoEggLesson.Titles[next],
                    step_number=next<0?6:next+1,step_count=6}};
        }
    }
}
