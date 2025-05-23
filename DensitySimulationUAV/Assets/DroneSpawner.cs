using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Assets.Helpers;
using UnityEngine;
using Random = UnityEngine.Random;

/// <summary>
/// Class that takes care of spawning UAVs for different scenarios
/// </summary>
public class DroneSpawner : MonoBehaviour
{
    public static DroneSpawner Instance;
    public GameObject Uav;
    public GameObject SpawnerObject;

    private int nextUavId;
    private float closestDistanceForCollTesting;
    private CollisionFromHistory collTestData;
    private List<string[]> spawnList = new(); //TODO not used for anything
    private bool shouldSkip; //Used to skip a spawn when collision testing

    public DroneSpawnerOptions Options = new();
    public int TotalTests;
    public int CurrentTest;

    private void Start()
    {
        Instance = this;
        Options.Range = Mathf.Sqrt(Options.RangeKm2*1000000);
    }

    /// <summary>
    /// Cancels Invoking new UAV spawning and destroys all currently spawned UAVs
    /// </summary>
    public void ResetDroneSpawner()
    {
        CancelInvoke();
        foreach (Transform child in transform)
        {
            Destroy(child.gameObject);
        }

        nextUavId = 0;
        spawnList = new List<string[]>();
    }

    public void StartDirectionFlightSpawning()
    {
        Options.FlightCountPerHour++;//To make sure it is above 0
        Debug.Log($"FlightCountPerHour: {Options.FlightCountPerHour}");
        //This is not the best way to handle the density, but the overall results are pretty close
        InvokeRepeating(nameof(SpawnDroneWithFlight), 0f, 3600f / Options.FlightCountPerHour);
    }

    /// <summary>
    /// Spawns "empty" UAV GameObject at specified location
    /// </summary>
    /// <param name="spawnPosition">Desired Vector3 transform position</param>
    /// <param name="uavId">Desired ID int of uav</param>
    /// <returns> "UAV object from the GameObject" </returns>
    private UAV SpawnEmptyUavGameObject(Vector3 spawnPosition, int uavId)
    {
        var drone = Instantiate(Uav, spawnPosition, Quaternion.identity);
        drone.transform.parent = SpawnerObject.transform;
        var spawnedUav = drone.GetComponent<UAV>();
        spawnedUav.Id = uavId;
        drone.transform.name = uavId.ToString();
        return spawnedUav;
    }

    private void SpawnDroneWithFlight()
    {
        var spawnPosition = GetNewSpawnPosition();
        var targetPosition = GetNewTargetPosition();
        var spawnedUav = SpawnEmptyUavGameObject(spawnPosition, nextUavId);
        nextUavId++;

        while (!spawnedUav.GenerateStats()) //Generate legit stats for the UAV

        while (true) //TODO this is not legit
        {
            var distance = Vector3.Distance(spawnPosition, targetPosition);
            if (distance > spawnedUav.FlightSpeed * spawnedUav.MaxFlightTimeSeconds * 0.5f)
            {
                targetPosition = GetNewTargetPosition();
                //Debug.Log("Path 2 long");
            }
            else
            {
                break;
            }
        }

        var angle = Angle360(spawnPosition, targetPosition);
        var flightLevel = angle / (360f / TestScheduler.Instance.FlightHeights.Length);
        var flightLevelNormalized = Math.Floor(flightLevel);
        //Debug.Log(angle2);
        //var flightHeights = new List<float> { 40f, 55f, 70f, 85f, 100f, 115f, 130f, 145f, 160f, 175f, 190f, 205f, 220f, 235f, 250f, 265f, 280f, 295f};
        // override with angle

        if (TestScheduler.Instance.AngleHeight)
        {
            spawnedUav.MaxFlightHeight = TestScheduler.Instance.FlightHeights[(int)flightLevelNormalized];
        }

        if (spawnedUav.MaxFlightHeight != 130f) //This is used to force a one flight level scenario
        {
            //return;
        }

        spawnedUav.TargetPosition = new WayPoint(targetPosition, spawnedUav.FlightSpeed);
        spawnedUav.TargetPosition.PushToHeight(0, keepType: false);
        spawnedUav.Range = Options.Range;
        spawnedUav.AssignFlight();
        //spawnList.Add(new[] { tmp_uav.id.ToString(), tmp_uav.transform.position.ToString(), tmp_uav.range.ToString() });
    }

    /// <summary>
    /// Creates a random Vector3 inside the spawn range
    /// </summary>
    /// <returns>The random Vector3</returns>
    private Vector3 GetAnyRandomPosition()
    {
        return new Vector3(Random.Range(-Options.Range / 2, Options.Range / 2), 0, Random.Range(-Options.Range / 2, Options.Range / 2));
    }


    private Vector3 GetAnyPositionInsidePolygons()
    {
        List<double> cumulativeAreas = new List<double> { 0 };
        double totalArea = 0;

        foreach (var circle in Options.Polygons)
        {
            double area = Math.PI * Math.Pow(circle.y, 2);
            totalArea += area;
            cumulativeAreas.Add(totalArea);
        }

        double randomNumber =
            Random.value * totalArea;

        int chosenCircleIndex = 0;
        for (int i = 0; i < cumulativeAreas.Count - 1; i++)
        {
            if (randomNumber >= cumulativeAreas[i] && randomNumber < cumulativeAreas[i + 1])
            {
                chosenCircleIndex = i;
                break;
            }
        }

        Vector2 radius = Random.insideUnitCircle;
        return new Vector3(Options.Polygons[chosenCircleIndex].x + Options.Polygons[chosenCircleIndex].y / 2 * radius.x, 0, Options.Polygons[chosenCircleIndex].z + Options.Polygons[chosenCircleIndex].y / 2 * radius.y);
    }
 
    private Vector3 GetNewSpawnPosition()
    {
        while (true)
        {
            Vector3 spawnPosition;
            switch (Options.SpawnMode)
            {
                case TestScheduler.SpawnMode.SmallerCenter:
                    spawnPosition = GetAnyRandomPosition();
                    break;
                case TestScheduler.SpawnMode.Balanced:
                    spawnPosition = GetAnyRandomPosition();
                    break;
                case TestScheduler.SpawnMode.Polygons:
                {
                    var tmp = Random.value;
                    spawnPosition = tmp > Options.PolygonBias ? GetAnyRandomPosition() : GetAnyPositionInsidePolygons();
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException();
            }

            if (!TestScheduler.Instance.EnableNoFlyZones || CheckDestinationForNoFlyZones(spawnPosition))
            {
                return spawnPosition;
            }
        }
    }
    private Vector3 GetNewTargetPosition()
    {
        while (true)
        {
            Vector3 targetPosition;
            switch (Options.SpawnMode)
            {
                case TestScheduler.SpawnMode.SmallerCenter:
                    //targetPosition = GetAnyPositionOutsideCollisionArea();
                    targetPosition = GetAnyRandomPosition();
                    targetPosition.y = 0;
                    break;
                case TestScheduler.SpawnMode.Balanced:
                    targetPosition = GetAnyRandomPosition();
                    targetPosition.y = 0;
                    break;
                case TestScheduler.SpawnMode.Polygons:
                {
                    var tmp = Random.value;
                    targetPosition = tmp > Options.PolygonBias ? GetAnyRandomPosition() : GetAnyPositionInsidePolygons();
                    targetPosition.y = 0;
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException();
            }

            //If the target position is in nofly zone then generate a new one
            if (!TestScheduler.Instance.EnableNoFlyZones || CheckDestinationForNoFlyZones(targetPosition))
            {
                return targetPosition;
            }
        }
    }

    /// <summary>
    /// Checks whether a Point is inside any NoFly zone 
    /// </summary>
    /// <param name="position">The point to check</param>
    /// <returns>true if a point is inside any NoFly zone</returns>
    private static bool CheckDestinationForNoFlyZones(Vector3 position)
    {
        //If distance to any noflyzone center point is smaller than the diameter of the nofly zone, it is considered that the point is inside a nofly zone
        return TestScheduler.Instance.NoFlyZones.All(noFlyZone => !(Vector3.Distance(position, new Vector3(noFlyZone.x, position.y, noFlyZone.z)) < noFlyZone.y));
    }

    /// <summary>
    /// A function to determine heading from one Vector3 to another as a euler angle projected onto horizontal plane
    /// </summary>
    /// <param name="from"></param>
    /// <param name="to"></param>
    /// <returns>Euler angle as float</returns>
    public float Angle360(Vector3 from, Vector3 to)
    {
        var angle = Vector3.Angle(to - from, Vector3.right);
        var dot = Vector3.Dot((to - from), Vector3.forward);
        if (dot > 0)
        {
            return angle;
        }
        else
        {
            return angle + 180;
        }
    }
}
