using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Assets.Helpers;
using UnityEngine;
using UnityEngine.Rendering;

public class TestScheduler : MonoBehaviour
{
    public static TestScheduler Instance;
    // Start is called before the first frame update
    private List<string[]> resultsList = new List<string[]>();
    private List<string[]> collisionsList = new List<string[]>();
    public List<string[]> FlightsList = new List<string[]>();
    private List<string[]> collisionTestingList = new List<string[]>();
    public Mesh CylinderMesh;
    public int ActiveFlights = 0;
    public float TimeScale;
    public int RangeKm2 = 0;
    public int CollisionRangeKm2 = 0;
    public int StartDensity;
    public int CurrentDensity = 0;
    public int EndDensity;
    public int Step;
    public float TimeFrameMinutes;
    private float rangeInMeters = 0;
    private float collisionRangeInMeters = 0;
    public string FolderName;
    public string FileName;
    public string FileNameCollisions;
    public string FileNameFlights;
    public string FileNameCollisionTests;
    public float MaxTimeScale;
    public bool EnableNoFlyZones;
    public bool AngleHeight;
    public bool OneFlightLevel;
    public bool CollisionEvasion;
    public bool ShouldNotEvadeIfOtherEvading;
    public UAV.EvasionType EvasionType;
    public UAV.ReactionType ReactionType;
    public bool CollTesting;
    public float FlightLevelHeight;
    public float[] FlightHeights;
    public bool MultipleTests;
    public float IntervalTime;
    public GameObject CanvasGameObject;
    public SimulationOptionsArray Options;
    public SimulationProgress Progress;
    public int DropdownOption1;
    public int DropdownOption2;
    public string CollisionInstanceReplayString;
    public string FlightsReplayFolderName;
    public int FlightsReplayStartSeconds;

    public float SimStartTime; //The Time.time of the start of the simulation
    bool isRunning = false;

    public SpawnMode TestMode;
    public float PolygonBias;
    public Vector3[] Polygons;
    public Vector3[] NoFlyZones;

    void Start()
    {
        Instance = this;

        Application.targetFrameRate = 60;         //Dont overuse resoures to push above 60fps
        Application.runInBackground = true;       //Allow running when minimised
        Graphics.activeTier = GraphicsTier.Tier1; //set graphics to low
        
        var reader = new StreamReader("Simulations.json");
        var reader2 = new StreamReader("progress.json");

        var readJson = reader.ReadToEnd();
        var readJson2 = reader2.ReadToEnd();

        reader.Close();
        reader2.Close();

        Options = JsonUtility.FromJson<SimulationOptionsArray>(readJson);
        Progress = JsonUtility.FromJson<SimulationProgress>(readJson2);

        if (Progress.SimStatus is not SimulationProgress.Status.InProgress)
        {
            Progress = new SimulationProgress();
            WriteProgressToJson();
        }

        var objectStr = JsonUtility.ToJson(Options, true);
        //Debug.Log(objectStr);
    }
    

    public void SetupSimulationResults()
    {
        if (Progress.SimStatus == SimulationProgress.Status.InProgress && !(Progress.TestNumber == 0 && Progress.StepNumber == 0))
        {
            FolderName = Progress.FileName;
            FileName = Progress.FileName;
            FileNameCollisions =
                $"{FileName}_collisions.csv".Replace(':', '.');
            FileNameFlights =
                $"{FileName}_flights.csv".Replace(':', '.');
            FileName += ".csv";
            resultsList = new List<string[]>();
            using (StreamReader reader = new StreamReader($"results\\{FolderName}\\{FileName}"))
            {
                // Read the file line by line until the end of the file
                while (!reader.EndOfStream)
                {
                    // Read a line from the file
                    string line = reader.ReadLine();
                    resultsList.Add(line.Split(CSVWriter.Instance.Separator));
                    // Process the line as needed
                    Console.WriteLine(line);
                }
            }
            collisionsList = new List<string[]>();
            using (var reader = new StreamReader($"results\\{FolderName}\\{FileNameCollisions}"))
            {
                // Read the file line by line until the end of the file
                while (!reader.EndOfStream)
                {
                    // Read a line from the file
                    var line = reader.ReadLine();
                    collisionsList.Add(line.Split(CSVWriter.Instance.Separator));
                    // Process the line as needed
                    Console.WriteLine(line);
                }
            }
            FlightsList = new List<string[]>();
            using (var reader = new StreamReader($"results\\{FolderName}\\{FileNameFlights}"))
            {
                // Read the file line by line until the end of the file
                while (!reader.EndOfStream)
                {
                    // Read a line from the file
                    var line = reader.ReadLine();
                    FlightsList.Add(line.Split(CSVWriter.Instance.Separator));
                    // Process the line as needed
                    Console.WriteLine(line);
                }
            }
        }
        else
        {
            FileName =
                $"{System.DateTime.Now.ToShortDateString()}_{System.DateTime.Now.ToShortTimeString()} {TestMode.ToString()}".Replace(':', '.');
            Progress.FileName = FileName;
            FolderName = FileName;
            FileNameCollisions =
                $"{FolderName}\\{FileName}_collisions.csv".Replace(':', '.');
            FileNameFlights =
                $"{FolderName}\\{FileName}_flights.csv".Replace(':', '.');
            FileName = $"{FolderName}\\{FileName}.csv";
            resultsList = new List<string[]>();
            collisionsList = new List<string[]>();
            FlightsList = new List<string[]>();
            var folder = Directory.CreateDirectory($"results\\{FolderName}"); // returns a DirectoryInfo object
            Debug.Log(folder);
        }
    }

    public void StartFromButton()
    {
        Progress.SimStatus = 0;
        //TODO use this as a way to select scenarios
        OneFlightLevel = true; //All uavs use 130f as flight height
        switch (DropdownOption1)
        {
            case 0:
                //EvasionType = UAV.EvasionType.None;
                break;
            case 1:
                //EvasionType = UAV.EvasionType.Nasa;
                break;
            case 2:
                //EvasionType = UAV.EvasionType.WaitAndGo;
                break;
            case 3:
                //EvasionType = UAV.EvasionType.Dynamic;
                break;
        }

        TestMode = SpawnMode.Balanced;
        EnableNoFlyZones = false;

        switch (DropdownOption2)
        {
            case 0:
                
                break;
            case 1:
                
                break;
            case 2:
                
                break;
            case 3:
                
                break;
        }

        AngleHeight = false;
        CollTesting = false;
        FlightLevelHeight = 10f;
        CollisionEvasion = false;
        SetupSimulationResults();
        StartTheTests();
    }

    public void StartTheTests()
    {
        SimStartTime = Time.time;
        //
        var flightLevelCount = (120f - 40f) / FlightLevelHeight;
        Debug.Log($"Flight level count {flightLevelCount} {(int)flightLevelCount}");
        FlightHeights = new float[(int)flightLevelCount+1];

        for (int i = 0; i < FlightHeights.Length; i++)
        {
            FlightHeights[i] = 40 + i * FlightLevelHeight;
        }

        if (OneFlightLevel)
        {
            FlightHeights = new float[1];
            FlightHeights[0] = 130f;
        }

        Debug.Log(FlightHeights);

        if (isRunning)
        {
            return;
        }

        if (Progress.StepNumber == 0)
        {
            resultsList.Add(new string[]
            {
                "Simulation Mode",
                "Polygon bias",
                "Polygons",
                "Range [km2]",
                "Collision Range [km2]",
                "NoFlyZones",
                "Angle height",
                "Collision Evasion",
                "Flight level height",
                "Time interval [minutes]",
                "Density target",
                "Flight count",
                "Far collisions",
                "Medium collisions",
                "Close collisions",
                "Spot on collisions"
            });
            collisionsList.Add(new string[]
            {   
                "Simulation Mode",
                "Density target",
                "currentPosition X",
                "currentPosition Y",
                "currentPosition Z",
                "currentPosition Type",
                "nextPosition X",
                "nextPosition Y",
                "nextPosition Z",
                "nextPosition Type",
                "targetPosition X",
                "targetPosition Y",
                "targetPosition Z",
                "targetPosition Type",
                "Heading angle",
                "flightState",
                "localCollisions",
                "currentPathStep",
                "currentStepPercentage",
                "flightSpeed",
                "aircraftDiameter",
                "Flight count",
                "Far collisions",
                "Medium collisions",
                "Close collisions",
                "Spot on collisions"
            });
            FlightsList.Add(new string[]
            {   
                "uavId",
                "Time flown",
                "path",
                "distance",
                "start time",
                "end time",
                "flight speed",
                "aircraft Diameter",
                "0 level max flights",
                "1 level max flights",
                "2 level max flights",
                "3 level max flights"
            });
        }
        
        Time.timeScale = TimeScale;
        rangeInMeters = Mathf.Sqrt(RangeKm2 * 1000000);
        collisionRangeInMeters = Mathf.Sqrt(CollisionRangeKm2 * 1000000);
        if (TestMode != SpawnMode.SmallerCenter)
        {
            collisionRangeInMeters = rangeInMeters;
        }

        StartCoroutine(DoTheTests());
    }
    public enum SpawnMode
    {
        SmallerCenter,
        Balanced,
        Polygons
    }

    public void SetupSaveLogCollisionTest()
    {
        FileName =
            $"{System.DateTime.Now.ToShortDateString()}_{System.DateTime.Now.ToShortTimeString()} {TestMode.ToString()}".Replace(':', '.');
        FileNameCollisionTests =
            $"{FileName}_collision_testing.csv".Replace(':', '.');
    }
    public void SaveLogCollisionTest()
    {
        CSVWriter.Instance.WriteToCsv(collisionTestingList, FileNameCollisionTests);
    }
    public string LogCollisionTest(int angleStep, float actualAngle, Vector3 uav1StartPosition, Vector3 uav2StartPosition, Vector3 uav1TargetPosition, Vector3 uav2TargetPosition, float uav1MaxFlightHeight, float uav2MaxFlightHeight, float uav1Speed, float uav2Speed, float uav1diameter, float uav2diameter, float closestDistance)
    {
        var item = new string[]
        {
            angleStep.ToString(CultureInfo.InvariantCulture),
            actualAngle.ToString(CultureInfo.InvariantCulture),
            uav1StartPosition.ToString(),
            uav2StartPosition.ToString(),
            uav1TargetPosition.ToString(),
            uav2TargetPosition.ToString(),
            uav1MaxFlightHeight.ToString(CultureInfo.InvariantCulture),
            uav2MaxFlightHeight.ToString(CultureInfo.InvariantCulture),
            uav1Speed.ToString(CultureInfo.InvariantCulture),
            uav2Speed.ToString(CultureInfo.InvariantCulture),
            uav1diameter.ToString(CultureInfo.InvariantCulture),
            uav2diameter.ToString(CultureInfo.InvariantCulture),
            closestDistance.ToString(CultureInfo.InvariantCulture),
            DroneSpawner.Instance.Options.CircleAnglesCount.ToString(CultureInfo.InvariantCulture),
        };
        collisionTestingList.Add(item);
        return string.Join("\t".ToString(), item);
    }

    public void LogUavCollision(WayPoint currentPosition, WayPoint nextPosition, WayPoint targetPosition,float headingAngle, UAV.FlightState flightState, int[] localCollisions, int currentPathStep, float currentStepPercentage, float flightSpeed, float aircraftDiameter, float crashDistanceSquared, float sqrMagnitude, int myCollisionId, int otherCollisionId)
    {
        collisionsList.Add(new string[]
        {   
            TestMode.ToString(),
            CurrentDensity.ToString(),
            currentPosition.Position.x.ToString(CultureInfo.InvariantCulture),
            currentPosition.Position.y.ToString(CultureInfo.InvariantCulture),
            currentPosition.Position.z.ToString(CultureInfo.InvariantCulture),
            currentPosition.Type.ToString(),
            nextPosition.Position.x.ToString(CultureInfo.InvariantCulture),
            nextPosition.Position.y.ToString(CultureInfo.InvariantCulture),
            nextPosition.Position.z.ToString(CultureInfo.InvariantCulture),
            nextPosition.Type.ToString(),
            targetPosition.Position.x.ToString(CultureInfo.InvariantCulture),
            targetPosition.Position.y.ToString(CultureInfo.InvariantCulture),
            targetPosition.Position.z.ToString(CultureInfo.InvariantCulture),
            targetPosition.Type.ToString(),
            headingAngle.ToString("F2", CultureInfo.InvariantCulture),
            flightState.ToString(),
            PrettyPrintIntArray(localCollisions),
            currentPathStep.ToString(),
            Math.Floor(currentStepPercentage*100f).ToString(CultureInfo.InvariantCulture),
            flightSpeed.ToString(CultureInfo.InvariantCulture),
            aircraftDiameter.ToString("F2", CultureInfo.InvariantCulture),
            CollisionControl.Instance.FlightCount.ToString(),
            CollisionControl.Instance.CollisionsCount[0].ToString(),
            CollisionControl.Instance.CollisionsCount[1].ToString(),
            CollisionControl.Instance.CollisionsCount[2].ToString(),
            (CollisionControl.Instance.CollisionsCount[3]+1/2).ToString(),
            crashDistanceSquared.ToString(CultureInfo.InvariantCulture),
            sqrMagnitude.ToString(CultureInfo.InvariantCulture),
            myCollisionId.ToString(CultureInfo.InvariantCulture),
            otherCollisionId.ToString(CultureInfo.InvariantCulture)
        });
        //Debug.Log(CollisionsList[0]);
    }

    public void LogUavFlight(int uavId, float timeFlown, List<WayPoint> path, float startTime, float endTime, float aircraftDiameter, float flightSpeed, int max0, int max1, int max2, int max3)
    {
        float distance = 0f;

        for (int i = 0; i < path.Count-1; i++)
        {
            distance += Vector3.Distance(path[i].Position, path[i + 1].Position);
        }

        FlightsList.Add(new string[]
        {  
            uavId.ToString(),
            timeFlown.ToString(CultureInfo.InvariantCulture),
            GetPathAsString(path),
            distance.ToString(CultureInfo.InvariantCulture),
            startTime.ToString(CultureInfo.InvariantCulture),
            endTime.ToString(CultureInfo.InvariantCulture),
            flightSpeed.ToString(CultureInfo.InvariantCulture),
            aircraftDiameter.ToString(CultureInfo.InvariantCulture),
            max0.ToString(CultureInfo.InvariantCulture),
            max1.ToString(CultureInfo.InvariantCulture),
            max2.ToString(CultureInfo.InvariantCulture),
            max3.ToString(CultureInfo.InvariantCulture)
    });
        //Debug.Log(CollisionsList[0]);
    }

    private string GetPathAsString(List<WayPoint> path)
    {
        string pathString = "";
        foreach(var point in path)
        {
            pathString += point.Serialize() + "_";
        }
        pathString = pathString.Remove(pathString.Length - 1, 1);
        return pathString;
    }

    private IEnumerator DoTheTests()
    {
        isRunning = true;
        yield return null;
        string polygonsString = Polygons.Aggregate("", (current, a) => current + (a.ToString() + " "));
        Planner.Instance.UpdateNoFlyZones();

        for (int i = StartDensity + Progress.StepNumber*Step; i <= EndDensity; i += Step)
        {
            Debug.Log(i);
            CurrentDensity = i;
            //Do the thing
            CollisionControl.Instance.ResetCollisionControlStatistics();
            CollisionControl.Instance.CollisionArea = new Vector3(-collisionRangeInMeters/2, 400, collisionRangeInMeters/2);
            CollisionControl.Instance.transform.localScale = new Vector3(collisionRangeInMeters, 400, collisionRangeInMeters);
            CollisionControl.Instance.transform.position = new Vector3(0, 100f, 0);
            DroneSpawner.Instance.ResetDroneSpawner();
            DroneSpawner.Instance.Options.Range = rangeInMeters;
            DroneSpawner.Instance.Options.SpawnMode = TestMode;
            DroneSpawner.Instance.Options.Polygons = Polygons;
            DroneSpawner.Instance.Options.PolygonBias = PolygonBias;
            DroneSpawner.Instance.Options.CollisionArea = new Vector3(collisionRangeInMeters / 2, 400, collisionRangeInMeters / 2);
            ActiveFlights = 0;

            CollisionControl.Instance.StartCollisionDetection(TimeFrameMinutes*60f);
            //DroneSpawner.instance.flightCountPerHour = i*RangeKM2;
            DroneSpawner.Instance.Options.FlightCountPerHour = i*CollisionRangeKm2;
            DroneSpawner.Instance.StartDirectionFlightSpawning();

            IntervalTime = 0;
            TimeScale = 20;

            yield return new WaitForSeconds(TimeFrameMinutes*60f);

            resultsList.Add(new string[]
            {   TestMode.ToString(),
                PolygonBias.ToString(),
                polygonsString,
                ((int)RangeKm2).ToString(),
                ((int)CollisionRangeKm2).ToString(),
                EnableNoFlyZones.ToString(),
                AngleHeight.ToString(),
                CollisionEvasion.ToString(),
                FlightLevelHeight.ToString(),
                ((int)TimeFrameMinutes).ToString(),
                i.ToString(),
                CollisionControl.Instance.FlightCount.ToString(),
                CollisionControl.Instance.CollisionsCount[0].ToString(),
                CollisionControl.Instance.CollisionsCount[1].ToString(),
                CollisionControl.Instance.CollisionsCount[2].ToString(),
                (CollisionControl.Instance.CollisionsCount[3]/2).ToString()
            });
            CSVWriter.Instance.WriteToCsv(resultsList, FileName);
            CSVWriter.Instance.WriteToCsv(collisionsList, FileNameCollisions);
            DroneSpawner.Instance.ResetDroneSpawner();
            yield return new WaitForSeconds(1f);
            FlightsList.Add(new[] { "", "" });
            CSVWriter.Instance.WriteToCsv(FlightsList, FileNameFlights);

            Progress.StepNumber = (i - StartDensity) / Step + 1;
            WriteProgressToJson();
        }
        CollisionControl.Instance.ResetCollisionControlStatistics();
        DroneSpawner.Instance.ResetDroneSpawner();
        isRunning = false;
        Debug.Log("Test done!");
        if (!MultipleTests)
        {
            Debug.Break();
            Application.Quit();
        }
    }

    public void WriteProgressToJson()
    {
        Debug.Log("Saving progress to progress.json");
        string json = JsonUtility.ToJson(Progress, true);

        // Specify the file path
        string filePath = "progress.json";

        // Write the JSON string to a file
        File.WriteAllText(filePath, json);
    }

    // Update is called once per frame
    void Update()
    {
        IntervalTime += Time.deltaTime;
        float tmp = 1 / (Time.unscaledDeltaTime);
        if (tmp < 15f)
        {
            TimeScale *= 0.95f;
        }
        if (tmp > 30f)
        {
            TimeScale += 0.1f;
        }
        TimeScale = Mathf.Clamp(TimeScale, 0.1f, MaxTimeScale);
        Time.timeScale = TimeScale;
        //Time.timeScale = 10f;
    }

    void OnValidate()
    {
        Time.timeScale = TimeScale;
        rangeInMeters = Mathf.Sqrt(RangeKm2) * 1000;
        collisionRangeInMeters = Mathf.Sqrt(CollisionRangeKm2) * 1000;
        if (TestMode != SpawnMode.SmallerCenter)
        {
            collisionRangeInMeters = rangeInMeters;
        }
    }
    void OnDrawGizmos()
    {
        //Draw denser spawn visualisations
        Gizmos.color = new Color(0, 255, 0, 0.5f);
        if (TestMode == SpawnMode.Polygons)
        {
            foreach (Vector3 item in Polygons)
            {
                Gizmos.DrawMesh(CylinderMesh, new Vector3(item.x, 100, item.z), Quaternion.identity, scale: new Vector3(item.y, 100, item.y));
            }
        }
        //Draw nofly zones visualisations
        Gizmos.color = new Color(255, 0, 0, 0.5f);
        if (EnableNoFlyZones)
        {
            foreach (Vector3 item in NoFlyZones)
            {
                Gizmos.DrawMesh(CylinderMesh, new Vector3(item.x, 100, item.z), Quaternion.identity, scale: new Vector3(item.y, 100, item.y));
            }
        }

        //Draw the spawn rangeInMeters borders
        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(new Vector3(0, 100, 0), new Vector3(rangeInMeters, 200, rangeInMeters));

        //Draw the collision rangeInMeters borders
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(new Vector3(0, 100, 0), new Vector3(collisionRangeInMeters, 190, collisionRangeInMeters));
    }

    string PrettyPrintBoolArray(bool[] array)
    {
        string output = "";
        for (int i = 0; i < array.Length; i++)
        {
            output += Convert.ToInt32(array[i]) + " ";
        }
        output = output.Trim();
        return output;
    }

    string PrettyPrintIntArray(int[] array)
    {
        string output = "";
        for (int i = 0; i < array.Length; i++)
        {
            output += array[i] + " ";
        }
        output = output.Trim();
        return output;
    }
}
