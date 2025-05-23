using System.Globalization;
using System.IO;
using UnityEngine;

/// <summary>
/// Class that represents a crash heat map of UAVs
/// </summary>
public class CrashHeatmap : MonoBehaviour
{
    private string fileName = "input.csv";
    public GameObject CollisionIndicator;

    //Attach this to a GameObject in scene and add a valid path to csv in the inspector.
    //The code then spawns CollisionIndicator objects in the world
    void Start()
    {
        using var reader = new StreamReader($"{fileName}");
        var line = reader.ReadLine(); //skips header line
        // Read the file line by line until the end of the file
        while (!reader.EndOfStream)
        {
            //TODO Use CollisionFromHistory objects instead
            // Read a line from the file
            line = reader.ReadLine();
            var substrings = line.Split(';');
            //Debug.Log(substrings[2] + " " + substrings[3] + " " + substrings[4]);
            var x = float.Parse(substrings[2], CultureInfo.InvariantCulture);
            var y = float.Parse(substrings[3], CultureInfo.InvariantCulture);
            var z = float.Parse(substrings[4], CultureInfo.InvariantCulture);
            var position = new Vector3(x, y, z);
            var tmpGameObject = GameObject.Instantiate(CollisionIndicator, position, Quaternion.identity);
            tmpGameObject.transform.SetParent(this.gameObject.transform);
        }
    }
}
