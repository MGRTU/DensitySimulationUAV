using UnityEngine;

/// <summary>
/// Class that represents the area where collision detection is necessary between UAVs. Also keeps statistics about total collision count
/// </summary>
public class CollisionControl : MonoBehaviour
{

    public static CollisionControl Instance;
    public int[] CollisionsCount = new int[4];
    public int[] LastCollisionCount = new int[4];
    public Vector3 CollisionArea;
    public int FlightCount; //total (not concurrent) flight count

    private void Start()
    {
        Instance = this;
        //InvokeRepeating("PrintLastCollisionControlStatistics",1f,1f);
        //Invoke("PrintCollisionControlStatistics", 3600f);
    }

    //Upon collision enter with a UAVCollisionHelper sets the flag that it is inside the collision zone to ture
    private void OnTriggerEnter(Collider other)
    {
        //Debug.Log("OnEnter");
        if (other.gameObject.layer > 5)
        {
            other.gameObject.GetComponent<UAVCollisionHelper>().InsideCollisionZone = true;
        }
        FlightCount++;
    }

    //Upon collision exit with a UAVCollisionHelper sets the flag that it is inside the collision zone to false
    private void OnTriggerExit(Collider other)
    {
        //Debug.Log("OnExit");
        if (other.gameObject.layer > 5)
        {
            other.gameObject.GetComponent<UAVCollisionHelper>().InsideCollisionZone = false;
        }
    }

    /// <summary>
    /// Just prints results after a time window
    /// </summary>
    /// <param name="resultTimeSeconds"></param>
    public void StartCollisionDetection(float resultTimeSeconds)
    {
        //TODO Remove this useless code
        Invoke(nameof(PrintCollisionControlStatistics), resultTimeSeconds-1f);
    }

    /// <summary>
    /// Resets the Statistics part of CollisionControl
    /// </summary>
    public void ResetCollisionControlStatistics()
    {
        CancelInvoke();
        CollisionsCount = new int[4];
        LastCollisionCount = new int[4];
        FlightCount = 0;
    }

    /// <summary>
    /// Prints the CollisionControl statistics
    /// </summary>
    public void PrintCollisionControlStatistics()
    {
        Debug.Log($"Total collisions far: {CollisionsCount[0]} medium: {CollisionsCount[1]} near: {CollisionsCount[2]} spot on: {CollisionsCount[3]/2}");
        Debug.Log($"Total flight count: {FlightCount}");
        //Debug.Break();
        //TestScheduler.instance.NextTest();
    }

    /// <summary>
    /// Prints the Delta of CollisionControl statistics since last this function call or Reset
    /// </summary>
    void PrintDeltaCollisionControlStatistics()
    {
        Debug.Log($"CurrentCollisions: {CollisionsCount[3] - LastCollisionCount[3]} | {CollisionsCount[2] - LastCollisionCount[2]} | {CollisionsCount[1] - LastCollisionCount[1]} | {CollisionsCount[0] - LastCollisionCount[0]}");
        LastCollisionCount[3] = CollisionsCount[3];
        LastCollisionCount[2] = CollisionsCount[2];
        LastCollisionCount[1] = CollisionsCount[1];
        LastCollisionCount[0] = CollisionsCount[0];
    }
}
