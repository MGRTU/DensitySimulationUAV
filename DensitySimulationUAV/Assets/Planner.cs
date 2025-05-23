using System;
using System.Collections.Generic;
using System.Linq;
using Assets.Helpers;
using UnityEngine;

public class Planner : MonoBehaviour
{
    public static Planner Instance;
    public float FlightHeight = 100;
    public int FixedCount;
    public float TotalTime = 0.0001f;
    public float FixedUpdatesPerSecond = 0;
    public GameObject NoFlyZone; //A prefab of a nofly zone

    public Queue<UAV> PlanQueue; //Quue of UAVs that need to be responded to
    private UAV uav; //Used as a placeholder for current UAV of interest

    private void Start()
    {
        PlanQueue = new Queue<UAV>();
        Instance = this;
        FixedCount = 0;
        TotalTime = 0.0001f;
        FixedUpdatesPerSecond = 0;
    }

    public void UavFlightRequest(UAV requestUAV)
    {
        PlanQueue.Enqueue(requestUAV);
    }


    public void UpdateNoFlyZones()
    {
        foreach (var noFlyZone in TestScheduler.Instance.NoFlyZones)
        {
            var tmpZone = Instantiate(NoFlyZone, new Vector3(noFlyZone.x, 0f, noFlyZone.z), Quaternion.identity);
            tmpZone.transform.parent = this.transform;
            tmpZone.transform.localScale = new Vector3(noFlyZone.y, 300f,  noFlyZone.y);
        }
    }

    private void RespondRequest()
    {
        uav = null;
        if (PlanQueue.Count > 0)
        {
            uav = PlanQueue.Dequeue();
        }
        if (uav == null)
        {
            return;
        }

        FlightHeight = uav.MaxFlightHeight;
        //flight_height = 40 + flight_height * 20;
        //@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@
        //flight_height = 100f;

        var distance = Vector3.Distance(uav.CurrentPosition.Position, uav.TargetPosition.Position);
        if (distance > uav.FlightSpeed * uav.MaxFlightTimeSeconds / 2)
        {
            Vector3 tmpVector3 = uav.TargetPosition.Position;
            uav.TargetPosition.Position = Vector3.Lerp(uav.CurrentPosition.Position, uav.TargetPosition.Position,
                (uav.FlightSpeed * uav.MaxFlightTimeSeconds / 2) / distance);
            //Debug.Log($"Distance overshoot: {tmpVector3} {uav.targetPosition.position} {tmpVector3.y/uav.targetPosition.position.y}");
        }


        List<WayPoint> path = new List<WayPoint>
        {
            uav.CurrentPosition.Copy().AdjustSpeed(uav.FlightSpeed),
            uav.CurrentPosition.Copy().PushToHeight(FlightHeight, keepType:false).AdjustSpeed(uav.FlightSpeed),

            //TODO Flight trajectory generation algorithm should add more Waypoints to this list

            uav.TargetPosition.Copy().PushToHeight(FlightHeight, keepType:false).AdjustSpeed(uav.FlightSpeed),
            uav.TargetPosition.PushToHeight(0f).AdjustSpeed(uav.FlightSpeed),
        };

        if (TestScheduler.Instance.EnableNoFlyZones)
        {
            path = CheckPathForHazards(path);
        }
        
        uav.ApproveFlight(path);
    }
    public static bool ClosestPointsOnTwoLines(out Vector3 closestPointLine1, out Vector3 closestPointLine2, Vector3 linePoint1, Vector3 lineVec1, Vector3 linePoint2, Vector3 lineVec2)
    {
        closestPointLine1 = Vector3.zero;
        closestPointLine2 = Vector3.zero;

        float a = Vector3.Dot(lineVec1, lineVec1);
        float b = Vector3.Dot(lineVec1, lineVec2);
        float e = Vector3.Dot(lineVec2, lineVec2);

        float d = a * e - b * b;

        //Debug.Log(d);

        //lines are not parallel
        if (d != 0.0f)
        {

            Vector3 r = linePoint1 - linePoint2;
            float c = Vector3.Dot(lineVec1, r);
            float f = Vector3.Dot(lineVec2, r);

            float s = (b * f - c * e) / d;
            float t = (a * f - c * b) / d;

            closestPointLine1 = linePoint1 + lineVec1 * s;
            closestPointLine2 = linePoint2 + lineVec2 * t;

            return true;
        }

        else
        {
            return false;
        }
    }

    public static float TimeTillClosestPoint(Vector3 startingPoint1, Vector3 startingPoint2, Vector3 direction1, float speed1, Vector3 direction2, float speed2, float maxSeconds, float precision)
    {
        float minSeconds = 0f;
        float _maxSeconds = maxSeconds;
        float distance1 = Vector3.SqrMagnitude((startingPoint2 + direction2 * speed2 * minSeconds) - (startingPoint1 + direction1 * speed1 * minSeconds));
        float distance2 = Vector3.SqrMagnitude((startingPoint2 + direction2 * speed2 * maxSeconds) - (startingPoint1 + direction1 * speed1 * maxSeconds));
        float precisionSqared = precision * precision;
        float lastSeconds = maxSeconds - minSeconds + 1000f;
        float currentSeconds = maxSeconds - minSeconds;

        //Debug.Log($"{maxSeconds - minSeconds} {distance2} {distance1}");

        while (Math.Abs(lastSeconds - currentSeconds) > precision)
        {
            if (distance2 > distance1)
            {
                maxSeconds = (maxSeconds + minSeconds) / 2;
            }
            else //distance2 < distance1
            {
                minSeconds = (maxSeconds + minSeconds) / 2;
            }

            distance1 = Vector3.SqrMagnitude((startingPoint2 + direction2 * speed2 * minSeconds) - (startingPoint1 + direction1 * speed1 * minSeconds));
            distance2 = Vector3.SqrMagnitude((startingPoint2 + direction2 * speed2 * maxSeconds) - (startingPoint1 + direction1 * speed1 * maxSeconds));
            lastSeconds = currentSeconds;
            currentSeconds = maxSeconds - minSeconds;
            //Debug.Log($"{maxSeconds - minSeconds} {distance2} {distance1}");
        }

        return (minSeconds + maxSeconds) / 2;
    }

    public float TimeTillPointBelowThreshold(Vector3 startingPoint1, Vector3 startingPoint2, Vector3 direction1, float speed1, Vector3 direction2, float speed2, float maxSeconds, float precision, float closenessLimit)
    {
        float minSeconds = 0f;
        float _maxSeconds = maxSeconds;
        float closenessLimitSquared = closenessLimit * closenessLimit;
        //distance 1 gets replaced by a closeness limit;
        float distance = Vector3.SqrMagnitude((startingPoint2 + direction2 * speed2 * minSeconds) - (startingPoint1 + direction1 * speed1 * minSeconds));
        float lastDistance = distance + 1f;

        while (distance > closenessLimitSquared)
        {
            minSeconds += 0.1f;
            distance = Vector3.SqrMagnitude((startingPoint2 + direction2 * speed2 * minSeconds) - (startingPoint1 + direction1 * speed1 * minSeconds));
            if (distance > lastDistance)
            {
                break;
            }
            lastDistance = distance;
        }

        return minSeconds;
    }

    public static bool AreLinesParallel(Vector3 dir1, Vector3 dir2)
    {
        // Normalize the direction vectors
        dir1.Normalize();
        dir2.Normalize();

        // Calculate the cross product of the direction vectors
        Vector3 crossProduct = Vector3.Cross(dir1, dir2);

        // Define a small tolerance
        float tolerance = 0.001f;

        // Check if the cross product's magnitude is close to zero
        return crossProduct.sqrMagnitude < tolerance;
    }

    public static float DistancePointToLine(Vector3 point, Vector3 linePoint, Vector3 lineDirection)
    {
        // Vector from linePoint to the point
        Vector3 pointToLinePoint = point - linePoint;

        // Projection of pointToLinePoint onto the line direction vector
        float t = Vector3.Dot(pointToLinePoint, lineDirection.normalized);

        // Projection vector
        Vector3 projection = t * lineDirection.normalized;

        // Perpendicular vector from the point to the line
        Vector3 perpendicularVector = pointToLinePoint - projection;

        // Distance is the magnitude of the perpendicular vector
        float distance = perpendicularVector.magnitude;

        return distance;
    }


    List<WayPoint> CheckPathForHazards(List<WayPoint> path)
    {
        Vector3 startpos = path[1].Position;
        Vector3 destpos = path[^2].Position;
        List<WayPoint> outputpath = new List<WayPoint>();
        //outputpath.Add(start);
        Vector3 directionNormalized = (destpos - startpos).normalized;
        RaycastHit[] hits;
        hits = Physics.SphereCastAll(startpos, 50f, directionNormalized, Vector3.Distance(destpos, startpos),
            LayerMask.GetMask("NoFlyZone"), QueryTriggerInteraction.Collide);
        //Debug.DrawLine(start, end, Color.red, 300f);
        //Debug.DrawRay(start, directionNormalized* Vector3.Distance(destpos, startpos),Color.red, 300f);
        hits = hits.OrderBy(raycastHit => raycastHit.distance).ToArray();
        if (hits.Length < 1)
        {
            return path;
        }

        for (int j = 0; j < hits.Length; j++)
        {
            var hit = hits[j];
            if (hit.distance < 1f)
            {
                continue;
            }
            Vector3 hitpoint = hit.point;
            Vector3 hitpointWithOffset = hitpoint - (60f * directionNormalized);
            Vector3 circleCenter = hit.transform.position;
            float circleRadius = hit.transform.localScale.x / 2;
            float angle = 30f;

            Vector3[] circlepoints = GetAllPointsAroundCircle(circleCenter, circleRadius + 60f, startpos.y, Mathf.CeilToInt(circleRadius / 20));

            bool hasRightBeen = false;
            bool hasLeftBeen = false;
            bool hasRightChanged = false;
            bool hasLeftChanged = false;
            int newRightSpot = 0;
            int newLeftSpot = 0;
            var lastSuperValue = Vector3.Cross(directionNormalized, (circlepoints[0] - startpos).normalized);

            List<Vector3> rightSide = new List<Vector3>();
            List<Vector3> leftSide = new List<Vector3>();


            for (int i = 0; i < circlepoints.Length; i++)
            {
                var superValue = Vector3.Cross(directionNormalized, (circlepoints[i] - startpos).normalized);
                //Debug.Log(supervalue);
                if (superValue.y > 0)
                {
                    if (lastSuperValue.y < 0)
                    {
                        if (hasRightBeen)
                        {
                            hasRightChanged = true;
                        }
                    }

                    if (hasRightChanged)
                    {
                        rightSide.Insert(newRightSpot, circlepoints[i]);
                        newRightSpot++;
                        //hasRightBeen = true;
                    }
                    else
                    {
                        rightSide.Add(circlepoints[i]);
                        hasRightBeen = true;
                    }

                    //Debug.DrawLine(circlepoints[i-1], circlepoints[i], Color.red, 300f);
                }
                else
                {
                    if (lastSuperValue.y >= 0)
                    {
                        if (hasLeftBeen)
                        {
                            hasLeftChanged = true;
                        }
                    }
                    if (hasLeftChanged)
                    {
                        leftSide.Insert(newLeftSpot, circlepoints[i]);
                        newLeftSpot++;
                        //hasLeftBeen = true;
                    }
                    else
                    {
                        leftSide.Add(circlepoints[i]);
                        hasLeftBeen = true;
                    }
                }

                lastSuperValue = superValue;
            }
             
            if (rightSide.Count <= leftSide.Count) //Change this to change behaviour || true
            {
                outputpath.Add(new WayPoint(NearestPointOnLine(startpos, directionNormalized, rightSide[0]) - 100f * directionNormalized, path[^1].SpeedToWayPoint, WayPoint.WayPointType.Air));
                outputpath.AddRange(WayPoint.GetWayPointArray(rightSide.ToArray(), path[^1].SpeedToWayPoint, WayPoint.WayPointType.NoFlyEvasion));
                outputpath.Add(new WayPoint(NearestPointOnLine(startpos, directionNormalized, rightSide[rightSide.Count - 1]) + 100f * directionNormalized, path[^1].SpeedToWayPoint, WayPoint.WayPointType.NoFlyEvasion));
            }
            else
            {
                leftSide.Reverse();
                outputpath.Add(new WayPoint(NearestPointOnLine(startpos, directionNormalized, leftSide[0]) -
                                            100f * directionNormalized, path[^1].SpeedToWayPoint, WayPoint.WayPointType.Air));
                outputpath.AddRange(WayPoint.GetWayPointArray(leftSide.ToArray(), path[^1].SpeedToWayPoint, WayPoint.WayPointType.NoFlyEvasion));
                outputpath.Add(new WayPoint(NearestPointOnLine(startpos, directionNormalized, leftSide[leftSide.Count - 1]) +
                                            100f * directionNormalized, path[^1].SpeedToWayPoint, WayPoint.WayPointType.NoFlyEvasion));
            }
        }

        path.InsertRange(2, outputpath);

        return path;
        //Debug.Log(hit.point);
    }

    public Vector3 NearestPointOnLine(Vector3 linePnt, Vector3 lineDir, Vector3 pnt)
    {
        lineDir.Normalize();//this needs to be a unit vector
        var v = pnt - linePnt;
        var d = Vector3.Dot(v, lineDir);
        return linePnt + lineDir * d;
    }

    public Vector3[] GetAllPointsAroundCircle(Vector3 center, float radius, float height, int count)
    {
        List<Vector3> output = new List<Vector3>();
        float angle = 360f;
        float iteration = 360f / count;
        for (int i = 0; i < count; i++)
        {
            output.Add(new Vector3(center.x + radius * Mathf.Sin(Mathf.Deg2Rad * angle), height, center.z + radius * (float)Math.Cos(Mathf.Deg2Rad * angle)));
            angle -= iteration;
        }

        return output.ToArray();
    }

    public void FixedUpdate()
    {
        FixedCount++;
    }
    public void Update()
    {
        TotalTime += Time.deltaTime;
        FixedUpdatesPerSecond = FixedCount / TotalTime;
        for (var i = 0; i < PlanQueue.Count; i++)
        {
            RespondRequest();
        }
    }
}
