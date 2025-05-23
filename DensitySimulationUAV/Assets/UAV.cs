using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Assets.Helpers;
using DG.Tweening;
using UnityEditor;
using UnityEngine;
using Random = UnityEngine.Random;

public class UAV : MonoBehaviour
{
    public bool ShouldDebug;

    public float MaxFlightHeight;
    public float MaxFlightSpeed;
    public float MaxFlightTimeSeconds;
    public float TargetFlightTime;
    public float AircraftDiameter;
    public float FlightSpeed;
    public float Range;
    public int EvasionId = -1000;
    public int LastEvasionId = -1000;
    public bool IsKinematic;
    public bool WaitEvasion;
    public EvasionType CurrentEvasionType;
    public ReactionType CurrentReactionType;
    public bool PositionSource;

    public readonly WayPoint CurrentPosition = new();
    public List<WayPoint> CurrentWayPointList;
    public WayPoint TargetPosition;
    
    public bool Approved = false;
    public GameObject DroneObject;
    public GameObject VisualisationObject;
    public bool Response = false;
    public int Id = 0;
    public FlightState State = FlightState.None;
    public GameObject Image;
    public UAVCollisionHelper CollisionHelper;

    public int[] CurrentCollisions;

    public Tweener CurrentTweener;
    public int CurrentPathStep;
    public float CurrentStepPercentage;
    public bool InFlight = false;
    public bool IsFlySegment = false;
    public Vector3 GBPTargetPosition;

    public Vector3 LastVector3 = new();

    //for density tracking
    public int LastGridX;
    public int LastGridY;
    public int LastGridZ;

    public Vector3 ClosestCollisionPoint;
    private Coroutine flyRoutine;

    private float spawnTime;
    public float FlightTimeElapsed;

    private float waitEvasionTimeout; 
    private float nextWaitEvasionEnd;

    private Vector3 lastposition;
    private int count;
    private int countlimit = 60;

    public GameObject[] Colliders;

    public enum EvasionType
    {
        None,
        Nasa,
        WaitAndGo,
        Dynamic
    }

    public enum ReactionType
    {
        None,
        GoBack,
        AngleMagic,
        Dynamic
    }

    public enum FlightState
    {
        None,
        Ground,
        Liftoff,
        Transit,
        Landing,
        WaitingResponse,
        SendingRequest
    }

    private void Awake()
    {
        //CurrentEvasionType = EvasionType.WaitAndGo;
        //CurrentEvasionType = EvasionType.Nasa;
        CurrentEvasionType = TestScheduler.Instance.EvasionType;
        CurrentReactionType = TestScheduler.Instance.ReactionType;
        WaitEvasion = false;
        IsKinematic = false;
        CurrentCollisions = new int[4];
        spawnTime = Time.time;
        //Debug.Log(collisions.Length);
        ClosestCollisionPoint = new Vector3(1000f, 1000f, 1000f);
        ShouldDebug = false;
        InvokeRepeating(nameof(Watchdog), 0, 1f);
    }

    public void Watchdog()
    {
        if (transform.position == lastposition)
        {
            count++;
        }
        else
        {
            count = 0;
        }
        lastposition = transform.position;
        if (count > countlimit)
        {
            Debug.Log($"Watchdog destroyed a bugged drone! {Id}");
            Destroy(this.gameObject);
        }
    }

    public void UpdateFlightPath(WayPoint[] newWayPoints)
    {
        if (IsFlySegment)
        {
            CurrentTweener.Kill();
            IsFlySegment = false;
        }
        CurrentWayPointList.Insert(CurrentPathStep + 1, CurrentPosition.Copy().SetType(WayPoint.WayPointType.CollisionEvasion));
        CurrentWayPointList.InsertRange(CurrentPathStep + 2, newWayPoints);
    }

    public void AddDelayWaypoint(float timeElapsed)
    {
        if (IsFlySegment)
        {
            CurrentTweener.Kill();
            IsFlySegment = false;
        }
        var waitingSpeed = 0.000001f;
        var tmpWayPoint = CurrentPosition.Copy().SetType(WayPoint.WayPointType.CollisionEvasion);
        tmpWayPoint.Position = Vector3.Lerp(tmpWayPoint.Position, CurrentWayPointList[CurrentPathStep + 1].Position,
            waitingSpeed * timeElapsed * -1f);
        tmpWayPoint.SpeedToWayPoint = waitingSpeed;
        CurrentWayPointList.Insert(CurrentPathStep, tmpWayPoint);
    }

    public void UpdateFlightPath(Vector3[] newPoints)
    {
        if (IsFlySegment)
        {
            CurrentTweener.Kill();
            IsFlySegment = false;
        }
        CurrentWayPointList.Insert(CurrentPathStep+1, CurrentPosition.Copy().SetType(WayPoint.WayPointType.CollisionEvasion));
        CurrentWayPointList.InsertRange(CurrentPathStep+2, WayPoint.GetWayPointArray(newPoints, FlightSpeed, WayPoint.WayPointType.CollisionEvasion));
        
    }
    private void OnDrawGizmos()
    {
        //If not flying
        if (State != FlightState.Transit) return;

        //Planned path
        Gizmos.color = Color.cyan;
        //Gizmos.DrawWireSphere(closestCollisionPoint, flightSpeed);
        for (var i = CurrentPathStep; i < CurrentWayPointList.Count - 1; i++)
        {
            Gizmos.color = CurrentWayPointList[i + 1].Type switch
            {
                WayPoint.WayPointType.CollisionEvasion => Color.cyan,
                WayPoint.WayPointType.NoFlyEvasion => Color.magenta,
                _ => Color.black
            };
            Gizmos.DrawLine(CurrentWayPointList[i].Position, CurrentWayPointList[i + 1].Position);
        }

        //The completed path in red
        Gizmos.color = Color.red;
        for (var i = 0; i < CurrentPathStep; i++)
        {
            Gizmos.DrawLine(CurrentWayPointList[i].Position, CurrentWayPointList[i + 1].Position);
        }
        Gizmos.DrawLine(CurrentWayPointList[CurrentPathStep].Position, CurrentPosition.Position);

        if (GBPTargetPosition != Vector3.zero)
        {
            Gizmos.color = Color.white;
            Gizmos.DrawLine(transform.position, GBPTargetPosition);
        }
    }

    private void SendFlightRequest()
    {
        Response = false;
        Approved = false;
        DebugPrint($"{Id}: Sending Flight request");
        Planner.Instance.UavFlightRequest(this);
    }

    /// <summary>
    /// Calculates and sets the CurrentPathStep based on FlightTimeElapsed. Adds a new WayPoint at the FlightTimeElapsed*FlightSpeed distance in the CurrentWayPointList
    /// </summary>
    private void AdjustWayPointListForReplay()
    {
        //Assume equal speed throughout the course
        var totalDistance = FlightTimeElapsed * FlightSpeed;
        var i = 0;
        //Debug.Log(flightTimeElapsed);
        //Debug.Log(id);
        //Get the current path step from distance 
        while (true)
        {
            var tmpDistance = Vector3.Distance(CurrentWayPointList[i].Position, CurrentWayPointList[i + 1].Position);
            //Debug.Log("" + totalDistance + " " + tmpDistance);
            if (totalDistance < tmpDistance)
            {
                break;
            }

            totalDistance -= tmpDistance;
            i++;
        }

        //Offset the last path segment
        var directionNormalized = (CurrentWayPointList[i + 1].Position - CurrentWayPointList[i].Position).normalized;
        var newWayPointPosition = CurrentWayPointList[i].Position + directionNormalized * totalDistance;
        this.transform.position = newWayPointPosition;
        //The actual path can not be modified so that the Gizmos visualisation looks the same
        CurrentPathStep = i;
        CurrentWayPointList.Insert(i + 1, new WayPoint(newWayPointPosition, FlightSpeed, CurrentWayPointList[i].Type));
    }

    private IEnumerator ReplayRoutine()
    {
        AdjustWayPointListForReplay();
        yield return StartCoroutine(FlyThroughWayPointList());
        State = FlightState.Ground;
        Destroy(this.gameObject);
    }

    public void StartFlyThroughWayPointsFromHistory()
    {
        StartCoroutine(ReplayRoutine());
    }

    public void NasaEvasion(UAV target)
    {
        UAV uav = this;
        Debug.Log($"{uav.Id} Should avoid something");
        float sideCoeff = 4f;
        float straightCoeff = 4f;
        Vector3 current1 = uav.CurrentPosition.Position;
        Vector3 current2 = target.CurrentPosition.Position;

        if (uav.CurrentWayPointList == null)
        {
            DebugPrint($"{uav.Id} uav.currentWayPointList == null");
            return;
        }
        if (target.CurrentWayPointList == null) //Null reference error
        {
            DebugPrint($"{uav.Id} target.currentWayPointList == null");
            return;
        }

        if (uav.CurrentWayPointList[CurrentPathStep].Type == WayPoint.WayPointType.Ground || uav.CurrentWayPointList[CurrentPathStep+1].Type == WayPoint.WayPointType.Ground)
        {
            Debug.Log("Not evading when liftoff or landing");
            return;
        }

        Vector3 directionNormalized1 = (uav.CurrentWayPointList[uav.CurrentPathStep + 1].Position - current1).normalized;
        Vector3 directionNormalized2 = (target.CurrentWayPointList[target.CurrentPathStep + 1].Position - current2).normalized;

        Vector3 closestpoint1;
        Vector3 closestpoint2;

        var parallel = !Planner.ClosestPointsOnTwoLines(out closestpoint1, out closestpoint2, current1, directionNormalized1, current2,
            directionNormalized2);

        var parallel2 = Planner.AreLinesParallel(directionNormalized1, directionNormalized2);
        var parallelExtraRange = 0f;
        if (parallel2)
        {
            DebugPrint($"{uav.Id} Parallel special case!");
            parallelExtraRange = 35f;
        }
        else
        {
            //Debug.Log($"Lines are crossing at point: {closestpoint1}");
        }

        Vector3 closestTrajectoryPoint = (closestpoint1 + closestpoint2) / 2;

        DebugPrint($"{uav.Id} Lines are crossing at point: {closestTrajectoryPoint}");

        var timeToClosestPhysicalPoint = Planner.TimeTillClosestPoint(current1, current2, directionNormalized1, uav.FlightSpeed,
            directionNormalized2, target.FlightSpeed, 60, 0.1f);

        var closestPhysicalPoint = current1 + directionNormalized1 * uav.FlightSpeed * timeToClosestPhysicalPoint;

        //var timeToClosestPoint = timeTillPointBelowThreshold(current1, current2, directionNormalized1, uav.flightSpeed,
        //    directionNormalized2, target.flightSpeed, 60, 0.1f, uav.flightSpeed*1f);

        straightCoeff = straightCoeff + timeToClosestPhysicalPoint;

        uav.ClosestCollisionPoint = current1 + timeToClosestPhysicalPoint * uav.FlightSpeed * directionNormalized1;

        DebugPrint($"{uav.Id} Time to closest point {timeToClosestPhysicalPoint}");

        float distanceToClosestTrajectoryPoint1 = Vector3.Distance(closestTrajectoryPoint, current1);
        float distanceToClosestTrajectoryPoint2 = Vector3.Distance(closestTrajectoryPoint, current2);

        float timeToClosestTrajectoryPoint1 = Vector3.Distance(closestTrajectoryPoint, current1) / uav.FlightSpeed;
        float timeToClosestTrajectoryPoint2 = Vector3.Distance(closestTrajectoryPoint, current2) / target.FlightSpeed;

        var closestPointDirectionDot1 = Vector3.Dot((closestPhysicalPoint - current1), directionNormalized1); //if < 0 means past collision point
        var closestPointDirectionDot2 = Vector3.Dot((closestPhysicalPoint - current2), directionNormalized2); //if < 0 means past collision point

        // EVASION LOGIC //////////////////////////////////////////////////////////////////

        if (closestPointDirectionDot1 < closestPointDirectionDot2 && Mathf.Abs(closestPointDirectionDot1 - closestPointDirectionDot2) > 1f)
        {
            if (TestScheduler.Instance.CollTesting)
                DebugPrint($"{uav.Id} {closestPointDirectionDot1} {closestPointDirectionDot2} More past contact position than the other, returning!");
            //return;
        }

        //////////////////////////////////////////////

        if (target.EvasionId == uav.Id && TestScheduler.Instance.ShouldNotEvadeIfOtherEvading)
        {
            if (TestScheduler.Instance.CollTesting)
                DebugPrint($"{uav.Id} Other drone already evading me!");
            return;
        }

        //////////////////////////////////////////////

        if (uav.FlightSpeed - target.FlightSpeed < -1f)
        {
            if (target.CurrentWayPointList[target.CurrentPathStep].Type != WayPoint.WayPointType.CollisionEvasion)
            {
                if (TestScheduler.Instance.CollTesting)
                    DebugPrint($"{uav.Id} Other drone evades because it is faster, returning!");
                return;
            }
        }
        else
        {
            if (timeToClosestTrajectoryPoint1 <= timeToClosestTrajectoryPoint2 && Mathf.Abs(uav.FlightSpeed - target.FlightSpeed) < 1f)
            {
                //return;
                DebugPrint($"{uav.Id} Times to closest point: {timeToClosestTrajectoryPoint1} {timeToClosestTrajectoryPoint2}");
                if (timeToClosestTrajectoryPoint1 - timeToClosestTrajectoryPoint2 < -1f)
                {
                    if (TestScheduler.Instance.CollTesting)
                        DebugPrint($"{uav.Id} {timeToClosestTrajectoryPoint1} {timeToClosestTrajectoryPoint2} Closer to the position!");
                    return;
                }
            }
        }

        //Evasion performs drone 1
        Vector3 cross = Vector3.Cross(directionNormalized1, directionNormalized2);
        float angle = Vector3.Angle(directionNormalized1, directionNormalized2);

        float turnAngle;

        //Approach angle variable lookup table
        if (angle < 5)
        {
            parallelExtraRange = 35 - (uav.FlightSpeed - target.FlightSpeed) * 2;
            if (parallelExtraRange < 0f)
            {
                parallelExtraRange = 0f;
            }
        }
        else
        {
            parallelExtraRange = 0f;
        }

        if (angle is > 45 and < 135)
        {
            turnAngle = 45;
        }
        else if (angle is > 25 and < 155)
        {
            turnAngle = 55;
        }
        else
        {
            turnAngle = 65;
        }

        Vector3 tmpDirectionLeft;
        Vector3 tmpDirectionRight;

        if (cross.y > 0)
        {
            if (TestScheduler.Instance.CollTesting)
                DebugPrint($"{uav.Id} {angle} evades, other drone comes from left");

            tmpDirectionLeft = Quaternion.Euler(0, turnAngle, 0) * directionNormalized1;
            tmpDirectionRight = Quaternion.Euler(0, -turnAngle, 0) * directionNormalized1;
        }
        else
        {
            if (TestScheduler.Instance.CollTesting)
                DebugPrint($"{uav.Id} {angle} evades, other drone comes from right");

            tmpDirectionRight = Quaternion.Euler(0, turnAngle, 0) * directionNormalized1;
            tmpDirectionLeft = Quaternion.Euler(0, -turnAngle, 0) * directionNormalized1;
        }

        if (true && distanceToClosestTrajectoryPoint1 < distanceToClosestTrajectoryPoint2)
        {
            //flip the directions
            (tmpDirectionRight, tmpDirectionLeft) = (tmpDirectionLeft, tmpDirectionRight);
        }

        Vector3[] newPoints = new Vector3[3];
        newPoints[0] = current1 + uav.FlightSpeed * sideCoeff * tmpDirectionRight;
        newPoints[1] = newPoints[0] + directionNormalized1 * uav.FlightSpeed * straightCoeff + parallelExtraRange * directionNormalized1 * uav.FlightSpeed;
        newPoints[2] = newPoints[1] + uav.FlightSpeed * sideCoeff * tmpDirectionLeft;

        var distance1 = Planner.DistancePointToLine(newPoints[1], current2, directionNormalized2);
        var distance2 = Planner.DistancePointToLine(newPoints[2], current2, directionNormalized2);



        if (distance2 < distance1 && angle is > 5 and < 90)
        {
            newPoints[2] += directionNormalized1 * (distance1 - distance2);
            Planner.ClosestPointsOnTwoLines(out newPoints[2], out _, current1, directionNormalized1, newPoints[1],
                directionNormalized2);
        }

        uav.EvasionId = target.Id;
        uav.UpdateFlightPath(newPoints);

        return;
    }

    public void WaitAndGo(UAV target)
    {
        UAV uav = this;

        //////////////////////////////////////////////

        if (target.EvasionId == uav.Id && TestScheduler.Instance.ShouldNotEvadeIfOtherEvading)
        {
            if (TestScheduler.Instance.CollTesting)
                Debug.Log($"{uav.Id} Other drone already evading me!");
            return;
        }

        if (this.EvasionId == target.Id && Time.time > nextWaitEvasionEnd)
        {
            if (TestScheduler.Instance.CollTesting)
                Debug.Log($"{uav.Id} Same UAV + FlightEvasionTimeout! {Time.time} {waitEvasionTimeout}");
            return;
        }

        //////////////////////////////////////////////

        Debug.Log($"{uav.Id} Should avoid something");
        float sideCoeff = 4f;
        float straightCoeff = 4f;
        Vector3 current1 = uav.CurrentPosition.Position;
        Vector3 current2 = target.CurrentPosition.Position;

        if (uav.CurrentWayPointList == null)
        {
            Debug.Log($"{uav.Id} uav.currentWayPointList == null");
            return;
        }
        if (target.CurrentWayPointList == null) //Null reference error
        {
            Debug.Log($"{uav.Id} target.currentWayPointList == null");
            return;
        }

        Vector3 directionNormalized1 = (uav.CurrentWayPointList[uav.CurrentPathStep + 1].Position - current1).normalized;
        Vector3 directionNormalized2 = (target.CurrentWayPointList[target.CurrentPathStep + 1].Position - current2).normalized;

        Vector3 closestpoint1;
        Vector3 closestpoint2;

        var parallel = !Planner.ClosestPointsOnTwoLines(out closestpoint1, out closestpoint2, current1, directionNormalized1, current2,
            directionNormalized2);

        var parallel2 = Planner.AreLinesParallel(directionNormalized1, directionNormalized2);
        var parallelExtraRange = 0f;
        if (parallel2)
        {
            Debug.Log($"{uav.Id} Parallel special case!");
            parallelExtraRange = 35f;
        }
        else
        {
            //Debug.Log($"Lines are crossing at point: {closestpoint1}");
        }

        Vector3 closestTrajectoryPoint = (closestpoint1 + closestpoint2) / 2;

        Debug.Log($"{uav.Id} Lines are crossing at point: {closestTrajectoryPoint}");

        var timeToClosestPhysicalPoint = Planner.TimeTillClosestPoint(current1, current2, directionNormalized1, uav.FlightSpeed,
            directionNormalized2, target.FlightSpeed, 60, 0.1f);

        var closestPhysicalPoint = current1 + directionNormalized1 * uav.FlightSpeed * timeToClosestPhysicalPoint;

        //var timeToClosestPoint = timeTillPointBelowThreshold(current1, current2, directionNormalized1, uav.flightSpeed,
        //    directionNormalized2, target.flightSpeed, 60, 0.1f, uav.flightSpeed*1f);

        straightCoeff = straightCoeff + timeToClosestPhysicalPoint;

        uav.ClosestCollisionPoint = current1 + timeToClosestPhysicalPoint * uav.FlightSpeed * directionNormalized1;

        Debug.Log($"{uav.Id} Time to closest point {timeToClosestPhysicalPoint}");

        float distanceToClosestTrajectoryPoint1 = Vector3.Distance(closestTrajectoryPoint, current1);
        float distanceToClosestTrajectoryPoint2 = Vector3.Distance(closestTrajectoryPoint, current2);

        float timeToClosestTrajectoryPoint1 = Vector3.Distance(closestTrajectoryPoint, current1) / uav.FlightSpeed;
        float timeToClosestTrajectoryPoint2 = Vector3.Distance(closestTrajectoryPoint, current2) / target.FlightSpeed;

        var closestPointDirectionDot1 = Vector3.Dot((closestPhysicalPoint - current1), directionNormalized1); //if < 0 means past collision point
        var closestPointDirectionDot2 = Vector3.Dot((closestPhysicalPoint - current2), directionNormalized2); //if < 0 means past collision point

        // EVASION LOGIC //////////////////////////////////////////////////////////////////

        

        //Evasion performs drone 1
        WayPoint[] newWayPoints = new WayPoint[1];
        newWayPoints[0] = new WayPoint(current1 + 0.1f * uav.FlightSpeed * directionNormalized1, 0.01f, WayPoint.WayPointType.CollisionEvasion);
        
        uav.EvasionId = target.Id;
        WaitEvasion = true;
        //uav.UpdateFlightPath(newWayPoints);

        return;
    }

    public void GBPEvasion(UAV target)
    {
        UAV uav = this;

        //////////////////////////////////////////////

        if (target.EvasionId == uav.Id && TestScheduler.Instance.ShouldNotEvadeIfOtherEvading)
        {
            if (TestScheduler.Instance.CollTesting)
                Debug.Log($"{uav.Id} Other drone already evading me!");
            return;
        }

        //uav.EvasionId = target.Id;

        StartCoroutine(GBPEvasionRoutine());
    }

    public void GBPReactionCrashEvasion(UAV target)

    {
        UAV uav = this;
        StartCoroutine(GBPReactionCrashEvasionRoutine());
    }

    public void HorizontalPlaneEvasion(UAV target)
    {
        UAV uav = this;
        StartCoroutine(HorizontalPlaneEvasionRoutine(target));
    }

    public IEnumerator HorizontalPlaneEvasionRoutine(UAV target)
    {
        // Configuration parameters 
        var evasionDistance = 10f * FlightSpeed;  // Greater evasion distance for more safety
        var rejoinDistance = 20f;                 // Farther rejoin distance
        var minSpeedMultiplier = 0.2f;            // Slower minimum speed for high-risk situations
        var speedTransitionRate = 0.5f;           // Faster speed transitions
        var currentSpeedMultiplier = 1.0f;        // Start at full speed
        var emergencyDeflectionAngle = 135f;      // Very sharp turn for urgent situations

        Debug.Log($"{Id} Started blind reactive evasion against UAV {target.Id}");

        if (!IsFlySegment || CurrentTweener == null)
        {
            yield break;
        }

        // Record target ID as our evasion target
        EvasionId = target.Id;

        // Store original flight altitude and path
        float fixedAltitude = transform.position.y;
        Vector3 originalPosition = transform.position;
        Vector3 originalTargetPos = Vector3.zero;

        if (CurrentPathStep + 1 < CurrentWayPointList.Count)
        {
            originalTargetPos = CurrentWayPointList[CurrentPathStep + 1].Position;
        }
        else if (CurrentWayPointList.Count > 0)
        {
            originalTargetPos = CurrentWayPointList[^1].Position;
        }
        else
        {
            originalTargetPos = transform.position + transform.forward * 10f;
        }

        // Project original target onto horizontal plane at our fixed altitude
        Vector3 projectedTargetPos = new Vector3(originalTargetPos.x, fixedAltitude, originalTargetPos.z);

        // Calculate original direction vector (in horizontal plane)
        Vector3 originalDirection = projectedTargetPos - originalPosition;
        originalDirection.y = 0; // Ensure it's perfectly horizontal

        if (originalDirection.sqrMagnitude < 0.001f)
        {
            originalDirection = transform.forward;
            originalDirection.y = 0;
            originalDirection.Normalize();
        }
        else
        {
            originalDirection.Normalize();
        }

        // Initial setup for evasion calculations
        bool inEvasionMode = true;
        float evasionStartTime = Time.time;
        float lastDistance = Vector3.Distance(transform.position, target.transform.position);
        bool hasReachedEvasionPoint = false;
        bool isReturningToPath = false;
        Vector3 evasionPoint = Vector3.zero;
        Vector3 rejoinPoint = Vector3.zero;

        // Buffer for emergency maneuvers
        bool hasTriggeredEmergencyEvasion = false;

        // Used for calculating speed adjustments
        float currentProximityRisk = 0f;

        // Main evasion loop
        while (inEvasionMode && IsFlySegment)
        {
            // Get up-to-date information
            Vector3 myPos = transform.position;
            Vector3 targetPos = target.transform.position;

            // Calculate horizontal distance to target UAV (ignoring height)
            Vector3 horizontalToTarget = new Vector3(targetPos.x - myPos.x, 0, targetPos.z - myPos.z);
            float horizontalDistance = horizontalToTarget.magnitude;

            // Check if we need an emergency maneuver
            if (horizontalDistance < CollisionHelper.Limits[2] && !hasTriggeredEmergencyEvasion && !hasReachedEvasionPoint)
            {
                hasTriggeredEmergencyEvasion = true;
                Debug.Log($"{Id} EMERGENCY: Extreme proximity detected! Initiating emergency maneuver.");

                // Calculate emergency evasion point - this resets our previous plan
                evasionPoint = Vector3.zero;
                hasReachedEvasionPoint = false;
                isReturningToPath = false;
            }

            // Exit conditions
            bool distanceIncreasing = horizontalDistance > lastDistance;
            bool targetTooFar = horizontalDistance > CollisionHelper.Limits[0] * 1.5f && distanceIncreasing;
            bool timeElapsed = Time.time - evasionStartTime > 7f && distanceIncreasing;

            if ((targetTooFar || timeElapsed) && hasReachedEvasionPoint && isReturningToPath)
            {
                currentSpeedMultiplier = Mathf.Lerp(currentSpeedMultiplier, 1.0f, speedTransitionRate * Time.deltaTime * 2f);

                if (currentSpeedMultiplier > 0.95f)
                {
                    Debug.Log($"{Id} Exiting evasion - situation resolved");

                    if (CurrentTweener != null && CurrentTweener.IsActive())
                    {
                        CurrentTweener.timeScale = 1.0f;
                        CurrentTweener.ChangeEndValue(projectedTargetPos, FlightSpeed, true);
                        CurrentTweener.SetSpeedBased(true);
                    }

                    inEvasionMode = false;
                    break;
                }
            }

            // If we haven't calculated evasion point yet or need to recalculate it
            if (evasionPoint == Vector3.zero)
            {
                // BLIND REACTIVE ALGORITHM

                // Estimate other UAV's velocity based purely on observation
                Vector3 otherVelocityDir;

                // Try to infer target velocity from tweener if available
                if (target.CurrentTweener != null && target.CurrentTweener.IsActive() &&
                    target.CurrentPathStep + 1 < target.CurrentWayPointList.Count)
                {
                    otherVelocityDir = (target.CurrentWayPointList[target.CurrentPathStep + 1].Position -
                                       targetPos).normalized;
                }
                else
                {
                    // Fallback to using forward direction
                    otherVelocityDir = target.transform.forward;
                }

                // Project to horizontal plane
                otherVelocityDir.y = 0;
                if (otherVelocityDir.sqrMagnitude > 0.001f)
                {
                    otherVelocityDir.Normalize();
                }
                else
                {
                    // Safety fallback
                    otherVelocityDir = Vector3.forward;
                }

                // Calculate vector from me to other UAV
                Vector3 toOtherUAV = horizontalToTarget.normalized;

                // Calculate relative velocity direction
                Vector3 myVelocityDir = originalDirection.normalized;
                Vector3 relativeVelocity = myVelocityDir * FlightSpeed - otherVelocityDir * target.FlightSpeed;
                relativeVelocity.y = 0; // Project to horizontal plane

                // Normalize if not too small
                if (relativeVelocity.sqrMagnitude > 0.001f)
                {
                    relativeVelocity.Normalize();
                }

                // Calculate perpendicular vectors to the line connecting the UAVs
                Vector3 perpendicularRight = Vector3.Cross(Vector3.up, toOtherUAV).normalized;
                Vector3 perpendicularLeft = -perpendicularRight;

                // Calculate pure escape vector (opposite to relative velocity)
                Vector3 escapeVector = -relativeVelocity;

                // Calculate collision risk metrics
                float distanceRisk = Mathf.Clamp01(1f - (horizontalDistance / CollisionHelper.Limits[0]));

                // Calculate angles to determine best escape direction
                float dotWithRelVel = Vector3.Dot(myVelocityDir, relativeVelocity);
                float collisionRisk = Mathf.Clamp01(distanceRisk * (0.5f + 0.5f * Mathf.Max(0, dotWithRelVel)));

                // Store for speed adjustment
                currentProximityRisk = collisionRisk;

                // Detect head-on or following situations
                bool isHeadOn = Vector3.Dot(myVelocityDir, otherVelocityDir) < -0.7f;
                bool isFollowing = Vector3.Dot(myVelocityDir, otherVelocityDir) > 0.7f;

                // Determine evasion direction without any assumptions about other UAV's behavior
                Vector3 evasionDirection;

                if (hasTriggeredEmergencyEvasion)
                {
                    // EMERGENCY: Use strongest possible evasion
                    // Determine if we should use a hard right or left turn based on perpendicular components
                    Vector3 rightEscape = perpendicularRight + (0.2f * escapeVector);
                    Vector3 leftEscape = perpendicularLeft + (0.2f * escapeVector);

                    // Project both options forward and see which gives better separation
                    Vector3 myFutureRightPos = myPos + rightEscape.normalized * evasionDistance;
                    Vector3 myFutureLeftPos = myPos + leftEscape.normalized * evasionDistance;
                    Vector3 targetFuturePos = targetPos + otherVelocityDir * target.FlightSpeed * (evasionDistance / FlightSpeed);

                    if (Vector3.Distance(myFutureRightPos, targetFuturePos) >
                        Vector3.Distance(myFutureLeftPos, targetFuturePos))
                    {
                        // Right turn gives better separation
                        evasionDirection = rightEscape.normalized;
                        Debug.Log($"{Id} EMERGENCY: Taking hard RIGHT turn for maximum separation");
                    }
                    else
                    {
                        // Left turn gives better separation
                        evasionDirection = leftEscape.normalized;
                        Debug.Log($"{Id} EMERGENCY: Taking hard LEFT turn for maximum separation");
                    }

                    // Apply a sharper turn for emergency
                    float turnAngle = emergencyDeflectionAngle;

                    // Create a rotation away from the other UAV's position
                    evasionDirection = Quaternion.AngleAxis(turnAngle, Vector3.up) * -toOtherUAV;
                }
                else if (isHeadOn)
                {
                    // Head-on approach - always turn right (maritime rules)
                    evasionDirection = perpendicularRight;
                    Debug.Log($"{Id} Head-on approach detected, applying maritime rule - turn RIGHT");
                }
                else if (isFollowing && Vector3.Distance(myPos, targetPos) < CollisionHelper.Limits[1])
                {
                    // Following situation - lateral escape plus slow down
                    // Choose the side that gives better immediate separation
                    if (Vector3.Dot(perpendicularRight, toOtherUAV) < 0)
                    {
                        evasionDirection = perpendicularRight;
                        Debug.Log($"{Id} Following scenario - lateral escape RIGHT");
                    }
                    else
                    {
                        evasionDirection = perpendicularLeft;
                        Debug.Log($"{Id} Following scenario - lateral escape LEFT");
                    }
                }
                else
                {
                    // Normal approach - project the relative velocity to determine escape direction
                    // Calculate the closest point of approach
                    Vector3 relVelNormalized = relativeVelocity.normalized;
                    float dotProduct = Vector3.Dot(toOtherUAV, relVelNormalized);

                    // If dot product is positive, we're moving toward each other
                    if (dotProduct > 0)
                    {
                        // Project escape vector to determine optimal escape direction
                        Vector3 escapePerpendicular = escapeVector - Vector3.Dot(escapeVector, toOtherUAV) * toOtherUAV;

                        if (escapePerpendicular.sqrMagnitude < 0.001f)
                        {
                            // If perpendicular component is too small, use pure perpendicular
                            // Determine which perpendicular gives better separation from projected path
                            if (Vector3.Dot(otherVelocityDir, perpendicularRight) <
                                Vector3.Dot(otherVelocityDir, perpendicularLeft))
                            {
                                evasionDirection = perpendicularRight;
                                Debug.Log($"{Id} Pure perpendicular escape - RIGHT");
                            }
                            else
                            {
                                evasionDirection = perpendicularLeft;
                                Debug.Log($"{Id} Pure perpendicular escape - LEFT");
                            }
                        }
                        else
                        {
                            // Use the perpendicular component of the escape vector
                            evasionDirection = escapePerpendicular.normalized;
                            Debug.Log($"{Id} Projected escape vector used");
                        }
                    }
                    else
                    {
                        // We're moving away, but still need to ensure separation
                        // Choose perpendicular that increases separation
                        float dotRight = Vector3.Dot(perpendicularRight, -toOtherUAV);
                        float dotLeft = Vector3.Dot(perpendicularLeft, -toOtherUAV);

                        if (dotRight > dotLeft)
                        {
                            evasionDirection = perpendicularRight;
                            Debug.Log($"{Id} Moving away but ensuring separation - RIGHT");
                        }
                        else
                        {
                            evasionDirection = perpendicularLeft;
                            Debug.Log($"{Id} Moving away but ensuring separation - LEFT");
                        }
                    }
                }

                // Scale evasion distance by risk
                float actualEvasionDistance = evasionDistance * (0.8f + 0.5f * currentProximityRisk);
                if (hasTriggeredEmergencyEvasion)
                {
                    actualEvasionDistance *= 1.5f; // Even greater distance for emergency
                }

                // Calculate the evasion point
                evasionPoint = myPos + evasionDirection * actualEvasionDistance;
                evasionPoint.y = fixedAltitude; // Maintain fixed altitude

                // Calculate rejoin point - aim for a point ahead that rejoins our original path
                Vector3 directionToOriginal = projectedTargetPos - evasionPoint;
                directionToOriginal.y = 0;

                if (directionToOriginal.sqrMagnitude > 0.001f)
                {
                    directionToOriginal.Normalize();

                    // Scale rejoin distance based on risk - higher risk means rejoin farther along
                    float actualRejoinDistance = rejoinDistance * (1.0f + currentProximityRisk);
                    rejoinPoint = evasionPoint + directionToOriginal * actualRejoinDistance;
                    rejoinPoint.y = fixedAltitude;
                }
                else
                {
                    // Fallback rejoin point
                    rejoinPoint = projectedTargetPos;
                }

                // Debug visualization
                if (TestScheduler.Instance.CollTesting)
                {
                    // Current positions and vectors
                    Debug.DrawRay(myPos, toOtherUAV * 10f, Color.red, 5f);
                    Debug.DrawRay(myPos, myVelocityDir * 10f, Color.blue, 5f);
                    Debug.DrawRay(targetPos, otherVelocityDir * 10f, Color.magenta, 5f);

                    // Evasion vectors
                    Debug.DrawRay(myPos, evasionDirection * 10f, Color.green, 5f);
                    Debug.DrawRay(myPos, escapeVector * 8f, new Color(1f, 0.5f, 0f), 5f); // Orange

                    // Perpendicular options
                    Debug.DrawRay(myPos, perpendicularRight * 5f, Color.yellow, 5f);
                    Debug.DrawRay(myPos, perpendicularLeft * 5f, Color.cyan, 5f);
                }

                // Log the evasion plan
                Debug.Log($"{Id} Evasion plan: {myPos} -> {evasionPoint} -> {rejoinPoint} -> {projectedTargetPos}");

                // Start moving to evasion point
                if (CurrentTweener != null && CurrentTweener.IsActive())
                {
                    CurrentTweener.ChangeEndValue(evasionPoint, FlightSpeed, true);
                    CurrentTweener.SetSpeedBased(true);
                }
            }

            // Check if we've reached the evasion point
            else if (!hasReachedEvasionPoint && Vector3.Distance(myPos, evasionPoint) < 3f)
            {
                hasReachedEvasionPoint = true;
                Debug.Log($"{Id} Reached evasion point, turning toward rejoin point");

                if (CurrentTweener != null && CurrentTweener.IsActive())
                {
                    CurrentTweener.ChangeEndValue(rejoinPoint, FlightSpeed, true);
                    CurrentTweener.SetSpeedBased(true);
                }
            }

            // Check if we've reached the rejoin point
            else if (hasReachedEvasionPoint && !isReturningToPath && Vector3.Distance(myPos, rejoinPoint) < 3f)
            {
                isReturningToPath = true;
                Debug.Log($"{Id} Reached rejoin point, returning to original path");

                if (CurrentTweener != null && CurrentTweener.IsActive())
                {
                    CurrentTweener.ChangeEndValue(projectedTargetPos, FlightSpeed, true);
                    CurrentTweener.SetSpeedBased(true);
                }
            }

            // Continuously adjust speed based on proximity risk
            float newProximityRisk = Mathf.Clamp01(1f - (horizontalDistance / (CollisionHelper.Limits[0] * 0.8f)));
            currentProximityRisk = Mathf.Lerp(currentProximityRisk, newProximityRisk, 0.2f); // Smooth transitions

            // Calculate target speed
            float targetSpeedMultiplier;

            if (hasTriggeredEmergencyEvasion && !hasReachedEvasionPoint)
            {
                // Emergency - use minimum speed until evasion point is reached
                targetSpeedMultiplier = minSpeedMultiplier;
            }
            else
            {
                // Normal operations - scale speed by proximity risk
                targetSpeedMultiplier = Mathf.Lerp(1.0f, minSpeedMultiplier, currentProximityRisk);
            }

            // Apply speed adjustment with smoothing
            currentSpeedMultiplier = Mathf.Lerp(currentSpeedMultiplier, targetSpeedMultiplier, speedTransitionRate * Time.deltaTime);

            if (CurrentTweener != null && CurrentTweener.IsActive())
            {
                CurrentTweener.timeScale = currentSpeedMultiplier;
            }

            // Draw debug visualization lines
            if (TestScheduler.Instance.CollTesting)
            {
                if (!hasReachedEvasionPoint)
                {
                    Debug.DrawLine(myPos, evasionPoint, Color.yellow, 0.1f);
                    Debug.DrawLine(evasionPoint, rejoinPoint, Color.cyan, 0.1f);
                    Debug.DrawLine(rejoinPoint, projectedTargetPos, Color.green, 0.1f);
                }
                else if (!isReturningToPath)
                {
                    Debug.DrawLine(myPos, rejoinPoint, Color.cyan, 0.1f);
                    Debug.DrawLine(rejoinPoint, projectedTargetPos, Color.green, 0.1f);
                }
                else
                {
                    Debug.DrawLine(myPos, projectedTargetPos, Color.green, 0.1f);
                }
            }

            // Store current distance for next frame comparison
            lastDistance = horizontalDistance;

            yield return null;
        }

        // Final cleanup
        if (CurrentTweener != null && CurrentTweener.IsActive())
        {
            CurrentTweener.timeScale = 1.0f;
            CurrentTweener.ChangeEndValue(projectedTargetPos, FlightSpeed, true);
            CurrentTweener.SetSpeedBased(true);
        }

        yield break;
    }




    public void GoBackReactionCrashEvasion(UAV target)

    {
        UAV uav = this;
        StartCoroutine(GoBackReactionCrashEvasionRoutine());
    }

    public IEnumerator GoBackReactionCrashEvasionRoutine()
    {
        var pushBackCoefficient = 20f;
        var behindTheBackCoefficient = 0f;
        var startLookaheadCoefficient = FlightSpeed;
        var maxRightBiasCoefficient = 0f; // Maximum right-side bias coefficient

        // Dynamic speed adjustment parameters
        var minSpeedMultiplier = 0.3f;  // Minimum speed is 10% of normal
        var speedTransitionSmoothing = 0.5f;  // Lower = sharper speed transitions
        var currentSpeedMultiplier = 1.0f;  // Start at full speed

        Debug.Log($"{Id} Started GoBack crash evasion with dynamic speed control");

        // First phase: Dynamic speed control based on proximity
        if (IsFlySegment && CurrentTweener != null)
        {
            bool inSpeedControlMode = true;
            float originalDuration = CurrentTweener.Duration();
            float originalDistance = Vector3.Distance(transform.position, CurrentWayPointList[CurrentPathStep + 1].Position);
            HashSet<int> monitoredUavIds = new HashSet<int>();

            // Identify UAVs to monitor
            foreach (var collisionInstance in CollisionHelper.CollisionInstances)
            {
                if (collisionInstance?.Uav != null)
                {
                    monitoredUavIds.Add(collisionInstance.Uav.Id);
                }
            }

            while (inSpeedControlMode && IsFlySegment)
            {
                bool shouldTransitionToEvasion = false;
                float closestProximityRatio = 0f;
                UAV closestUav = null;

                // Check all collision instances to determine closest UAV
                foreach (var collisionInstance in CollisionHelper.CollisionInstances)
                {
                    if (collisionInstance?.Uav == null) continue;

                    // Only monitor previously identified UAVs
                    if (!monitoredUavIds.Contains(collisionInstance.Uav.Id)) continue;

                    float distance = Vector3.Distance(transform.position, collisionInstance.Uav.transform.position);

                    // Calculate how close the UAV is compared to our reaction threshold
                    float proximityRatio = 0f;

                    // Different zones for different speed adjustments
                    if (distance <= CollisionHelper.Limits[1]/3) // Imminent crash distance - transition to evasion
                    {
                        shouldTransitionToEvasion = true;
                        closestUav = collisionInstance.Uav;
                        break;
                    }
                    else if (distance <= CollisionHelper.Limits[1]/2) // Reaction distance - significant slowdown
                    {
                        // Calculate a value between 0-1 representing how deep we are in the reaction zone
                        proximityRatio = 1f - ((distance - CollisionHelper.Limits[2]) /
                                              (CollisionHelper.Limits[1] - CollisionHelper.Limits[2]));
                    }
                    else if (distance <= CollisionHelper.Limits[1]) // Awareness distance - mild slowdown
                    {
                        // Calculate a value between 0-1 representing how deep we are in the awareness zone
                        proximityRatio = 0.5f * (1f - ((distance - CollisionHelper.Limits[1]) /
                                                      (CollisionHelper.Limits[0] - CollisionHelper.Limits[1])));
                    }

                    // Keep track of the closest UAV
                    if (proximityRatio > closestProximityRatio)
                    {
                        closestProximityRatio = proximityRatio;
                        closestUav = collisionInstance.Uav;
                    }
                }

                // Check if we need to transition to evasion
                if (shouldTransitionToEvasion)
                {
                    Debug.Log($"{Id} Proximity critical - transitioning from speed control to evasion");
                    inSpeedControlMode = false;
                    break;
                }

                // If no UAVs are in range, gradually return to normal speed
                if (closestProximityRatio <= 0)
                {
                    closestProximityRatio = 0;
                    // Gradually return to normal speed if no threats
                    currentSpeedMultiplier = Mathf.Lerp(currentSpeedMultiplier, 1.0f, speedTransitionSmoothing);

                    // If we're back to almost normal speed, exit speed control
                    if (currentSpeedMultiplier > 0.95f)
                    {
                        Debug.Log($"{Id} Situation improved, resuming normal flight speed");
                        CurrentTweener.timeScale = 1.0f;
                        yield break;  // Exit the routine entirely
                    }
                }
                else
                {
                    // Calculate target speed multiplier based on proximity (closer = slower)
                    float targetSpeedMultiplier = Mathf.Lerp(1.0f, minSpeedMultiplier, closestProximityRatio);

                    // Smooth the transition to avoid abrupt speed changes
                    currentSpeedMultiplier = Mathf.Lerp(currentSpeedMultiplier, targetSpeedMultiplier, speedTransitionSmoothing);

                    if (closestUav != null)
                    {
                        Debug.Log($"{Id} Adjusting speed to {currentSpeedMultiplier:F2}x due to UAV {closestUav.Id} proximity");
                    }
                }

                // Apply the speed adjustment to the tweener
                if (CurrentTweener != null && CurrentTweener.IsActive())
                {
                    CurrentTweener.timeScale = currentSpeedMultiplier;
                }

                yield return null;
            }
        }

        // Second phase: Evasion routine when proximity is critical
        while (true)
        {
            var shouldBreakRoutine = true;

            // Early exit checks
            if (CurrentPathStep == 0 ||
                CurrentPathStep + 1 >= CurrentWayPointList.Count ||
                CurrentWayPointList[CurrentPathStep + 1].Position.y < 1f)
            {
                break;
            }

            // Safely get next waypoint position
            Vector3 nextWaypoint = CurrentWayPointList[CurrentPathStep + 1].Position;
            if (ContainsNaN(nextWaypoint))
            {
                Debug.LogWarning($"UAV {Id}: NaN detected in next waypoint position. Using current position.");
                nextWaypoint = transform.position;
            }

            // Calculate forward direction safely
            Vector3 direction = nextWaypoint - transform.position;
            Vector3 forwardDirection;

            // Safe normalization
            if (direction.sqrMagnitude > 0.001f)
            {
                forwardDirection = direction.normalized;
            }
            else
            {
                Debug.LogWarning($"UAV {Id}: Direction vector too small for normalization. Using transform.forward.");
                forwardDirection = transform.forward;
            }

            // Calculate right direction safely
            Vector3 crossProduct = Vector3.Cross(Vector3.up, forwardDirection);
            Vector3 rightDirection;

            if (crossProduct.sqrMagnitude > 0.001f)
            {
                rightDirection = crossProduct.normalized;
            }
            else
            {
                Debug.LogWarning($"UAV {Id}: Cross product too small for normalization. Using transform.right.");
                rightDirection = transform.right;
            }

            // Set initial target position - go backward by reversing the forwardDirection
            GBPTargetPosition = transform.position - (forwardDirection * startLookaheadCoefficient);

            // Process collision instances
            foreach (var collisionInstance in CollisionHelper.CollisionInstances)
            {
                // Skip invalid instances
                if (collisionInstance?.Uav == null)
                    continue;

                // Check if within collision limits
                float distance = Vector3.Distance(collisionInstance.Uav.transform.position, transform.position);
                if (distance >= CollisionHelper.Limits[1])
                    continue;

                var tmp = collisionInstance.Uav;
                if (tmp == null || tmp.CurrentPathStep + 1 >= tmp.CurrentWayPointList.Count)
                    continue;

                // Calculate other UAV's direction vector safely
                Vector3 directionNormalized1;
                try
                {
                    Vector3 tmpDirection = tmp.transform.position - tmp.CurrentWayPointList[tmp.CurrentPathStep + 1].Position;
                    if (tmpDirection.sqrMagnitude > 0.001f)
                    {
                        directionNormalized1 = tmpDirection.normalized;
                    }
                    else
                    {
                        Debug.LogWarning($"UAV {Id}: Other UAV direction vector too small. Skipping this instance.");
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"UAV {Id}: Error calculating direction for UAV {tmp.Id}: {ex.Message}");
                    continue;
                }

                // Calculate direction to other UAV safely
                Vector3 toOtherDrone = tmp.transform.position - transform.position;
                Vector3 directionNormalized2;

                if (toOtherDrone.sqrMagnitude > 0.001f)
                {
                    directionNormalized2 = toOtherDrone.normalized;
                }
                else
                {
                    Debug.LogWarning($"UAV {Id}: Direction to other UAV is zero. Skipping this instance.");
                    continue;
                }

                // Get other UAV's heading direction safely
                Vector3 otherUavDirection;
                try
                {
                    Vector3 tmpOtherDirection = tmp.CurrentWayPointList[tmp.CurrentPathStep + 1].Position - tmp.transform.position;
                    if (tmpOtherDirection.sqrMagnitude > 0.001f)
                    {
                        otherUavDirection = tmpOtherDirection.normalized;
                    }
                    else
                    {
                        otherUavDirection = tmp.transform.forward;
                        Debug.LogWarning($"UAV {Id}: Other UAV heading vector too small. Using its transform.forward.");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"UAV {Id}: Error calculating heading for UAV {tmp.Id}: {ex.Message}");
                    otherUavDirection = tmp.transform.forward;
                }

                // Create horizontal projections with safe normalization
                Vector3 forwardHorizontal = new Vector3(forwardDirection.x, 0, forwardDirection.z);
                Vector3 toOtherHorizontal = new Vector3(toOtherDrone.x, 0, toOtherDrone.z);
                Vector3 otherDirectionHorizontal = new Vector3(otherUavDirection.x, 0, otherUavDirection.z);

                // Safe normalization for horizontal vectors
                if (forwardHorizontal.sqrMagnitude > 0.001f)
                    forwardHorizontal.Normalize();
                else
                    forwardHorizontal = new Vector3(0, 0, 1);

                if (toOtherHorizontal.sqrMagnitude > 0.001f)
                    toOtherHorizontal.Normalize();
                else
                    continue;

                if (otherDirectionHorizontal.sqrMagnitude > 0.001f)
                    otherDirectionHorizontal.Normalize();
                else
                    otherDirectionHorizontal = new Vector3(0, 0, 1);

                // Calculate angles safely
                float signedAngle;
                try
                {
                    signedAngle = Vector3.SignedAngle(forwardHorizontal, toOtherHorizontal, Vector3.up);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"UAV {Id}: Error calculating signed angle: {ex.Message}");
                    signedAngle = 0;
                }

                float trajectoryAngle;
                try
                {
                    trajectoryAngle = Vector3.Angle(forwardHorizontal, otherDirectionHorizontal);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"UAV {Id}: Error calculating trajectory angle: {ex.Message}");
                    trajectoryAngle = 0;
                }

                // Handle aligned or opposite trajectories
                bool arePathsAligned = trajectoryAngle < 20f || trajectoryAngle > 160f;

                // Calculate bias multiplier with safety check
                float rightBiasMultiplier = 0f;
                if (signedAngle > 0)
                {
                    try
                    {
                        rightBiasMultiplier = Mathf.Sin(signedAngle * Mathf.Deg2Rad);
                        // Check for invalid result
                        if (float.IsNaN(rightBiasMultiplier) || float.IsInfinity(rightBiasMultiplier))
                        {
                            rightBiasMultiplier = 0;
                            Debug.LogWarning($"UAV {Id}: Invalid bias multiplier calculated. Using 0.");
                        }
                    }
                    catch
                    {
                        Debug.LogWarning($"UAV {Id}: Error calculating bias multiplier. Using 0.");
                        rightBiasMultiplier = 0;
                    }
                }

                // Apply right-side bias with safety
                float appliedBias = maxRightBiasCoefficient * rightBiasMultiplier;

                // Handle edge cases for aligned paths
                if (arePathsAligned)
                {
                    if (Mathf.Abs(signedAngle) < 90f)
                    {
                        if (signedAngle > 0)
                            appliedBias = maxRightBiasCoefficient * 0.8f;
                        else
                            appliedBias = maxRightBiasCoefficient * 0.5f;
                    }
                }

                // Calculate each offset vector separately for safety checks
                Vector3 rightBiasOffset = rightDirection * appliedBias;
                Vector3 behindBackOffset = directionNormalized1 * behindTheBackCoefficient;

                // Ensure safe distance calculation
                distance = Mathf.Max(0.1f, distance); // Prevent division by very small numbers
                float avoidanceStrength = pushBackCoefficient / (distance / 10f);

                // Check for overflow/invalid result
                if (float.IsNaN(avoidanceStrength) || float.IsInfinity(avoidanceStrength))
                {
                    avoidanceStrength = pushBackCoefficient;
                    Debug.LogWarning($"UAV {Id}: Invalid avoidance strength calculated. Using default.");
                }

                Vector3 avoidanceOffset = -directionNormalized2 * avoidanceStrength;

                // Calculate new target position incrementally with safety checks
                Vector3 newTargetPosition = GBPTargetPosition;

                // Check and add each offset only if valid
                if (!ContainsNaN(rightBiasOffset))
                    newTargetPosition += rightBiasOffset;
                else
                    Debug.LogWarning($"UAV {Id}: NaN detected in right bias offset. Ignoring this component.");

                if (!ContainsNaN(behindBackOffset))
                    newTargetPosition += behindBackOffset;
                else
                    Debug.LogWarning($"UAV {Id}: NaN detected in behind back offset. Ignoring this component.");

                if (!ContainsNaN(avoidanceOffset))
                    newTargetPosition += avoidanceOffset;
                else
                    Debug.LogWarning($"UAV {Id}: NaN detected in avoidance offset. Ignoring this component.");

                // Final check before updating target position
                if (!ContainsNaN(newTargetPosition))
                {
                    GBPTargetPosition = newTargetPosition;
                    shouldBreakRoutine = false;
                }
                else
                {
                    Debug.LogError($"UAV {Id}: NaN detected in final target position calculation.");
                    // Keep original target position
                }
            }

            // Final NaN check before updating tweener
            if (!ContainsNaN(GBPTargetPosition))
            {
                // Restore normal timeScale before changing the target
                if (CurrentTweener != null)
                {
                    CurrentTweener.timeScale = 1.0f;
                    CurrentTweener.ChangeEndValue(GBPTargetPosition, FlightSpeed, true);
                    CurrentTweener.SetSpeedBased(true);
                }
            }
            else
            {
                Debug.LogError($"UAV {Id}: NaN detected in final GBPTargetPosition. Using safe fallback position.");
                // Fallback to a safe position
                Vector3 safePosition = transform.position - (forwardDirection * startLookaheadCoefficient);

                if (CurrentTweener != null)
                {
                    CurrentTweener.timeScale = 1.0f;
                    CurrentTweener.ChangeEndValue(safePosition, FlightSpeed, true);
                    CurrentTweener.SetSpeedBased(true);
                }
            }

            // Handle routine termination
            if (shouldBreakRoutine)
            {
                GBPTargetPosition = Vector3.zero;

                // Safely set next target position
                if (CurrentPathStep + 1 < CurrentWayPointList.Count)
                {
                    Vector3 nextWaypointPos = CurrentWayPointList[CurrentPathStep + 1].Position;
                    if (!ContainsNaN(nextWaypointPos) && CurrentTweener != null)
                    {
                        CurrentTweener.timeScale = 1.0f;
                        CurrentTweener.ChangeEndValue(nextWaypointPos, FlightSpeed, true);
                    }
                    else
                    {
                        Debug.LogError($"UAV {Id}: NaN detected in waypoint position. Using current position.");
                        if (CurrentTweener != null)
                        {
                            CurrentTweener.timeScale = 1.0f;
                            CurrentTweener.ChangeEndValue(transform.position, FlightSpeed, true);
                        }
                    }
                }
                else if (CurrentWayPointList.Count > 0) // Make sure we have at least one waypoint
                {
                    Vector3 lastWaypointPos = CurrentWayPointList[^1].Position;
                    if (!ContainsNaN(lastWaypointPos) && CurrentTweener != null)
                    {
                        CurrentTweener.timeScale = 1.0f;
                        CurrentTweener.ChangeEndValue(lastWaypointPos, FlightSpeed, true);
                    }
                    else
                    {
                        Debug.LogError($"UAV {Id}: NaN detected in last waypoint position. Using current position.");
                        if (CurrentTweener != null)
                        {
                            CurrentTweener.timeScale = 1.0f;
                            CurrentTweener.ChangeEndValue(transform.position, FlightSpeed, true);
                        }
                    }
                }

                if (CurrentTweener != null)
                {
                    CurrentTweener.SetSpeedBased(true);
                }
                break;
            }

            yield return null;
        }
    }



    public IEnumerator GBPEvasionRoutine()
    {
        var pushBackCoefficient = 5f;
        var behindTheBackCoefficient = 10f;
        var startLookaheadCoefficient = 20f + FlightSpeed;
        var maxRightBiasCoefficient = 7f; // Maximum right-side bias coefficient

        Debug.Log($"{Id} Started crashevasion routine");
        while (true)
        {
            var shouldBreakRoutine = true;

            // Early exit checks
            if (CurrentPathStep == 0 ||
                CurrentPathStep + 1 >= CurrentWayPointList.Count ||
                CurrentWayPointList[CurrentPathStep + 1].Position.y < 1f)
            {
                break;
            }

            // Safely get next waypoint position
            Vector3 nextWaypoint = CurrentWayPointList[CurrentPathStep + 1].Position;
            if (ContainsNaN(nextWaypoint))
            {
                Debug.LogError($"UAV {Id}: NaN detected in next waypoint position. Using current position.");
                nextWaypoint = transform.position;
            }
            GBPTargetPosition = nextWaypoint;

            // Calculate forward direction safely
            Vector3 direction = GBPTargetPosition - transform.position;
            Vector3 forwardDirection;

            // Safe normalization
            if (direction.sqrMagnitude > 0.001f)
            {
                forwardDirection = direction.normalized;
            }
            else
            {
                Debug.LogWarning($"UAV {Id}: Direction vector too small for normalization. Using transform.forward.");
                forwardDirection = transform.forward;
            }

            // Calculate right direction safely
            Vector3 crossProduct = Vector3.Cross(Vector3.up, forwardDirection);
            Vector3 rightDirection;

            if (crossProduct.sqrMagnitude > 0.001f)
            {
                rightDirection = crossProduct.normalized;
            }
            else
            {
                Debug.LogWarning($"UAV {Id}: Cross product too small for normalization. Using transform.right.");
                rightDirection = transform.right;
            }

            // Set initial target position with safety checks
            GBPTargetPosition = transform.position + (forwardDirection * startLookaheadCoefficient);

            // Process collision instances
            foreach (var collisionHelperCollisionInstance in CollisionHelper.CollisionInstances)
            {
                // Skip invalid instances
                if (collisionHelperCollisionInstance == null || collisionHelperCollisionInstance.Uav == null)
                    continue;

                // Check if within collision limits
                float distance = Vector3.Distance(collisionHelperCollisionInstance.Uav.transform.position, transform.position);
                if (distance >= CollisionHelper.Limits[0])
                    continue;

                var tmp = collisionHelperCollisionInstance.Uav;
                if (tmp == null || tmp.CurrentPathStep + 1 >= tmp.CurrentWayPointList.Count)
                    continue;

                // Calculate other UAV's direction vector safely
                Vector3 directionNormalized1;
                try
                {
                    Vector3 tmpDirection = tmp.transform.position - tmp.CurrentWayPointList[tmp.CurrentPathStep + 1].Position;
                    if (tmpDirection.sqrMagnitude > 0.001f)
                    {
                        directionNormalized1 = tmpDirection.normalized;
                    }
                    else
                    {
                        Debug.LogWarning($"UAV {Id}: Other UAV direction vector too small. Skipping this instance.");
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"UAV {Id}: Error calculating direction for UAV {tmp.Id}: {ex.Message}");
                    continue;
                }

                // Calculate direction to other UAV safely
                Vector3 toOtherDrone = tmp.transform.position - transform.position;
                Vector3 directionNormalized2;

                if (toOtherDrone.sqrMagnitude > 0.001f)
                {
                    directionNormalized2 = toOtherDrone.normalized;
                }
                else
                {
                    Debug.LogWarning($"UAV {Id}: Direction to other UAV is zero. Skipping this instance.");
                    continue;
                }

                // Get other UAV's heading direction safely
                Vector3 otherUavDirection;
                try
                {
                    Vector3 tmpOtherDirection = tmp.CurrentWayPointList[tmp.CurrentPathStep + 1].Position - tmp.transform.position;
                    if (tmpOtherDirection.sqrMagnitude > 0.001f)
                    {
                        otherUavDirection = tmpOtherDirection.normalized;
                    }
                    else
                    {
                        otherUavDirection = tmp.transform.forward;
                        Debug.LogWarning($"UAV {Id}: Other UAV heading vector too small. Using its transform.forward.");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"UAV {Id}: Error calculating heading for UAV {tmp.Id}: {ex.Message}");
                    otherUavDirection = tmp.transform.forward;
                }

                // Create horizontal projections with safe normalization
                Vector3 forwardHorizontal = new Vector3(forwardDirection.x, 0, forwardDirection.z);
                Vector3 toOtherHorizontal = new Vector3(toOtherDrone.x, 0, toOtherDrone.z);
                Vector3 otherDirectionHorizontal = new Vector3(otherUavDirection.x, 0, otherUavDirection.z);

                // Safe normalization for horizontal vectors
                if (forwardHorizontal.sqrMagnitude > 0.001f)
                    forwardHorizontal.Normalize();
                else
                    forwardHorizontal = new Vector3(0, 0, 1);

                if (toOtherHorizontal.sqrMagnitude > 0.001f)
                    toOtherHorizontal.Normalize();
                else
                    continue;

                if (otherDirectionHorizontal.sqrMagnitude > 0.001f)
                    otherDirectionHorizontal.Normalize();
                else
                    otherDirectionHorizontal = new Vector3(0, 0, 1);

                // Calculate angles safely
                float signedAngle;
                try
                {
                    signedAngle = Vector3.SignedAngle(forwardHorizontal, toOtherHorizontal, Vector3.up);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"UAV {Id}: Error calculating signed angle: {ex.Message}");
                    signedAngle = 0;
                }

                float trajectoryAngle;
                try
                {
                    trajectoryAngle = Vector3.Angle(forwardHorizontal, otherDirectionHorizontal);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"UAV {Id}: Error calculating trajectory angle: {ex.Message}");
                    trajectoryAngle = 0;
                }

                // Handle aligned or opposite trajectories
                bool arePathsAligned = trajectoryAngle < 20f || trajectoryAngle > 160f;

                // Calculate bias multiplier with safety check
                float rightBiasMultiplier = 0f;
                if (signedAngle > 0)
                {
                    try
                    {
                        rightBiasMultiplier = Mathf.Sin(signedAngle * Mathf.Deg2Rad);
                        // Check for invalid result
                        if (float.IsNaN(rightBiasMultiplier) || float.IsInfinity(rightBiasMultiplier))
                        {
                            rightBiasMultiplier = 0;
                            Debug.LogWarning($"UAV {Id}: Invalid bias multiplier calculated. Using 0.");
                        }
                    }
                    catch
                    {
                        Debug.LogWarning($"UAV {Id}: Error calculating bias multiplier. Using 0.");
                        rightBiasMultiplier = 0;
                    }
                }

                // Apply right-side bias with safety
                float appliedBias = maxRightBiasCoefficient * rightBiasMultiplier;

                // Handle edge cases for aligned paths
                if (arePathsAligned)
                {
                    if (Mathf.Abs(signedAngle) < 90f)
                    {
                        if (signedAngle > 0)
                            appliedBias = maxRightBiasCoefficient * 0.8f;
                        else
                            appliedBias = maxRightBiasCoefficient * 0.5f;
                    }
                }

                // Calculate each offset vector separately for safety checks
                Vector3 rightBiasOffset = rightDirection * appliedBias;
                Vector3 behindBackOffset = directionNormalized1 * behindTheBackCoefficient;

                // Ensure safe distance calculation
                distance = Mathf.Max(0.1f, distance); // Prevent division by very small numbers
                float avoidanceStrength = pushBackCoefficient / (distance / 10f);

                // Check for overflow/invalid result
                if (float.IsNaN(avoidanceStrength) || float.IsInfinity(avoidanceStrength))
                {
                    avoidanceStrength = pushBackCoefficient;
                    Debug.LogWarning($"UAV {Id}: Invalid avoidance strength calculated. Using default.");
                }

                Vector3 avoidanceOffset = -directionNormalized2 * avoidanceStrength;

                // Calculate new target position incrementally with safety checks
                Vector3 newTargetPosition = GBPTargetPosition;

                // Check and add each offset only if valid
                if (!ContainsNaN(rightBiasOffset))
                    newTargetPosition += rightBiasOffset;
                else
                    Debug.LogWarning($"UAV {Id}: NaN detected in right bias offset. Ignoring this component.");

                if (!ContainsNaN(behindBackOffset))
                    newTargetPosition += behindBackOffset;
                else
                    Debug.LogWarning($"UAV {Id}: NaN detected in behind back offset. Ignoring this component.");

                if (!ContainsNaN(avoidanceOffset))
                    newTargetPosition += avoidanceOffset;
                else
                    Debug.LogWarning($"UAV {Id}: NaN detected in avoidance offset. Ignoring this component.");

                // Final check before updating target position
                if (!ContainsNaN(newTargetPosition))
                {
                    GBPTargetPosition = newTargetPosition;
                    shouldBreakRoutine = false;
                }
                else
                {
                    Debug.LogError($"UAV {Id}: NaN detected in final target position calculation.");
                    // Keep original target position
                }
            }

            // Final NaN check before updating tweener
            if (!ContainsNaN(GBPTargetPosition))
            {
                CurrentTweener.ChangeEndValue(GBPTargetPosition, FlightSpeed, true);
                CurrentTweener.SetSpeedBased(true);
            }
            else
            {
                Debug.LogError($"UAV {Id}: NaN detected in final GBPTargetPosition. Using safe fallback position.");
                // Fallback to a safe position
                Vector3 safePosition = transform.position + (transform.forward * startLookaheadCoefficient);
                CurrentTweener.ChangeEndValue(safePosition, FlightSpeed, true);
                CurrentTweener.SetSpeedBased(true);
            }

            // Handle routine termination
            if (shouldBreakRoutine)
            {
                GBPTargetPosition = Vector3.zero;

                // Safely set next target position
                if (CurrentPathStep + 1 < CurrentWayPointList.Count)
                {
                    if (Vector3.Distance(transform.position, CurrentWayPointList[CurrentPathStep].Position) < 1f)
                    {
                        //CurrentTweener.Complete();
                    }
                    else
                    {
                        Vector3 nextWaypointPos = CurrentWayPointList[CurrentPathStep + 1].Position;
                        if (!ContainsNaN(nextWaypointPos))
                        {
                            CurrentTweener.ChangeEndValue(nextWaypointPos, FlightSpeed, true);
                        }
                        else
                        {
                            Debug.LogError($"UAV {Id}: NaN detected in waypoint position. Using current position.");
                            CurrentTweener.ChangeEndValue(transform.position, FlightSpeed, true);
                        }
                    }
                }
                else if (CurrentWayPointList.Count > 0) // Make sure we have at least one waypoint
                {
                    Vector3 lastWaypointPos = CurrentWayPointList[^1].Position;
                    if (!ContainsNaN(lastWaypointPos))
                    {
                        CurrentTweener.ChangeEndValue(lastWaypointPos, FlightSpeed, true);
                    }
                    else
                    {
                        Debug.LogError($"UAV {Id}: NaN detected in last waypoint position. Using current position.");
                        CurrentTweener.ChangeEndValue(transform.position, FlightSpeed, true);
                    }
                }
                else
                {
                    // Ensure we have a valid endpoint if waypoint list is empty
                    CurrentTweener.ChangeEndValue(transform.position, FlightSpeed, true);
                }

                CurrentTweener.SetSpeedBased(true);
                break;
            }

            yield return null;
        }
    }

    public IEnumerator GBPReactionCrashEvasionRoutine()
    {
        var pushBackCoefficient = 20f;
        var behindTheBackCoefficient = 10f;
        var startLookaheadCoefficient = 20f + FlightSpeed;
        var maxRightBiasCoefficient = 7f; // Maximum right-side bias coefficient

        Debug.Log($"{Id} Started crashevasion routine");
        while (true)
        {
            var shouldBreakRoutine = true;

            // Early exit checks
            if (CurrentPathStep == 0 ||
                CurrentPathStep + 1 >= CurrentWayPointList.Count ||
                CurrentWayPointList[CurrentPathStep + 1].Position.y < 1f)
            {
                break;
            }

            // Safely get next waypoint position
            Vector3 nextWaypoint = CurrentWayPointList[CurrentPathStep + 1].Position;
            if (ContainsNaN(nextWaypoint))
            {
                Debug.LogWarning($"UAV {Id}: NaN detected in next waypoint position. Using current position.");
                nextWaypoint = transform.position;
            }
            GBPTargetPosition = nextWaypoint;

            // Calculate forward direction safely
            Vector3 direction = GBPTargetPosition - transform.position;
            Vector3 forwardDirection;

            // Safe normalization
            if (direction.sqrMagnitude > 0.001f)
            {
                forwardDirection = direction.normalized;
            }
            else
            {
                Debug.LogWarning($"UAV {Id}: Direction vector too small for normalization. Using transform.forward.");
                forwardDirection = transform.forward;
            }

            // Calculate right direction safely
            Vector3 crossProduct = Vector3.Cross(Vector3.up, forwardDirection);
            Vector3 rightDirection;

            if (crossProduct.sqrMagnitude > 0.001f)
            {
                rightDirection = crossProduct.normalized;
            }
            else
            {
                Debug.LogWarning($"UAV {Id}: Cross product too small for normalization. Using transform.right.");
                rightDirection = transform.right;
            }

            // Set initial target position with safety checks
            GBPTargetPosition = transform.position + (forwardDirection * startLookaheadCoefficient);

            // Process collision instances
            foreach (var collisionHelperCollisionInstance in CollisionHelper.CollisionInstances)
            {
                // Skip invalid instances
                if (collisionHelperCollisionInstance == null || collisionHelperCollisionInstance.Uav == null)
                    continue;

                // Check if within collision limits
                float distance = Vector3.Distance(collisionHelperCollisionInstance.Uav.transform.position, transform.position);
                if (distance >= CollisionHelper.Limits[1])
                    continue;

                var tmp = collisionHelperCollisionInstance.Uav;
                if (tmp == null || tmp.CurrentPathStep + 1 >= tmp.CurrentWayPointList.Count)
                    continue;

                // Calculate other UAV's direction vector safely
                Vector3 directionNormalized1;
                try
                {
                    Vector3 tmpDirection = tmp.transform.position - tmp.CurrentWayPointList[tmp.CurrentPathStep + 1].Position;
                    if (tmpDirection.sqrMagnitude > 0.001f)
                    {
                        directionNormalized1 = tmpDirection.normalized;
                    }
                    else
                    {
                        Debug.LogWarning($"UAV {Id}: Other UAV direction vector too small. Skipping this instance.");
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"UAV {Id}: Error calculating direction for UAV {tmp.Id}: {ex.Message}");
                    continue;
                }

                // Calculate direction to other UAV safely
                Vector3 toOtherDrone = tmp.transform.position - transform.position;
                Vector3 directionNormalized2;

                if (toOtherDrone.sqrMagnitude > 0.001f)
                {
                    directionNormalized2 = toOtherDrone.normalized;
                }
                else
                {
                    Debug.LogWarning($"UAV {Id}: Direction to other UAV is zero. Skipping this instance.");
                    //EditorApplication.isPaused = true;
                    continue;
                }

                // Get other UAV's heading direction safely
                Vector3 otherUavDirection;
                try
                {
                    Vector3 tmpOtherDirection = tmp.CurrentWayPointList[tmp.CurrentPathStep + 1].Position - tmp.transform.position;
                    if (tmpOtherDirection.sqrMagnitude > 0.001f)
                    {
                        otherUavDirection = tmpOtherDirection.normalized;
                    }
                    else
                    {
                        otherUavDirection = tmp.transform.forward;
                        Debug.LogWarning($"UAV {Id}: Other UAV heading vector too small. Using its transform.forward.");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"UAV {Id}: Error calculating heading for UAV {tmp.Id}: {ex.Message}");
                    otherUavDirection = tmp.transform.forward;
                }

                // Create horizontal projections with safe normalization
                Vector3 forwardHorizontal = new Vector3(forwardDirection.x, 0, forwardDirection.z);
                Vector3 toOtherHorizontal = new Vector3(toOtherDrone.x, 0, toOtherDrone.z);
                Vector3 otherDirectionHorizontal = new Vector3(otherUavDirection.x, 0, otherUavDirection.z);

                // Safe normalization for horizontal vectors
                if (forwardHorizontal.sqrMagnitude > 0.001f)
                    forwardHorizontal.Normalize();
                else
                    forwardHorizontal = new Vector3(0, 0, 1);

                if (toOtherHorizontal.sqrMagnitude > 0.001f)
                    toOtherHorizontal.Normalize();
                else
                    continue;

                if (otherDirectionHorizontal.sqrMagnitude > 0.001f)
                    otherDirectionHorizontal.Normalize();
                else
                    otherDirectionHorizontal = new Vector3(0, 0, 1);

                // Calculate angles safely
                float signedAngle;
                try
                {
                    signedAngle = Vector3.SignedAngle(forwardHorizontal, toOtherHorizontal, Vector3.up);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"UAV {Id}: Error calculating signed angle: {ex.Message}");
                    signedAngle = 0;
                }

                float trajectoryAngle;
                try
                {
                    trajectoryAngle = Vector3.Angle(forwardHorizontal, otherDirectionHorizontal);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"UAV {Id}: Error calculating trajectory angle: {ex.Message}");
                    trajectoryAngle = 0;
                }

                // Handle aligned or opposite trajectories
                bool arePathsAligned = trajectoryAngle < 20f || trajectoryAngle > 160f;

                // Calculate bias multiplier with safety check
                float rightBiasMultiplier = 0f;
                if (signedAngle > 0)
                {
                    try
                    {
                        rightBiasMultiplier = Mathf.Sin(signedAngle * Mathf.Deg2Rad);
                        // Check for invalid result
                        if (float.IsNaN(rightBiasMultiplier) || float.IsInfinity(rightBiasMultiplier))
                        {
                            rightBiasMultiplier = 0;
                            Debug.LogWarning($"UAV {Id}: Invalid bias multiplier calculated. Using 0.");
                        }
                    }
                    catch
                    {
                        Debug.LogWarning($"UAV {Id}: Error calculating bias multiplier. Using 0.");
                        rightBiasMultiplier = 0;
                    }
                }

                // Apply right-side bias with safety
                float appliedBias = maxRightBiasCoefficient * rightBiasMultiplier;

                // Handle edge cases for aligned paths
                if (arePathsAligned)
                {
                    if (Mathf.Abs(signedAngle) < 90f)
                    {
                        if (signedAngle > 0)
                            appliedBias = maxRightBiasCoefficient * 0.8f;
                        else
                            appliedBias = maxRightBiasCoefficient * 0.5f;
                    }
                }

                // Calculate each offset vector separately for safety checks
                Vector3 rightBiasOffset = rightDirection * appliedBias;
                Vector3 behindBackOffset = directionNormalized1 * behindTheBackCoefficient;

                // Ensure safe distance calculation
                distance = Mathf.Max(0.1f, distance); // Prevent division by very small numbers
                float avoidanceStrength = pushBackCoefficient / (distance / 10f);

                // Check for overflow/invalid result
                if (float.IsNaN(avoidanceStrength) || float.IsInfinity(avoidanceStrength))
                {
                    avoidanceStrength = pushBackCoefficient;
                    Debug.LogWarning($"UAV {Id}: Invalid avoidance strength calculated. Using default.");
                }

                Vector3 avoidanceOffset = -directionNormalized2 * avoidanceStrength;

                // Calculate new target position incrementally with safety checks
                Vector3 newTargetPosition = GBPTargetPosition;

                // Check and add each offset only if valid
                if (!ContainsNaN(rightBiasOffset))
                    newTargetPosition += rightBiasOffset;
                else
                    Debug.LogWarning($"UAV {Id}: NaN detected in right bias offset. Ignoring this component.");

                if (!ContainsNaN(behindBackOffset))
                    newTargetPosition += behindBackOffset;
                else
                    Debug.LogWarning($"UAV {Id}: NaN detected in behind back offset. Ignoring this component.");

                if (!ContainsNaN(avoidanceOffset))
                    newTargetPosition += avoidanceOffset;
                else
                    Debug.LogWarning($"UAV {Id}: NaN detected in avoidance offset. Ignoring this component.");

                // Final check before updating target position
                if (!ContainsNaN(newTargetPosition))
                {
                    GBPTargetPosition = newTargetPosition;
                    shouldBreakRoutine = false;
                }
                else
                {
                    Debug.LogError($"UAV {Id}: NaN detected in final target position calculation.");
                    // Keep original target position
                }
            }

            // Final NaN check before updating tweener
            if (!ContainsNaN(GBPTargetPosition))
            {
                CurrentTweener.ChangeEndValue(GBPTargetPosition, FlightSpeed, true);
                CurrentTweener.SetSpeedBased(true);
            }
            else
            {
                Debug.LogError($"UAV {Id}: NaN detected in final GBPTargetPosition. Using safe fallback position.");
                // Fallback to a safe position
                Vector3 safePosition = transform.position + (transform.forward * startLookaheadCoefficient);
                CurrentTweener.ChangeEndValue(safePosition, FlightSpeed, true);
                CurrentTweener.SetSpeedBased(true);
            }

            // Handle routine termination
            if (shouldBreakRoutine)
            {
                GBPTargetPosition = Vector3.zero;

                // Safely set next target position
                if (CurrentPathStep + 1 < CurrentWayPointList.Count)
                {
                    if (Vector3.Distance(transform.position, CurrentWayPointList[CurrentPathStep].Position) < 1f)
                    {
                        //CurrentTweener.Complete();
                    }
                    else
                    {
                        Vector3 nextWaypointPos = CurrentWayPointList[CurrentPathStep + 1].Position;
                        if (!ContainsNaN(nextWaypointPos))
                        {
                            CurrentTweener.ChangeEndValue(nextWaypointPos, FlightSpeed, true);
                        }
                        else
                        {
                            Debug.LogError($"UAV {Id}: NaN detected in waypoint position. Using current position.");
                            CurrentTweener.ChangeEndValue(transform.position, FlightSpeed, true);
                        }
                    }
                }
                else if (CurrentWayPointList.Count > 0) // Make sure we have at least one waypoint
                {
                    Vector3 lastWaypointPos = CurrentWayPointList[^1].Position;
                    if (!ContainsNaN(lastWaypointPos))
                    {
                        CurrentTweener.ChangeEndValue(lastWaypointPos, FlightSpeed, true);
                    }
                    else
                    {
                        Debug.LogError($"UAV {Id}: NaN detected in last waypoint position. Using current position.");
                        CurrentTweener.ChangeEndValue(transform.position, FlightSpeed, true);
                    }
                }
                else
                {
                    // Ensure we have a valid endpoint if waypoint list is empty
                    CurrentTweener.ChangeEndValue(transform.position, FlightSpeed, true);
                }

                CurrentTweener.SetSpeedBased(true);
                break;
            }

            yield return null;
        }
    }

    // Helper method to check for NaN values in a Vector3
    private bool ContainsNaN(Vector3 vector)
    {
        return float.IsNaN(vector.x) || float.IsNaN(vector.y) || float.IsNaN(vector.z) ||
               float.IsInfinity(vector.x) || float.IsInfinity(vector.y) || float.IsInfinity(vector.z);
    }


    public void OnCollisionStart(int level, UAV otherUav, Vector3 collPosition, float crashDistanceSquared, float sqrMagnitude, int collisionId)
    {
        //if (state != FlightState.Transit) return;
        //Return if not supposed to receive collisions
        if (IsKinematic)
        {
            return;
        }
        //Return if not inside active collision zone 
        //TODO fix this
        if (CollisionHelper.InsideCollisionZone || true)
        {
            CollisionControl.Instance.CollisionsCount[level]++;
        }

        if (CurrentPathStep < 0 || CurrentPathStep > CurrentWayPointList.Count)
        {
            Debug.Log($"Pathstep index out of range error {CurrentPathStep}");
            return;
        }

        switch (level)
        {
            //Crash 
            case 3:
                CalculateCurrentCollisionTable();
                if (CollisionHelper.InsideCollisionZone)
                {
                    float headingAngle = DroneSpawner.Instance.Angle360(CurrentPosition.Position,
                        CurrentWayPointList[CurrentPathStep + 1].Position);
                    TestScheduler.Instance.LogUavCollision(CurrentPosition, CurrentWayPointList[CurrentPathStep + 1],
                        TargetPosition, headingAngle, State, CurrentCollisions, CurrentPathStep, CurrentStepPercentage,
                        FlightSpeed, AircraftDiameter, crashDistanceSquared, sqrMagnitude, CollisionHelper.UniqueId,
                        collisionId);
                }
                break;
            //Reaction
            case 1 when CurrentWayPointList != null && CurrentPathStep < CurrentWayPointList.Count:
            {
                switch (CurrentReactionType)
                {
                    //Planner.Instance.AdjustFlightPath(this, tmp);
                    case ReactionType.None:
                        break;
                    case ReactionType.GoBack:
                        GoBackReactionCrashEvasion(otherUav);
                        break;
                    case ReactionType.AngleMagic:
                        HorizontalPlaneEvasion(otherUav);
                        break;
                    case ReactionType.Dynamic:
                        GBPReactionCrashEvasion(otherUav);
                        break;
                }
                    //GBPReactionCrashEvasion(otherUav);
                //GBPEvasion(otherUav);
                break;
            }
            //Evasion
            case 0 when CurrentWayPointList != null && CurrentPathStep < CurrentWayPointList.Count && CurrentWayPointList[CurrentPathStep].Type != WayPoint.WayPointType.CollisionEvasion:
            {
                //break;
                if (!TestScheduler.Instance.CollisionEvasion) //Do not evade if evasion is disabled
                {
                    return;
                }

                if (otherUav == null)
                {
                    return;
                }

                if (TestScheduler.Instance.CollTesting)
                {
                    //Debug.Log(currentWayPointList[currentPathStep].type + " " + tmp.currentWayPointList[tmp.currentPathStep].type);
                }
                    //I need to check whether the trajectories even come close before deciding on evasion
                if (!AreTrajectoriesTooClose(this, otherUav))
                {
                    Debug.Log($"{Id} Trajectories does not come too close, not evading");
                    break;
                }
                switch (CurrentEvasionType)
                {
                        //Planner.Instance.AdjustFlightPath(this, tmp);
                    case EvasionType.None:
                        break;
                    case EvasionType.WaitAndGo:
                        WaitAndGo(otherUav);
                        break;
                    case EvasionType.Nasa:
                        NasaEvasion(otherUav);
                        break;
                    case EvasionType.Dynamic:
                        GBPEvasion(otherUav);
                        break;
                }
                
                //Planner.Instance.AdjustFlightPathStopType(this, tmp);
                break;
            }
        }
    }

    /// <summary>
    /// Checks if two UAV trajectories will come dangerously close to each other based on their current positions and directions
    /// </summary>
    /// <param name="uav1">First UAV</param>
    /// <param name="uav2">Second UAV</param>
    /// <param name="safetyBuffer">Additional safety distance beyond aircraft diameters</param>
    /// <param name="maxLookaheadTime">Maximum time to look ahead for collision detection in seconds</param>
    /// <returns>True if trajectories will come dangerously close, false otherwise</returns>
    public static bool AreTrajectoriesTooClose(UAV uav1, UAV uav2, float safetyBuffer = 5f, float maxLookaheadTime = 10f)
    {
        // If either UAV doesn't have enough waypoints, can't predict trajectory
        if (uav1.CurrentPathStep + 1 >= uav1.CurrentWayPointList.Count ||
            uav2.CurrentPathStep + 1 >= uav2.CurrentWayPointList.Count)
        {
            return false;
        }

        Vector3 pos1 = uav1.CurrentPosition.Position;
        Vector3 pos2 = uav2.CurrentPosition.Position;

        Vector3 dir1 = (uav1.CurrentWayPointList[uav1.CurrentPathStep + 1].Position - pos1).normalized;
        Vector3 dir2 = (uav2.CurrentWayPointList[uav2.CurrentPathStep + 1].Position - pos2).normalized;

        // Calculate minimum safe distance based on both aircraft diameters plus safety buffer
        float minSafeDistance = (uav1.AircraftDiameter + uav2.AircraftDiameter) / 2f + safetyBuffer;

        // Find closest approach point between trajectories
        Vector3 closestPoint1, closestPoint2;
        bool areParallel = !Planner.ClosestPointsOnTwoLines(out closestPoint1, out closestPoint2,
            pos1, dir1, pos2, dir2);

        // If lines are parallel, check current distance and projected distances
        if (areParallel)
        {
            // Check current distance
            float currentDistance = Vector3.Distance(pos1, pos2);
            if (currentDistance < minSafeDistance)
                return true;

            // For parallel trajectories, check distance between lines
            float distanceBetweenLines = Planner.DistancePointToLine(pos1, pos2, dir2);
            return distanceBetweenLines < minSafeDistance;
        }

        // Calculate time to reach closest points
        float timeToClosestPoint = Planner.TimeTillClosestPoint(
            pos1, pos2, dir1, uav1.FlightSpeed, dir2, uav2.FlightSpeed,
            maxLookaheadTime, 0.1f);

        // If time is negative or beyond our lookahead window, no immediate threat
        if (timeToClosestPoint < 0 || timeToClosestPoint > maxLookaheadTime)
            return false;

        // Calculate positions at closest approach
        Vector3 futurePos1 = pos1 + dir1 * uav1.FlightSpeed * timeToClosestPoint;
        Vector3 futurePos2 = pos2 + dir2 * uav2.FlightSpeed * timeToClosestPoint;

        // Check if predicted positions are too close
        float predictedDistance = Vector3.Distance(futurePos1, futurePos2);
        return predictedDistance < minSafeDistance;
    }


    public void OnCollisionEnd(int level)
    {
        
    }

    public void ApproveFlight(List<WayPoint> path)
    {
        Response = true;
        Approved = true;
        if (path == null)
        {
            Console.Error.WriteLine("Path = null :(");
        }
        CurrentWayPointList = path;
    }

    private IEnumerator FlyThroughWayPointList()
    {
        if (FlightSpeed <= 0)
        {
            Debug.LogError("FlightSpeed cant be <=0 !");
            yield break;
        }
        State = FlightState.Transit;
        while (CurrentPathStep + 1 < CurrentWayPointList.Count())
        {
            CurrentPosition.Type = CurrentWayPointList[CurrentPathStep + 1].Type;
            yield return StartCoroutine(FlyToWayPoint(CurrentWayPointList[CurrentPathStep], CurrentWayPointList[CurrentPathStep + 1]));
            CurrentPathStep++;
            if (CurrentPathStep + 1 == CurrentWayPointList.Count())
            {
                break;
            }
        }
    }

    private IEnumerator FlyToWayPoint(WayPoint startSegmentWayPoint, WayPoint endSegmentWayPoint)
    {
        CurrentTweener = DroneObject.transform.DOMove(endSegmentWayPoint.Position, endSegmentWayPoint.SpeedToWayPoint).SetSpeedBased(true).OnComplete(CompleteFlySegment).SetEase(Ease.Linear);
        IsFlySegment = true;
        float stopStartTime = 0f;
        waitEvasionTimeout = FlightSpeed + 5;
        while (IsFlySegment)
        {
            if (WaitEvasion)
            {
                if (EvasionId != LastEvasionId)
                {
                    LastEvasionId = EvasionId;
                    nextWaitEvasionEnd = Time.time + waitEvasionTimeout;
                }
                while (true)
                {
                    var shouldStopWaiting = true;
                    CurrentTweener.Pause();
                    //If EvasionId still in range then stop
                    foreach (var instance in CollisionHelper.CollisionInstances)
                    {
                        if (instance.InstanceId == EvasionId)
                        {
                            if (instance.Uav != null)
                            {
                                if (Vector3.Distance(instance.Uav.transform.position,
                                        this.transform.position) <
                                    CollisionHelper.Limits[0])
                                {
                                    shouldStopWaiting = false;
                                }
                            }
                        }
                    }

                    if (Time.time > nextWaitEvasionEnd)
                    {
                        shouldStopWaiting = true;
                    }

                    if (shouldStopWaiting)
                    {
                        WaitEvasion = false;
                        AddDelayWaypoint(stopStartTime - Time.time);
                        CurrentTweener.Play();
                    }

                    if (WaitEvasion == false)
                    {
                        break;
                    }
                    yield return null;
                }
            }
            stopStartTime = Time.time;
            CurrentStepPercentage = CurrentTweener.ElapsedPercentage();
            yield return null;
        }
    }

    /// <summary>
    /// A callback used to indicate the completion of a FlyToWayPoint. Sets "IsFlySegment" to false
    /// </summary>
    private void CompleteFlySegment()
    {
        IsFlySegment = false;
    }

    private IEnumerator WaitResponse()
    {
        State = FlightState.WaitingResponse;
        while (Response != true)
        {
            yield return null;
        }
    }

    private void DebugPrint(string message)
    {
        if (ShouldDebug)
        {
            Debug.Log(message);
        }
    }

    /// <summary>
    /// Adds statistics and tracking to the UAV. RemoveStatisticsAndTracking should be used afterwards
    /// </summary>
    private void AddStatisticsAndTracking()
    {
        TestScheduler.Instance.ActiveFlights++;
        if (DensityTracker.Instance != null)
        {
            DensityTracker.Instance.UavList.Add(this);
        }
    }

    /// <summary>
    /// Removes statistics and tracking from the UAV
    /// </summary>
    private void RemoveStatisticsAndTracking()
    {
        TestScheduler.Instance.ActiveFlights--;
        if (DensityTracker.Instance != null)
        {
            DensityTracker.Instance.UavList.Remove(this);
        }
    }

    private IEnumerator DestinationFlight()
    {
        AddStatisticsAndTracking();
        DebugPrint($"{Id}: Destination flight started");

        State = FlightState.SendingRequest;
        SendFlightRequest();
        yield return StartCoroutine(WaitResponse());

        spawnTime = Time.time;
        yield return StartCoroutine(FlyThroughWayPointList());
        State = FlightState.Ground;
        
        DebugPrint($"{Id}: Destination flight finished");
        RemoveStatisticsAndTracking();
        Destroy(this.gameObject);
    }

    /// <summary>
    /// Generates random stats for a UAV given the flight rules
    /// </summary>
    /// <returns>"true" if stats are generated successfully; "false" if failed to generate legal stats</returns>
    public bool GenerateStats()
    {
        //Use these to override flight levels
        //var flightHeights = new List<float> { 120f, 140f, 160f, 180f, 200f, 220f, 240f, 260f, 280f, 300f};
        //var flightHeights = new List<float> {40f, 60f, 80f, 100f, 120f};
        //var flightHeights = new List<float> { 40f, 60f, 80f, 100f, 120f, 140f, 160f, 180f, 200f, 220f, 240f, 260f, 280f, 300f };
        //var flightHeights = new List<float> { 120f, 300f};
        //var flightHeights = new List<float> { 120f };
        MaxFlightHeight = TestScheduler.Instance.FlightHeights[Random.Range(0, TestScheduler.Instance.FlightHeights.Length)];
        MaxFlightSpeed = Random.Range(1, 31);
        MaxFlightTimeSeconds = Random.Range(15 * 60, 60 * 60);
        TargetFlightTime = 1;
        AircraftDiameter = Random.Range(0.1f, 3f);
        FlightSpeed = Random.Range(1, 31);
        if (MaxFlightSpeed < FlightSpeed)
        {
            return false;
        }

        CollisionHelper.UniqueId = Id;
        CollisionHelper.UpdateLimits();

        if (CollisionHelper.UniqueId < 0)
        {
            Debug.LogWarning("CollisionHelper uniqueID is not set properly!");
        }

        return true;
    }

    public void AssignFlight()
    {
        CurrentPosition.Position = DroneObject.transform.position;
        if (flyRoutine != null)
            StopCoroutine(flyRoutine);
        flyRoutine = StartCoroutine(DestinationFlight());
        
    }

    /// <summary>
    /// Updates the CurrentCollisions array with current collision count data
    /// </summary>
    public void CalculateCurrentCollisionTable()
    {
        for (var i = 0; i < 4; i++)
        {
            CurrentCollisions[i] = 0;
        }

        foreach (var collisionInstance in CollisionHelper.CollisionInstances)
        {
            for (var i = 0; i < 4; i++)
            {
                if (collisionInstance.Level[i])
                {
                    CurrentCollisions[i]++;
                }
            }
        }
    }

    private void FixedUpdate()
    {
        //This is in FixedUpdate to make sure that the UAV flight data scales with time scale
        CurrentPosition.Position = DroneObject.transform.position;
        
    }

    private void OnDestroy()
    {
        //RemoveStatisticsAndTracking();
        CurrentTweener.Kill(); //Just to make sure it is killed to prevent errors
        if (TestScheduler.Instance)
        {
            if (State != FlightState.Ground)
            {
                //Cut off remaining part of the WayPointList to create a realistic path for the CSV file and distance statistics
                CurrentWayPointList = CurrentWayPointList.Take(CurrentPathStep + 1).ToList();
                CurrentWayPointList.Add(new WayPoint(transform.position, FlightSpeed, CurrentWayPointList[^1].Type));
            }

            var printStr = CurrentWayPointList.Aggregate("", (current, wayPoint) => current + (wayPoint.Serialize() + "_"));
            //Debug.Log(printStr);

            TestScheduler.Instance.LogUavFlight(
                Id,
                Time.time - spawnTime,
                CurrentWayPointList,
                spawnTime - TestScheduler.Instance.SimStartTime,
                Time.time - TestScheduler.Instance.SimStartTime,
                AircraftDiameter,
                FlightSpeed,
                CollisionHelper.MaxCollisionCounts[0],
                CollisionHelper.MaxCollisionCounts[1],
                CollisionHelper.MaxCollisionCounts[2],
                CollisionHelper.MaxCollisionCounts[3]);
        }

        Destroy(Image); //UI overlay image that is controlled by other script
    }
}
