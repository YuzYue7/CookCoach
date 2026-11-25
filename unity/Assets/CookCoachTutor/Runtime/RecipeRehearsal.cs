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
        bool sceneInput;
        readonly HashSet<string> completed = new HashSet<string>();
        readonly List<KitchenEvent> recent = new List<KitchenEvent>();
        readonly VisualElement controls, actions;
        readonly List<Button> stepButtons = new List<Button>();
        readonly Button primary, more;
        bool menuOpen;
        string session;
        int version, errors;
        bool on, oilAdded, saltAdded;
        readonly Button oilButton, saltButton;
        int finishIndex;
        public bool Active { get; private set; }

        public RecipeRehearsal(KitchenTutor tutor, VisualElement hud, Action<bool> showFlame)
        {
            this.tutor=tutor; this.showFlame=showFlame;
            controls=new VisualElement {name="rehearsal"};
            hud.Q("actionSlot").Add(controls);
            var row=new VisualElement();row.AddToClassList("horizontal");controls.Add(row);
            primary=new Button(()=>{
                int step=NextStep();
                if(step<0) Start();
                else if(step==3 && !oilAdded) TrySeason(TomatoEggLesson.Oil);
                else TryStep(step);
            }) {name="nextAction"};
            more=new Button(()=>SetMenu(!menuOpen)) {name="otherActions",text="其他动作"};
            more.AddToClassList("textAction");
            row.Add(primary);row.Add(more);
            actions=new VisualElement {name="rehearsalActions"};controls.Add(actions);
            for(int i=0;i<TomatoEggLesson.Actions.Length;i++) {
                int index=i;
                var button=new Button(()=>TryStep(index)){text=TomatoEggLesson.Titles[i]};
                button.AddToClassList("alternate");actions.Add(button);stepButtons.Add(button);
            }
            oilButton=new Button(()=>TrySeason(TomatoEggLesson.Oil)){text="倒油"};
            saltButton=new Button(()=>TrySeason(TomatoEggLesson.Salt)){text="撒盐（可选）"};
            actions.Add(oilButton);actions.Add(saltButton);
            var reset=new Button(Start){text="重新开始"};reset.AddToClassList("alternate");actions.Add(reset);
            tutor.WaitingChanged+=OnWaiting;
            Close();
        }

        public void Start() => Begin(false);

        public void Begin(bool useSceneObjects)
        {
            sceneInput=useSceneObjects;
            Active=true; tutor.InputSource=sceneInput ? "desktop_objects_local_rules" : "simulated_actions";
            completed.Clear(); recent.Clear(); errors=0; version=0; on=false; oilAdded=false; saltAdded=false; finishIndex=0;
            session=Guid.NewGuid().ToString("N"); showFlame(false);
            controls.style.display=sceneInput ? DisplayStyle.None : DisplayStyle.Flex;
            SetMenu(false);
            tutor.SetContext(Context());
            tutor.ShowSceneCue(sceneInput?"先拿砧板上的刀，点番茄切三下。也可以先按住碗沿，绕圈打蛋。":"先切番茄或打散鸡蛋，开火后倒油。");
            OnWaiting(false);
        }

        public void Close()
        {
            Active=false; controls.style.display=DisplayStyle.None;
        }
        public void Dispose() { tutor.WaitingChanged-=OnWaiting; controls.RemoveFromHierarchy(); }
        int NextStep() => Array.FindIndex(TomatoEggLesson.Ids,id=>!completed.Contains(id));
        void SetMenu(bool open)
        {
            menuOpen=open;
            actions.style.display=open?DisplayStyle.Flex:DisplayStyle.None;
            more.text=open?"收起动作":"其他动作";
        }

        void OnWaiting(bool busy)
        {
            if(!Active) return;
            int next=NextStep();
            primary.text=next<0?"再练一次":"模拟"+(next==5?TomatoEggLesson.ActionName(TomatoEggLesson.FinishActions[finishIndex]):TomatoEggLesson.Titles[next]);
            if(next==3 && !oilAdded) primary.text="模拟倒油";
            oilButton.SetEnabled(!busy && !oilAdded);
            saltButton.SetEnabled(!busy && !saltAdded);
            primary.SetEnabled(!busy);
            more.style.display=next<0?DisplayStyle.None:DisplayStyle.Flex;
            for(int i=0;i<stepButtons.Count;i++) {
                stepButtons[i].style.display=i!=next && !completed.Contains(TomatoEggLesson.Ids[i])?DisplayStyle.Flex:DisplayStyle.None;
                stepButtons[i].SetEnabled(!busy);
            }
        }

        public void TryStep(int i)
        {
            if(!Active || tutor.IsWaiting || i<0 || i>=TomatoEggLesson.Ids.Length || completed.Count==6) return;
            if(i==5) {
                TryFinish(TomatoEggLesson.FinishActions[finishIndex]);
                return;
            }
            string id=TomatoEggLesson.Ids[i];
            if(completed.Contains(id)) return;
            SetMenu(false);
            string reason="";
            if(i==3 && !on) reason="stove_off";
            else if(i>=2 && !(completed.Contains("S1") && completed.Contains("S2"))) reason="prep_incomplete";
            else if(i>=3 && !completed.Contains(TomatoEggLesson.Ids[i-1])) reason="previous_step";
            else if(i==3 && !oilAdded) reason="oil_missing";
            bool ok=reason.Length==0;
            if(ok) {completed.Add(id); if(i==2)on=true; showFlame(on);}
            Publish(TomatoEggLesson.Actions[i],TomatoEggLesson.Targets[i],reason);
        }

        public void TrySeason(string action)
        {
            if(!Active || tutor.IsWaiting || completed.Contains("S6")) return;
            bool oil=action==TomatoEggLesson.Oil;
            if(!oil && action!=TomatoEggLesson.Salt) return;
            string reason="";
            if(oil ? oilAdded : saltAdded) reason="already_done";
            else if(oil && (!on || NextStep()!=3)) reason="oil_window";
            else if(!oil && (!on || NextStep()!=5 || TomatoEggLesson.FinishActions[finishIndex]!=TomatoEggLesson.Mix)) reason="salt_window";
            if(reason.Length==0) {
                if(oil) oilAdded=true; else saltAdded=true;
            }
            Publish(action,oil?TomatoEggLesson.OilBottle:TomatoEggLesson.SaltShaker,reason);
        }

        public void TryFinish(string action)
        {
            if(!Active || tutor.IsWaiting || completed.Contains("S6")) return;
            int attemptedIndex=Array.IndexOf(TomatoEggLesson.FinishActions,action);
            if(attemptedIndex<0) return;
            bool valid=completed.Contains("S5") && attemptedIndex==finishIndex;
            if(valid) {
                if(action==TomatoEggLesson.HeatOff) {on=false;showFlame(false);}
                if(action==TomatoEggLesson.Serve) completed.Add("S6");
                else finishIndex++;
            }
            Publish(action,TomatoEggLesson.FinishTarget(action),valid?"":attemptedIndex<finishIndex?"already_done":"previous_step");
        }

        void Publish(string action,string target,string reason)
        {
            bool ok=string.IsNullOrEmpty(reason);
            if(!ok) errors++;
            version++;
            var ev=new KitchenEvent {action_id=action,target_id=target,outcome=ok?"ok":"blocked",reason=reason};
            recent.Add(ev); if(recent.Count>10) recent.RemoveAt(0);
            var next=Context();
            tutor.ReportAction(next,ev);
            if(ok) tutor.ShowSceneCue(completed.Count==6?"已关火装盘。可以打开本轮回顾，带走做饭清单。":
                (next.snapshot.current_step=="S4" && !oilAdded ? "火已开。把台面右侧的黑色橄榄油瓶拖到锅里倒油，再倒蛋液。" :
                action==TomatoEggLesson.Oil ? "油已倒好，把蛋碗拖到锅上倒入蛋液。" :
                next.snapshot.finish_phase==TomatoEggLesson.Mix ? (saltAdded?"盐已撒好。按住锅内绕圈合炒。":"熟蛋已回锅。可把油瓶旁的白色盐罐拖到锅里撒盐，或直接绕圈合炒。") :
                next.snapshot.current_step=="S6" ? TomatoEggLesson.FinishCue(next.snapshot.finish_phase) : sceneInput ? NextGesture(next.snapshot.current_step) : TomatoEggLesson.ActionName(action)+"已完成。接下来可以"+next.snapshot.current_step_title+"。"),reveal:false);
            OnWaiting(tutor.IsWaiting);
        }

        static string NextGesture(string step)
        {
            switch(step) {
                case "S1": return "鸡蛋打好了。拿起小刀，把番茄切成块。";
                case "S2": return "番茄切好了。按住碗沿，绕一圈打散鸡蛋。";
                case "S3": return "备料齐了。点炉台前方靠左的旋钮开火。";
                case "S4": return "火已点着。把蛋碗拖到锅上，松手倒入蛋液。";
                case "S5": return "熟蛋已盛出。把砧板上的番茄块拖入锅中。";
                default: return "";
            }
        }

        KitchenContext Context()
        {
            int next=NextStep();
            return new KitchenContext {session_id=session,recipe_id="tomato_egg",recipe_version="2",recipe_title="番茄炒蛋",
                state_version=version,recent_events=recent.ToArray(),snapshot=new KitchenSnapshot {
                    completed_steps=TomatoEggLesson.Ids.Where(completed.Contains).ToArray(),stove_on=on,oil_added=oilAdded,salt_added=saltAdded,related_error_count=errors,
                    current_step=next<0?"done":TomatoEggLesson.Ids[next],current_step_title=next<0?"练习完成":next==3 && !oilAdded?"倒油后炒蛋":next==5?TomatoEggLesson.ActionName(TomatoEggLesson.FinishActions[finishIndex]):TomatoEggLesson.Titles[next],
                    finish_phase=next==5?TomatoEggLesson.FinishActions[finishIndex]:"",
                    step_number=next<0?6:next+1,step_count=6}};
        }
    }
}
