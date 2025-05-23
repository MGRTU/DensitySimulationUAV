using System.Collections.Generic;
using System.Globalization;
using Assets.Helpers;
using UnityEngine;

/// <summary>
/// Class that represents a UAV flight that is recorded with metadata
/// </summary>
public class FlightFromHistory
{
    public int UavId;
    public float TimeFlown;
    public Vector3 StartPosition;
    public Vector3 TargetPosition;
    public float Distance;
    public float StartTime;
    public float EndTime;
    public float FlightSpeed;
    public float AircraftDiameter;
    public float Level0Max;
    public float Level1Max;
    public float Level2Max;
    public float Level3Max;
    public List<WayPoint> WayPointList;

    /// <summary>
    /// Creates a Vector3 object from a string representation of Vector3
    /// </summary>
    /// <param name="sVector">String representation of Vector3 in a format "(x,y,z)"</param>
    /// <returns>A Vector3 object</returns>
    private static Vector3 StringToVector3(string sVector)
    {
        // Remove the parentheses
        if (sVector.StartsWith("(") && sVector.EndsWith(")"))
        {
            sVector = sVector[1..^1];
        }

        // split the items
        var sArray = sVector.Split(',');

        // store as a Vector3
        var result = new Vector3(
            float.Parse(sArray[0], CultureInfo.InvariantCulture),
            float.Parse(sArray[1], CultureInfo.InvariantCulture),
            float.Parse(sArray[2], CultureInfo.InvariantCulture));

        return result;
    }

    /// <summary>
    /// Creates an empty FlightFromHistory object
    /// </summary>
    public FlightFromHistory()
    {

    }

    /// <summary>
    /// Creates a FlightFromHistory object from a split CSV line 
    /// </summary>
    /// <param name="line">split line from CSV</param>
    /// <returns></returns>
    public FlightFromHistory(IReadOnlyList<string> line)
    {
        this.UavId = int.Parse(line[0]);
        this.TimeFlown = float.Parse(line[1], CultureInfo.InvariantCulture);
        this.WayPointList = WayPoint.GetWayPointListFromPathString(line[2]);
        this.StartPosition = this.WayPointList[0].Position;
        this.TargetPosition = this.WayPointList[^1].Position;
        //this.startPosition = new Vector3(float.Parse(line[2], CultureInfo.InvariantCulture),float.Parse(line[3], CultureInfo.InvariantCulture), float.Parse(line[4], CultureInfo.InvariantCulture));
        //this.targetPosition = new Vector3(float.Parse(line[6], CultureInfo.InvariantCulture), float.Parse(line[7], CultureInfo.InvariantCulture), float.Parse(line[8], CultureInfo.InvariantCulture));
        this.Distance = float.Parse(line[3], CultureInfo.InvariantCulture);
        this.StartTime = float.Parse(line[4], CultureInfo.InvariantCulture);
        this.EndTime = float.Parse(line[5], CultureInfo.InvariantCulture);
        this.FlightSpeed = float.Parse(line[6], CultureInfo.InvariantCulture);
        this.AircraftDiameter = float.Parse(line[7], CultureInfo.InvariantCulture);
        this.Level0Max = int.Parse(line[8]);
        this.Level1Max = int.Parse(line[9]);
        this.Level2Max = int.Parse(line[10]);
        this.Level3Max = int.Parse(line[11]);
    }
}
