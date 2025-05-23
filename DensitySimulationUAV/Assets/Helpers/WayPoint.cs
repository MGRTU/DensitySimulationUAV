using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace Assets.Helpers
{
    /// <summary>
    /// Class that represents a Vector3 position with extra metadata
    /// </summary>
    [Serializable]
    public class WayPoint
    {
        public Vector3 Position;
        public float SpeedToWayPoint;
        public WayPointType Type;

        /// <summary>
        /// Constructor of an empty WayPoint
        /// </summary>
        public WayPoint()
        {
            Position = Vector3.zero;
            Type = WayPointType.Ground;
            SpeedToWayPoint = 1f;
        }

        public WayPoint(string serializedWayPoint)
        {
            var tmpPoint = Deserialize(serializedWayPoint);
            Position = tmpPoint.Position;
            SpeedToWayPoint = tmpPoint.SpeedToWayPoint;
            if (SpeedToWayPoint < 0.001f)
            {
                Debug.LogError("SpeedToWaypoint is realy small!!");
            }
            Type = tmpPoint.Type;
        }

        /// <summary>
        /// Constructor of a WayPoint instance from a Vector3 position. Any position above ground is marked as sky.
        /// </summary>
        /// <param name="position">Vector3 to be used as a position</param>
        /// <param name="speedToWayPoint">The speed at which uav is supposed to fly to this waypoint</param>
        public WayPoint(Vector3 position, float speedToWayPoint)
        {
            this.Position = position;
            Type = position.y > 0.1f ? WayPointType.Air : WayPointType.Ground;
            SpeedToWayPoint = speedToWayPoint;
            if (SpeedToWayPoint < 0.001f)
            {
                Debug.LogError("SpeedToWaypoint is realy small!!");
            }
        }

        /// <summary>
        /// Constructor of a WayPoint instance from a Vector3 position. Any position above ground is marked as sky.
        /// </summary>
        /// <param name="position">Vector3 to be used as a position</param>
        /// <param name="type">desired WayPointType</param>
        public WayPoint(Vector3 position, float speedToWayPoint, WayPointType type)
        {
            this.Position = position;
            this.SpeedToWayPoint = speedToWayPoint;
            this.Type = type;
        }
        


        public static WayPoint[] GetWayPointArray(Vector3[] points, float speedToWayPoint, WayPointType type)
        {
            var wayPoints = new WayPoint[points.Length];
            for (var i = 0; i < points.Length; i++)
            {
                wayPoints[i] = new WayPoint(points[i], speedToWayPoint, type);
            }
            return wayPoints;
        }

        /// <summary>
        /// Changes the WayPoint type 
        /// </summary>
        /// <param name="newType">Desired WayPoint type</param>
        /// <returns> Itself so that the function calls can be chained</returns>
        public WayPoint SetType(WayPointType newType)
        {
            Type = newType;
            return this;
        }

        /// <summary>
        /// Returns a copy of the WayPoint
        /// </summary>
        /// <returns></returns>
        public WayPoint Copy()
        {
            return new WayPoint(this.Position, this.SpeedToWayPoint, this.Type);
        }

        public WayPoint PushToHeight(float height, bool keepType = true)
        {
            Position.y = height;
            if (height > 0.1f && !keepType)
            {
                Type = WayPointType.Air;
            }
            return this;
        }

        public WayPoint AdjustSpeed(float speed)
        {
            SpeedToWayPoint = speed;
            return this;
        }

        public WayPoint Midpoint(WayPoint endPoint)
        {
            var tmp = new WayPoint
            {
                Position = Vector3.Lerp(this.Position, endPoint.Position, 0.5f)
            };
            if (tmp.Position.y > 0.1f)
            {
                tmp.Type = WayPointType.Air;
            }
            return tmp;
        }

        private static Vector3 StringToVector3(string sVector)
        {
            // Remove the parentheses
            if (sVector.StartsWith("(") && sVector.EndsWith(")"))
            {
                sVector = sVector.Substring(1, sVector.Length - 2);
            }

            //Debug.Log(sVector);
            // split the items
            string[] sArray = sVector.Split(',');

            //Debug.Log(sArray.Length);

            // store as a Vector3
            Vector3 result = new Vector3(
                float.Parse(sArray[0], CultureInfo.InvariantCulture),
                float.Parse(sArray[1], CultureInfo.InvariantCulture),
                float.Parse(sArray[2], CultureInfo.InvariantCulture));

            return result;
        }

        public static List<WayPoint> GetWayPointListFromPathString(string str)
        {
            var splitStr = str.Split('_');
            return splitStr.Select(t => new WayPoint(t)).ToList();
        }

        public enum WayPointType
        {
            Ground,
            Air,
            CollisionEvasion,
            NoFlyEvasion
        }

        public WayPoint Deserialize(string serializedWayPoint)
        {
            string sVector = serializedWayPoint;
            // Remove the parentheses
            if (sVector.StartsWith("(") && sVector.EndsWith(")"))
            {
                sVector = sVector.Substring(1, sVector.Length - 2);
            }

            //Debug.Log(sVector);
            // split the items
            string[] sArray = sVector.Split(',');

            //Debug.Log(sArray.Length);

            // store as a Vector3
            Vector3 position = new Vector3(
                float.Parse(sArray[0], CultureInfo.InvariantCulture),
                float.Parse(sArray[1], CultureInfo.InvariantCulture),
                float.Parse(sArray[2], CultureInfo.InvariantCulture));
            
            int type = int.Parse(sArray[3], CultureInfo.InvariantCulture);
            float speed = float.Parse(sArray[4], CultureInfo.InvariantCulture);


            return new WayPoint(position, speed, (WayPointType)type);
        }

        public string Serialize()
        {
            return $"({Position.x.ToString(CultureInfo.InvariantCulture)}, {Position.y.ToString(CultureInfo.InvariantCulture)}, {Position.z.ToString(CultureInfo.InvariantCulture)}, {((int)Type).ToString(CultureInfo.InvariantCulture)}, {SpeedToWayPoint.ToString(CultureInfo.InvariantCulture)})";
        }

        public override string ToString()
        {
            return $"WayPoint: ({Position}, {Type}, {SpeedToWayPoint})";
        }
    }
}
