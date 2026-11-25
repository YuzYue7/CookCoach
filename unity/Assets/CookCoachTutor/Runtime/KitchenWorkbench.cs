using System;
using System.Collections.Generic;
using UnityEngine;

// Implement on the component assigned to StovePractice.workflow to supply the team's recipe rules.
namespace CookCoach.Tutoring
{
    public interface IKitchenWorkflow
    {
        void BeginLesson(KitchenTutor tutor);
        void Attempt(string actionId, string targetId, KitchenTutor tutor);
    }

    public sealed class KitchenWorkbench
    {
        readonly GameObject props;
        readonly KitchenTutor tutor;
        readonly GameObject flame;
        readonly Transform whole, pieces, raw, beaten, eggs, finished, panTomatoes;
        readonly Transform oilFilm, saltGrains;
        readonly Dictionary<string, Transform> anchors = new();
        readonly Dictionary<string, Bounds> hitAreas = new();
        public Transform PanEggs { get; }
        public Transform OilBottle { get; }
        public Vector3 OilSpout { get; }
        KitchenSnapshot state;
        static readonly string[] Names = {"砧板上的番茄", "蛋碗", "炉灶开关", "平底锅", "装盘位置"};

        public KitchenWorkbench(GameObject props, Transform pan, Renderer knob, GameObject flame, KitchenTutor tutor, Transform oliveOilBody, Transform oliveOilCap)
        {
            this.props = props;
            this.tutor = tutor;
            this.flame = flame;
            Transform board = props.transform.Find("tomato"), bowl = props.transform.Find("egg_bowl"), plate = props.transform.Find("plate");
            whole = board.Find("Whole"); pieces = board.Find("Pieces");
            raw = bowl.Find("Raw"); beaten = bowl.Find("Beaten");
            eggs = plate.Find("Eggs"); finished = plate.Find("Finished");
            panTomatoes = props.transform.Find("Pan tomatoes");
            oilFilm=props.transform.Find("Oil in pan");
            saltGrains=props.transform.Find("Salt in pan");
            // The imported kitchen groups the bottle and an unrelated white jar under one parent.
            // Attach only the original bottle body and cap to the draggable object.
            var oilBounds = oliveOilBody.GetComponent<Renderer>().bounds;
            oilBounds.Encapsulate(oliveOilCap.GetComponent<Renderer>().bounds);
            OilBottle = new GameObject(TomatoEggLesson.OilBottle).transform;
            OilBottle.SetParent(props.transform.parent);
            OilBottle.position = new Vector3(oilBounds.center.x, oilBounds.min.y, oilBounds.center.z);
            oliveOilBody.SetParent(OilBottle, true);
            oliveOilCap.SetParent(OilBottle, true);
            var capBounds = oliveOilCap.GetComponent<Renderer>().bounds;
            OilSpout = OilBottle.InverseTransformPoint(new Vector3(capBounds.center.x, capBounds.max.y, capBounds.center.z));
            Add(TomatoEggLesson.OilBottle, OilBottle, Vector3.up * oilBounds.extents.y, oilBounds.size);
            Add(TomatoEggLesson.SaltShaker,props.transform.Find(TomatoEggLesson.SaltShaker),Vector3.up*.05f,new Vector3(.089f,.108f,.089f));
            PanEggs=UnityEngine.Object.Instantiate(eggs,pan,false);
            PanEggs.name="Returned eggs";
            PanEggs.localPosition=new Vector3(.018f,.038f,-.019f);
            PanEggs.gameObject.SetActive(false);
            Add("tomato",board, new Vector3(-.035f,.069f,0), new Vector3(.45f,.14f,.29f));
            Add("egg_bowl",bowl, new Vector3(0,.054f,0), new Vector3(.24f,.13f,.24f));
            Add("pan",pan, new Vector3(0,.038f,0), new Vector3(.35f,.085f,.35f));
            Add("plate",plate, new Vector3(0,.027f,0), new Vector3(.325f,.067f,.325f));
            Add("stove_switch",knob.transform,knob.transform.InverseTransformPoint(knob.bounds.center), new Vector3(.089f,.075f,.089f));
        }

        void Add(string id, Transform parent, Vector3 offset, Vector3 size)
        {
            var anchor = new GameObject(id + " focus").transform;
            anchor.SetParent(parent,false);
            anchor.localPosition = offset;
            anchors.Add(id,anchor);
            hitAreas.Add(id,new Bounds(Vector3.zero,size));
        }

        public void Show(bool visible)
        {
            props.SetActive(visible);
            PanEggs.gameObject.SetActive(false);
            foreach (var pair in anchors) {
                if (visible) {
                    int step = Array.IndexOf(TomatoEggLesson.Targets,pair.Key);
                    string label = pair.Key==TomatoEggLesson.OilBottle ? "食用油瓶" : pair.Key==TomatoEggLesson.SaltShaker ? "盐罐" : step == 5 ? Names[4] : Names[Mathf.Min(step,3)];
                    tutor.RegisterTarget(pair.Key,pair.Value,label);
                } else tutor.UnregisterTarget(pair.Key);
            }
            state = null;
        }

        public void Render(KitchenContext context)
        {
            if (!props.activeSelf || !TomatoEggLesson.Matches(context)) return;
            state = context.snapshot;
            bool Has(string id) => Array.IndexOf(state.completed_steps,id) >= 0;
            bool cut = Has("S1"), mixed = Has("S2"), fried = Has("S4"), cooked = Has("S5"), done = Has("S6");
            whole.gameObject.SetActive(!cut);
            pieces.gameObject.SetActive(cut && !cooked);
            raw.gameObject.SetActive(!mixed);
            beaten.gameObject.SetActive(mixed && !fried);
            string phase=TomatoEggLesson.FinishPhase(state);
            bool returned=phase==TomatoEggLesson.Mix || phase==TomatoEggLesson.HeatOff || phase==TomatoEggLesson.Serve;
            eggs.gameObject.SetActive(fried && !returned && !done);
            PanEggs.gameObject.SetActive(returned);
            panTomatoes.gameObject.SetActive(cooked && !done);
            finished.gameObject.SetActive(done);
            oilFilm.gameObject.SetActive(state.oil_added && !fried);
            saltGrains.gameObject.SetActive(state.salt_added && phase==TomatoEggLesson.Mix);
            flame.SetActive(state.stove_on);
        }

        public string Pick(Ray ray, bool includeKnife=true)
        {
            if(includeKnife && StepFor("tomato")==0) {
                var handle=props.transform.Find("tomato/Paring knife");
                Bounds knifeBounds=new Bounds(handle.position+Vector3.up*.008f,new Vector3(.072f,.056f,.246f));
                if(knifeBounds.IntersectRay(ray,out float hit) && hit<=1.95f) return "knife";
            }
            float nearest = 1.95f;
            string result = null;
            foreach (var pair in hitAreas) {
                Bounds bounds = pair.Value;
                bounds.center = anchors[pair.Key].position;
                if (bounds.IntersectRay(ray,out float distance) && distance < nearest) {
                    nearest = distance;
                    result = pair.Key;
                }
            }
            return result;
        }

        public int StepFor(string id)
        {
            if (state == null || state.current_step == "done" || id == null) return -1;
            int step = Array.IndexOf(TomatoEggLesson.Targets,id);
            if (id == "pan" && Array.IndexOf(state.completed_steps,"S4") >= 0) step = 4;
            return step >= 0 && Array.IndexOf(state.completed_steps,TomatoEggLesson.Ids[step]) < 0 ? step : -1;
        }
    }
}
