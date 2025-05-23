using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A class that represents Density grid map of UAVs
/// </summary>
public class DensityTracker : MonoBehaviour
{
    //TODO Rewrite all this as needed
    public static DensityTracker Instance;

    public int GridCount;
    public int GridHeightCount;
    private float range;
    private float gridLength;
    private float gridHeight;
    private float firstCoordinate;
    private int[,,] dataGrid;
    public int TotalDataPoints;
    public List<UAV> UavList;

    /// <summary>
    /// This function is used for real time editor mode visualisation updates
    /// </summary>
    private void OnValidate()
    {
        var tmpTestScheduler = GameObject.FindObjectOfType<TestScheduler>();
        range = Mathf.Sqrt(tmpTestScheduler.RangeKm2) * 1000;
        gridLength = range / GridCount;
        //gridHeightCount = 10;
        gridHeight = (120f - 40f) / GridHeightCount;
        firstCoordinate = -range / 2 + gridLength / 2;
        dataGrid = new int[GridCount, GridCount, GridHeightCount];
    }


    /// <summary>
    /// This function is used for the Editor mode visualisations
    /// </summary>
    private void OnDrawGizmos()
    {
        Gizmos.color = Color.green;
        for (var i = 0; i < GridCount; i++)
        {
            //Gizmos.color = Random.ColorHSV(0, 1, 1, 1, 1, 1, 1, 1);
            //Gizmos.color = Color.green;
            for (var j = 0; j < GridCount; j++)
            {
                for (var k = 0; k < GridHeightCount; k++)
                {
                    //Draw a cube with a color intensity gradient
                    Gizmos.color = new Color(0, 255, 0, (float)dataGrid[i, j, k] / 100);
                    Gizmos.DrawCube(
                        new Vector3(firstCoordinate + i * gridLength, 40+gridHeight/2+gridHeight*k, firstCoordinate + j * gridLength),
                        new Vector3(gridLength, gridHeight, gridLength));
                }
            }
        }
    }

    private void Start()
    {
        Instance = this;
        dataGrid = new int[GridCount, GridCount, GridHeightCount];
        UavList = new List<UAV>();
        InvokeRepeating(nameof(UpdateDataGrid), 0, 1f);
    }

    /// <summary>
    /// Logs the current position of all UAVs and updates the density map accordingly
    /// </summary>
    private void UpdateDataGrid()
    {
        foreach (var uav in UavList)
        {
            var currentGridX = (int)Mathf.Floor((uav.CurrentPosition.Position.x + range / 2) / gridLength);
            var currentGridY = (int)Mathf.Floor((uav.CurrentPosition.Position.z + range / 2) / gridLength);
            var currentGridZ = (int)Mathf.Floor((uav.CurrentPosition.Position.y - 40f) / gridHeight);

            if (currentGridZ < 0)
            {
                currentGridZ = 0;
            }

            if (currentGridZ > dataGrid.GetLength(2))
            {
                currentGridZ = dataGrid.GetLength(2) - 1;
            }

            if (uav.LastGridX == currentGridX && uav.LastGridY == currentGridY &&
                uav.LastGridZ == currentGridZ) continue;
            Debug.Log(uav .Id + " " + currentGridX + " " + currentGridY + " " + currentGridZ);
            dataGrid[currentGridX, currentGridY, currentGridZ]++;
            uav.LastGridX = currentGridX;
            uav.LastGridY = currentGridY;
            uav.LastGridZ = currentGridZ;
            TotalDataPoints++;
        }
    }
}
