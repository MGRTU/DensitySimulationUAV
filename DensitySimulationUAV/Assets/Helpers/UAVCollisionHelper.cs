using System;
using System.Collections.Generic;
using Assets.Helpers;
using UnityEngine;

/// <summary>
/// A Class that represents a collision handling code between UAVs
/// </summary>
public class UAVCollisionHelper : MonoBehaviour
{
    public bool Triggered = false;
    public UAV Uav;
    public bool InsideCollisionZone = false;
    private static float _cutoffLimit;
    public bool DontCutThisCollider;
    public bool[] Collisions;
    public float[] Limits;
    public int UniqueId;
    [SerializeField] private float[] limitsSquared;
    public int TriggerCount;
    public float CrashRadius;
    
    public List<CollisionInstance> CollisionInstances = new();
    public int[] CollisionCounts;
    public int[] MaxCollisionCounts;
    ///For GPU collision detection
    public int sphereIndex;

    ///////////////////////////////

    private void Awake()
    {
        Collisions = new bool[4];
        limitsSquared = new float[4];
        sphereIndex = GPUSphereCollisionSystem.instance.AddSphere(this.transform, 1f);
        GPUSphereCollisionSystem.instance.SubscribeToCollisionEnter(sphereIndex, HandleGPUCollision);
    }

    void HandleGPUCollision(int sphere1Index, int sphere2Index)
    {
        //Debug.Log($"I {sphere1Index} Collided with {sphere2Index}");

        //Ja ir tuvāk par robežu - tad ir collision
        //Ja nav ieslēgts cutoff, tad vienmēr būs collision
        //Ja ir ieslēgts override, tad vienmēr būs collision

        // Or using TryGetComponent pattern
        if (!GPUSphereCollisionSystem.instance.TryGetComponentFromSphereID<UAVCollisionHelper>(sphere2Index, out var otherUavCollisionHelper))
        {
            Debug.Log("Failed to get other UAVCollisionHelper!");
            return;
        }

        //If a collision is detected then a Collision instance is added to be watched more closely
        //The actual Collider GameObject ir slightly larger than all of the limits a UAV has. Only if another UAV is in close proximity, the distances are being measured.
        //This is kind of a simple optimisation attempt, that actually should be done on GPU (Broad phase collision detection)

        if (Uav.Id == otherUavCollisionHelper.Uav.Id)
        {
            //UnityEditor.EditorApplication.isPaused = true;
            return;
        }

        CollisionInstances.Add(new CollisionInstance
        {
            Uav = otherUavCollisionHelper.Uav,
            InstanceId = otherUavCollisionHelper.UniqueId,
            LastDistanceSquared = float.MaxValue,
            CollisionLevel = -1,
            OtherUavCrashRadius = otherUavCollisionHelper.Limits[3]
        });
        //triggered = true;
        TriggerCount = CollisionInstances.Count;
    }

    private void Start()
    {
        _cutoffLimit = 9.95f;
    }

    /// <summary>
    /// Calculate the limits based on current flightSpeed and aircraftDiameter, also adjusts the visualisation scale
    /// </summary>
    public void UpdateLimits()
    {
        Limits[0] = Uav.FlightSpeed * 10f; //Awareness
        Limits[1] = Uav.FlightSpeed * 4f; //Reaction
        Limits[2] = Uav.FlightSpeed * 1f; //Imminent crash
        Limits[3] = Uav.AircraftDiameter / 2; //Guaranteed crash
        CrashRadius = Uav.AircraftDiameter / 2;
        //Scales the whole visualisation GameObject
        Uav.Colliders[0].transform.localScale = new Vector3(Limits[0] * 2, 5, Limits[0] * 2);
        Uav.Colliders[1].transform.localScale = new Vector3(Limits[1] * 2, 5, Limits[1] * 2);
        GPUSphereCollisionSystem.instance.UpdateSphereRadius(sphereIndex, Limits[0]);
        //limits squared are used for faster comparisons
        for (var i = 0; i < 4; i++)
        {
            limitsSquared[i] = Limits[i] * Limits[i]; 
        }
    }


    //This is used so that the distance measurements are always within tolerances no matter the simulation Time scale
    private void FixedUpdate()
    {
        if (!InsideCollisionZone)
        {
            //return;
        }

        //CollisionInstances = CollisionInstances.DistinctBy(x => x.collider != null).ToList();

        CollisionCounts = new int[4];

        foreach (var collisionInstance in CollisionInstances)
        {

            if (!collisionInstance.Uav) //Checks whether this collisionHelper's UAV object still exists
            {
                continue;
            }

            //Squared distance comparison is faster at scale
            var sqrDistance = (this.transform.position - collisionInstance.Uav.transform.position).sqrMagnitude;
            var distance = Math.Sqrt(sqrDistance);
            //Debug.Log($"fixed {sqrDistance} {limitsSquared[0]} {limitsSquared[1]} {limitsSquared[2]} {limitsSquared[3]} {collisionInstance.CollisionLevel}");
            var crashDistance = Limits[3] + collisionInstance.OtherUavCrashRadius;
            var crashDistanceSquared = crashDistance * crashDistance;
            if (distance <= collisionInstance.LastDistance)
            {
                //Debug.Log("Distance smaller!");
                //Awareness distance
                if (distance <= Limits[0] && collisionInstance.CollisionLevel < 0 && MathF.Abs(this.transform.position.y - collisionInstance.Uav.transform.position.y) < _cutoffLimit)
                {
                    if (TestScheduler.Instance.CollTesting)
                    {
                        //Debug.Log($"{this.uniqueID} far collision");
                    }
                    collisionInstance.CollisionLevel = 0;
                    collisionInstance.Level[0] = true;
                    Uav.OnCollisionStart(0, collisionInstance.Uav, Uav.CurrentPosition.Position, crashDistance, (float)distance, collisionInstance.InstanceId);
                    //continue;
                }

                //Reaction distance
                if (distance <= Limits[1] && collisionInstance.CollisionLevel < 1 && MathF.Abs(this.transform.position.y - collisionInstance.Uav.transform.position.y) < _cutoffLimit)
                {
                    if (TestScheduler.Instance.CollTesting)
                    {
                        //Debug.Log($"{this.uniqueID} med collision");
                    }
                    //Debug.Log("Actually got here");
                    collisionInstance.CollisionLevel = 1;
                    collisionInstance.Level[1] = true;
                    Uav.OnCollisionStart(1, collisionInstance.Uav, Uav.CurrentPosition.Position, crashDistance, (float)distance, collisionInstance.InstanceId);
                    //continue;
                }

                //Imminent crash distance
                if (distance <= Limits[2] && collisionInstance.CollisionLevel < 2 && MathF.Abs(this.transform.position.y - collisionInstance.Uav.transform.position.y) < _cutoffLimit)
                {
                    if (TestScheduler.Instance.CollTesting)
                    {
                        //Debug.Log($"{this.uniqueID} close collision");
                    }
                    collisionInstance.CollisionLevel = 2;
                    collisionInstance.Level[2] = true;
                    Uav.OnCollisionStart(2, collisionInstance.Uav, Uav.CurrentPosition.Position, crashDistance, (float)distance, collisionInstance.InstanceId);
                    //continue;
                }

                //Guaranteed crash distance
                if (distance <= crashDistance && collisionInstance.CollisionLevel < 3)
                {
                    if (TestScheduler.Instance.CollTesting)
                    {
                        //Debug.Log($"{this.uniqueID} spot on collision");
                    }
                    collisionInstance.CollisionLevel = 3;
                    collisionInstance.Level[3] = true;
                    Uav.OnCollisionStart(3, collisionInstance.Uav, Uav.CurrentPosition.Position, crashDistance, (float)distance, collisionInstance.InstanceId);
                    Debug.Log($"{Uav.Id} Spot on collision with {collisionInstance.Uav.Id} {this.transform.position} {collisionInstance.Uav.transform.position} {crashDistance} {(float)distance}");
                    //continue;
                }
            }
            else
            {
                //Debug.Log("Distance bigger!");
                if (distance > crashDistance && collisionInstance.CollisionLevel >= 3)
                {
                    if(TestScheduler.Instance.CollTesting)
                    {
                        //Debug.Log($"{this.uniqueID} spot on collision END");
                    }
                    collisionInstance.CollisionLevel = 2;
                    collisionInstance.Level[3] = false;
                    Uav.OnCollisionEnd(3);
                    //continue;
                }

                if (distance > Limits[2] && collisionInstance.CollisionLevel >= 2)
                {
                    if (TestScheduler.Instance.CollTesting)
                    {
                        //Debug.Log($"{this.uniqueID} close collision END");
                    }
                    collisionInstance.CollisionLevel = 1;
                    collisionInstance.Level[2] = false;
                    Uav.OnCollisionEnd(2);
                    //continue;
                }

                if (distance > Limits[1] && collisionInstance.CollisionLevel >= 1)
                {
                    if (TestScheduler.Instance.CollTesting)
                    {
                        //Debug.Log($"{this.uniqueID} med collision END");
                    }
                    collisionInstance.CollisionLevel = 0;
                    collisionInstance.Level[1] = false;
                    Uav.OnCollisionEnd(1);
                    //continue;
                }

                if (distance > Limits[0] && collisionInstance.CollisionLevel >= 0)
                {
                    if (TestScheduler.Instance.CollTesting)
                    {
                        //Debug.Log($"{this.uniqueID} far collision END");
                    }
                    collisionInstance.CollisionLevel = -1;
                    collisionInstance.Level[0] = false;
                    Uav.OnCollisionEnd(0);
                    //continue;
                }
            }

            //Collect the metrics
            for (var i = 0; i <= collisionInstance.CollisionLevel; i++)
            {
                CollisionCounts[i]++;
            }

            collisionInstance.LastDistance = (float)distance;
        }

        for (var i = 0; i < 4; i++)
        {
            if (CollisionCounts[i] > MaxCollisionCounts[i])
            {
                MaxCollisionCounts[i] = CollisionCounts[i];
            }
        }

        ////GPU collision detection
        List<int> collisions = GPUSphereCollisionSystem.instance.GetCollisionsForSphere(sphereIndex);
        foreach (int otherSphereIndex in collisions)
        {
            //
        }
    }

    private void OnDestroy()
    {
        //Debug.Log("Destroyed");
        //GPUSphereCollisionSystem.instance.UnsubscribeFromCollisionEnter(sphereIndex, HandleGPUCollision);
        GPUSphereCollisionSystem.instance.RemoveSphere(this.transform);
    }
}
