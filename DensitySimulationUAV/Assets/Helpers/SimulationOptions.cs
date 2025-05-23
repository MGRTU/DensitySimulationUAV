using System;
using UnityEngine;
using static TestScheduler;

/// <summary>
/// Class that represents the options of a simulation
/// </summary>
[Serializable]
public class SimulationOptions
{
    public int RangeKm2;
    public int CollisionRangeKm2;
    public int StartDensity;
    public int CurrentDensity;
    public int EndDensity;
    public int Step;
    public float TimeFrameMinutes;
    public SpawnMode TestMode;
    public Vector3[] Polygons;
    public bool AngleHeight;
    public bool CollisionEvasion;
    public bool NoFlyZones;
    public float FlightLevelHeight;
}
