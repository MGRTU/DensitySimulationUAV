using System.Globalization;
using UnityEngine;


/// <summary>
/// Class that represents a collision between UAVs that is recorded with metadata
/// </summary>
public class CollisionFromHistory
{
    public int AngleStep;
    public float ActualAngle;
    public Vector3 Uav1StartPosition;
    public Vector3 Uav2StartPosition;
    public Vector3 Uav1TargetPosition;
    public Vector3 Uav2TargetPosition;
    public float Uav1FlightSpeed;
    public float Uav2FlightSpeed;
    public float Uav1MaxFlightHeight;
    public float Uav2MaxFlightHeight;
    public float AircraftDiameter1;
    public float AircraftDiameter2;
    public float ClosestDistance;
    public int CircleAnglesCount;


    /// <summary>
    /// Constructor of an empty CollisionFromHistory
    /// </summary>
    public CollisionFromHistory()
    {

    }

    /// <summary>
    /// Constructor of a CollisionFromHistory with given attributes
    /// </summary>
    /// <param name="angleStep"></param>
    /// <param name="actualAngle"></param>
    /// <param name="uav1StartPosition"></param>
    /// <param name="uav2StartPosition"></param>
    /// <param name="uav1TargetPosition"></param>
    /// <param name="uav2TargetPosition"></param>
    /// <param name="uav1FlightSpeed"></param>
    /// <param name="uav2FlightSpeed"></param>
    /// <param name="aircraftDiameter1"></param>
    /// <param name="aircraftDiameter2"></param>
    /// <param name="closestDistance"></param>
    /// <param name="circleAnglesCount"></param>
    public CollisionFromHistory(int angleStep, float actualAngle, Vector3 uav1StartPosition, Vector3 uav2StartPosition, Vector3 uav1TargetPosition, Vector3 uav2TargetPosition, float uav1FlightSpeed, float uav2FlightSpeed, float aircraftDiameter1, float aircraftDiameter2, float closestDistance, int circleAnglesCount)
    {
        this.AngleStep = angleStep;
        this.ActualAngle = actualAngle;
        this.Uav1StartPosition = uav1StartPosition;
        this.Uav2StartPosition = uav2StartPosition;
        this.Uav1TargetPosition = uav1TargetPosition;
        this.Uav2TargetPosition = uav2TargetPosition;
        this.Uav1FlightSpeed = uav1FlightSpeed;
        this.Uav2FlightSpeed = uav2FlightSpeed;
        this.AircraftDiameter1 = aircraftDiameter1;
        this.AircraftDiameter2 = aircraftDiameter2;
        this.ClosestDistance = closestDistance;
        this.CircleAnglesCount = circleAnglesCount;
    }

    /// <summary>
    /// Creates a CollisionFromHistory object from a line of CSV
    /// </summary>
    /// <param name="collString">line of a CSV file with '\t' as a default separator</param>
    /// <param name="separator">CSV separator</param>
    public CollisionFromHistory(string collString, char separator = '\t')
    {
        var stringArray = collString.Split(separator);

        this.AngleStep = int.Parse(stringArray[0]);
        this.ActualAngle = float.Parse(stringArray[1], CultureInfo.InvariantCulture);
        this.Uav1StartPosition = StringToVector3(stringArray[2]);
        this.Uav2StartPosition = StringToVector3(stringArray[3]);
        this.Uav1TargetPosition = StringToVector3(stringArray[4]);
        this.Uav2TargetPosition = StringToVector3(stringArray[5]);
        this.Uav1MaxFlightHeight = float.Parse(stringArray[6], CultureInfo.InvariantCulture);
        this.Uav2MaxFlightHeight = float.Parse(stringArray[7], CultureInfo.InvariantCulture);
        this.Uav1FlightSpeed = float.Parse(stringArray[8], CultureInfo.InvariantCulture);
        this.Uav2FlightSpeed = float.Parse(stringArray[9], CultureInfo.InvariantCulture);
        this.AircraftDiameter1 = float.Parse(stringArray[10], CultureInfo.InvariantCulture);
        this.AircraftDiameter2 = float.Parse(stringArray[11], CultureInfo.InvariantCulture);
        this.ClosestDistance = float.Parse(stringArray[12], CultureInfo.InvariantCulture);
        this.CircleAnglesCount = int.Parse(stringArray[13], CultureInfo.InvariantCulture);
    }

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
}
