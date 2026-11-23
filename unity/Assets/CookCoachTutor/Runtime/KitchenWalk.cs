using System;
using UnityEngine;

namespace CookCoach.Tutoring
{
    // The mask samples the kitchen's walls, counters, stools and island at three body heights.
    // Each 4 cm floor cell already includes 20 cm of clearance from solid geometry.
    public sealed class KitchenWalk
    {
        readonly Camera camera;
        readonly string[] floor;
        float yaw, pitch;

        public KitchenWalk(Camera camera, TextAsset floorMap)
        {
            this.camera=camera;
            floor=floorMap.text.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries);
        }

        public void Home()
        {
            camera.transform.position=new Vector3(-1.51f,1.97f,1.16f);
            camera.transform.LookAt(new Vector3(-1.46f,1.22f,2.24f));
            camera.fieldOfView=62;
            yaw=camera.transform.eulerAngles.y;
            pitch=camera.transform.eulerAngles.x;
        }

        bool CanStand(Vector3 p)
        {
            int x=Mathf.RoundToInt((p.x+3.75f)/.04f), z=Mathf.RoundToInt((p.z+3.30f)/.04f);
            return z>=0 && z<floor.Length && x>=0 && x<floor[z].Length && floor[z][x]=='.';
        }

        public void Move(Vector2 axes,Vector2 look,float deltaTime)
        {
            yaw+=look.x*2.1f; pitch=Mathf.Clamp(pitch-look.y*2.1f,-72f,78f);
            camera.transform.rotation=Quaternion.Euler(pitch,yaw,0);
            var input=Vector2.ClampMagnitude(axes,1);
            var displacement=Quaternion.Euler(0,yaw,0)*new Vector3(input.x,0,input.y)*1.45f*Mathf.Min(deltaTime,.08f);
            var p=camera.transform.position;
            int n=Mathf.Max(1,Mathf.CeilToInt(displacement.magnitude/.025f));
            for(int i=0;i<n;i++) {
                var side=p+Vector3.right*(displacement.x/n);
                if(CanStand(side)) p=side;
                var forward=p+Vector3.forward*(displacement.z/n);
                if(CanStand(forward)) p=forward;
            }
            camera.transform.position=p;
        }
    }
}
