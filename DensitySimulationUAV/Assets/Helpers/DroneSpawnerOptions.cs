using UnityEngine;

namespace Assets.Helpers
{
    /// <summary>
    /// Class that represents Options of a DroneSpawner object
    /// </summary>
    public class DroneSpawnerOptions
    {
        public float PolygonBias;
        public Vector3[] Polygons;
        public Vector3 CollisionArea;
        public int FlightCountPerHour;
        public float Range;
        public float RangeKm2;
        public TestScheduler.SpawnMode SpawnMode;
        public int CircleAnglesCount;
    }
}
