using UnityEngine;

namespace Assets.Helpers
{
    /// <summary>
    /// Class that represents a collision in progress between UAVs
    /// </summary>
    public class CollisionInstance
    {
        public UAV Uav;
        public int InstanceId;
        public float LastDistanceSquared;
        public float LastDistance;
        public int CollisionLevel;
        public float OtherUavCrashRadius;
        public bool[] Level = new bool[4];
    }
}
