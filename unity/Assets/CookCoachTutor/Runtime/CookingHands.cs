using System;
using UnityEngine;

namespace CookCoach.Tutoring
{
    public enum HandInput { Hover, Press, Hold, Release }

    // Gesture details stay local. Only finished actions and actual rule violations reach the workflow.
    public sealed class CookingHands
    {
        readonly KitchenWorkbench bench;
        readonly Camera camera;
        readonly Action<int> submit;
        readonly Action<string> finish;
        readonly Transform platedEggs;
        bool mixing;
        string Phase => TomatoEggLesson.FinishPhase(state);
        readonly Transform knife, whole, yolks, parts, board, bowl;
        readonly Transform pan, panFood;
        GameObject liftedSource;
        readonly Vector3 knifeRest, wholeSize;
        readonly Quaternion knifeRotation, yolkRest;
        KitchenSnapshot state;
        string session, carrying, pressedStation, selectedStation;
        GameObject heldFood;
        bool equipped, stirring, hasAngle;
        int cuts;
        float lastCut=-10, angle, winding;
        string feedback;
        float feedbackUntil;

        public CookingHands(KitchenWorkbench bench,Transform props,Transform pan,Camera camera,Action<int> submit,Action<string> finish)
        {
            this.bench=bench; this.pan=pan; this.camera=camera; this.submit=submit;this.finish=finish;platedEggs=props.Find("plate/Eggs");
            panFood=props.Find("Pan tomatoes");
            board=props.Find("tomato"); bowl=props.Find("egg_bowl");
            knife=board.Find("Paring knife"); whole=board.Find("Whole"); parts=board.Find("Pieces"); yolks=bowl.Find("Raw");
            knifeRest=knife.localPosition; knifeRotation=knife.localRotation; wholeSize=whole.localScale; yolkRest=yolks.localRotation;
        }

        bool Done(int step)=>state!=null && Array.IndexOf(state.completed_steps,TomatoEggLesson.Ids[step])>=0;
        public bool Busy=>stirring || carrying!=null || pressedStation!=null;
        public bool Equipped=>equipped;
        public string Status {
            get {
                if(stirring) return (mixing?"锅中合炒　":"打蛋　")+Mathf.Min(100,Mathf.RoundToInt(Mathf.Abs(winding)/300f*100))+"%";
                if(carrying!=null) return carrying=="pan"?"移到盘子上松手装盘":"移到平底锅上松手下锅";
                if(Time.unscaledTime<feedbackUntil) return feedback;
                if(selectedStation=="plate") return "已选熟蛋　·　点锅让熟蛋回锅";
                if(selectedStation=="pan") return "已选平底锅　·　点盘子尝试装盘";
                if(equipped) return "菜刀在手　·　点番茄切块　"+cuts+" / 3";
                return "";
            }
        }

        public void Sync(KitchenContext c)
        {
            CancelGesture();
            if(session!=c.session_id || Array.IndexOf(c.snapshot.completed_steps,"S1")>=0) PutKnifeBack();
            session=c.session_id; state=c.snapshot;
        }

        void PutKnifeBack()
        {
            cuts=0;equipped=false;lastCut=-10;
            knife.localPosition=knifeRest;knife.localRotation=knifeRotation;whole.localScale=wholeSize;
        }

        public void CancelGesture()
        {
            if(mixing) {
                bench.PanEggs.localRotation=Quaternion.identity;
                panFood.localRotation=Quaternion.identity;
            }
            pressedStation=null;selectedStation=null;
            stirring=false;mixing=false;hasAngle=false;winding=0;
            yolks.localRotation=yolkRest;
            if(heldFood!=null){heldFood.SetActive(false);UnityEngine.Object.Destroy(heldFood);}
            if(liftedSource!=null) liftedSource.SetActive(true);
            if(carrying=="pan") panFood.gameObject.SetActive(true);
            liftedSource=null;heldFood=null;carrying=null;feedbackUntil=0;
        }

        void Tell(string text) {feedback=text;feedbackUntil=Time.unscaledTime+2.2f;}

        public void Animate()
        {
            if(!equipped) return;
            float elapsed=Time.unscaledTime-lastCut;
            float stroke=elapsed<.25f?Mathf.Sin(Mathf.PI*elapsed/.25f):0;
            knife.localPosition=whole.localPosition+new Vector3(.036f*cuts-.068f,.175f-.098f*stroke,.005f);
            knife.localRotation=Quaternion.Euler(-15,86,-28+stroke*20);
        }

        public string Verb(string id)
        {
            if(state==null || state.current_step=="done") return "";
            if(Phase!="") {
                if(id=="plate" && Phase==TomatoEggLesson.ReturnEgg) return "拖熟蛋回锅";
                if(id=="pan" && Phase==TomatoEggLesson.Mix) return "按住锅内绕圈合炒";
                if(id=="stove_switch" && Phase==TomatoEggLesson.HeatOff) return "关闭炉火";
                if(id=="pan" && Phase==TomatoEggLesson.Serve) return "拖向空盘装盘";
                return "";
            }
            switch(id){
                case "knife": return Done(0)?"":"拿起菜刀";
                case "tomato": return !Done(0)?(equipped?"切番茄":"先拿砧板旁的刀"):!Done(4)?"拖番茄块入锅":"";
                case "egg_bowl": return !Done(1)?"按住碗边，绕圈打蛋":!Done(3)?"拖蛋液入锅":"";
                case "stove_switch": return !Done(2)?"旋开炉火":"";
                case "pan": return !Done(2)?"检查平底锅":!Done(3)?"从蛋碗拖入蛋液":"从砧板拖入番茄";
                case "plate": return "装盘位置";
                default:return "";
            }
        }

        public void Pointer(string id,Vector2 screen,HandInput input)
        {
            if(state==null || state.current_step=="done") return;
            if(input==HandInput.Hover) return;
            if(input==HandInput.Release && pressedStation!=null) {
                string from=pressedStation;
                if(from=="pan" && id=="plate" || from=="plate" && id=="pan") {
                    CancelGesture();
                    finish(from=="pan"?TomatoEggLesson.Serve:TomatoEggLesson.ReturnEgg);
                    return;
                }
                if(id==from && !stirring) {
                    CancelGesture();
                    if(from=="pan" || Phase==TomatoEggLesson.ReturnEgg) selectedStation=from;
                    return;
                }
                pressedStation=null;
            }
            if(carrying!=null){
                MoveFood(screen);
                if(input!=HandInput.Release) return;
                string source=carrying;
                CancelGesture();
                if((source=="pan" || source=="tomato" || source=="egg_bowl") && id=="plate") finish(TomatoEggLesson.Serve);
                else if(source=="cooked_eggs" && id=="pan") finish(TomatoEggLesson.ReturnEgg);
                else if(id=="pan" && (source=="egg_bowl" || source=="tomato")) submit(source=="egg_bowl"?3:4);
                else Tell("食材已放回原位，可以重新拖动");
                return;
            }
            if(stirring){
                if(input==HandInput.Release){string cue=mixing?"按住锅内，连续绕一圈":"按住碗沿，连续绕一圈";CancelGesture();Tell(cue);}
                else if(input==HandInput.Hold) Stir(screen);
                return;
            }
            if(input!=HandInput.Press) return;
            if(Phase!="") {
                if(selectedStation=="plate" && id=="pan" || selectedStation=="pan" && id=="plate") {
                    string action=selectedStation=="plate"?TomatoEggLesson.ReturnEgg:TomatoEggLesson.Serve;
                    CancelGesture();finish(action);return;
                }
                selectedStation=null;
                if(id=="plate" || id=="pan") pressedStation=id;
                if(id=="plate" && Phase==TomatoEggLesson.ReturnEgg) Lift("cooked_eggs",platedEggs,screen);
                else if(id=="pan" && Phase==TomatoEggLesson.Mix) {mixing=true;stirring=true;Stir(screen);}
                else if(id=="stove_switch") finish(TomatoEggLesson.HeatOff);
                else if(id=="pan" && Phase==TomatoEggLesson.Serve) Lift("pan",pan,screen);
                else if(id!=null) Tell(TomatoEggLesson.FinishCue(Phase));
                return;
            }
            if(id=="knife" && !Done(0)){equipped=true;feedbackUntil=0;Animate();return;}
            if(id=="tomato" && !Done(0)){
                if(!equipped){Tell("先点砧板旁的菜刀，再切番茄");return;}
                if(Time.unscaledTime-lastCut<.22f) return;
                lastCut=Time.unscaledTime;cuts++;
                if(cuts==3) submit(0);
                else whole.localScale=new Vector3(1-.14f*cuts,1-.06f*cuts,1)*wholeSize.x;
                return;
            }
            if(id=="egg_bowl" && !Done(1)){stirring=true;winding=0;hasAngle=false;Stir(screen);return;}
            if(id=="egg_bowl" && !Done(3)) Lift(id,bowl,screen);
            else if(id=="tomato" && !Done(4)) Lift(id,parts,screen);
            else if(id=="stove_switch" && !Done(2)) submit(2);
            else if(id=="pan" && !Done(2)) submit(3);
            else if(id=="plate" && !Done(4)) finish(TomatoEggLesson.Serve);
            else if(id=="pan") Tell(Verb(id));
        }

        void Stir(Vector2 mouse)
        {
            Vector3 center=mixing?pan.position+Vector3.up*.032f:bowl.position+Vector3.up*.065f;
            Vector2 projected=camera.WorldToScreenPoint(center);
            float radius=Vector2.Distance(projected,camera.WorldToScreenPoint(center+camera.transform.right*(mixing?.167f:.109f)));
            var offset=mouse-projected;
            if(offset.magnitude<radius*.17f || offset.magnitude>radius*1.65f){hasAngle=false;return;}
            float next=Mathf.Atan2(offset.y,offset.x)*Mathf.Rad2Deg;
            if(hasAngle){
                float delta=Mathf.DeltaAngle(angle,next);
                if(Mathf.Abs(delta)<100) winding+=delta;
            }
            angle=next;hasAngle=true;
            var rotation=Quaternion.Euler(0,-winding,0);
            if(mixing) {bench.PanEggs.localRotation=rotation;panFood.localRotation=rotation;}
            else yolks.localRotation=rotation*yolkRest;
            if(Mathf.Abs(winding)>=300) {
                bool combined=mixing;CancelGesture();
                if(combined) finish(TomatoEggLesson.Mix); else submit(1);
            }
        }

        void Lift(string id,Transform source,Vector2 screen)
        {
            carrying=id;
            heldFood=UnityEngine.Object.Instantiate(source.gameObject);
            // The pan also carries the cooked tomatoes, which belong to the workbench hierarchy.
            if(id=="pan"){
                var tomatoes=UnityEngine.Object.Instantiate(panFood.gameObject,heldFood.transform);
                tomatoes.transform.localPosition=Vector3.up*.014f;
            }
            liftedSource=source.gameObject;
            liftedSource.SetActive(false);
            if(id=="pan") panFood.gameObject.SetActive(false);
            heldFood.SetActive(true);
            MoveFood(screen);
        }

        void MoveFood(Vector2 screen)
        {
            var ray=camera.ScreenPointToRay(screen);
            var plane=new Plane(Vector3.up,new Vector3(0,1.38f,0));
            if(plane.Raycast(ray,out float distance) && distance<3f)
                {
                    heldFood.transform.position=ray.GetPoint(distance);
                    float tilt=carrying=="egg_bowl"?-22f:carrying=="pan"?-12f:0f;
                    heldFood.transform.rotation=Quaternion.AngleAxis(tilt,camera.transform.forward)*liftedSource.transform.rotation;
                }
        }
    }
}
