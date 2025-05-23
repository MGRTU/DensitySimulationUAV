// Clean up sphere-specific event registrations when removing spheres
using System.Collections.Generic;
using UnityEngine;

public class GPUSphereCollisionSystem : MonoBehaviour
{
    // Compute shader that will perform the collision detection
    public ComputeShader collisionShader;

    // Maximum spheres the system can handle
    public int maxSpheres = 1024;

    // Maximum collisions per sphere we'll track
    public int maxCollisionsPerSphere = 16;

    // List of spheres in the scene
    private List<Transform> sphereObjects = new List<Transform>();
    private List<float> sphereRadii = new List<float>();

    // GPU buffers
    private ComputeBuffer sphereBuffer;
    private ComputeBuffer collisionCountBuffer;
    private ComputeBuffer collisionPairsBuffer;

    // CPU-side arrays for reading back data
    private SphereData[] sphereData;
    private int[] collisionCounts;
    private int[] collisionPairs;

    // Collision results (accessible from other scripts)
    private Dictionary<int, List<int>> sphereCollisions = new Dictionary<int, List<int>>();

    // Public delegates for different collision events
    public delegate void CollisionEnterEvent(int sphere1Index, int sphere2Index);
    public delegate void CollisionStayEvent(int sphere1Index, int sphere2Index);
    public delegate void CollisionExitEvent(int sphere1Index, int sphere2Index);

    // Global events (for all collisions)
    public event CollisionEnterEvent OnCollisionEnter;
    public event CollisionStayEvent OnCollisionStay;
    public event CollisionExitEvent OnCollisionExit;

    // Dictionary of events for specific spheres
    private Dictionary<int, CollisionEnterEvent> specificCollisionEnterEvents = new Dictionary<int, CollisionEnterEvent>();
    private Dictionary<int, CollisionStayEvent> specificCollisionStayEvents = new Dictionary<int, CollisionStayEvent>();
    private Dictionary<int, CollisionExitEvent> specificCollisionExitEvents = new Dictionary<int, CollisionExitEvent>();

    // Collection to track previous frame collisions
    private HashSet<long> previousFrameCollisions = new HashSet<long>();
    private HashSet<long> currentFrameCollisions = new HashSet<long>();

    public static GPUSphereCollisionSystem instance;

    // Helper to create a unique key for a collision pair
    private long GetCollisionPairKey(int sphere1, int sphere2)
    {
        // Ensure consistent ordering regardless of which sphere has lower index
        int minIndex = Mathf.Min(sphere1, sphere2);
        int maxIndex = Mathf.Max(sphere1, sphere2);

        // Combine into a single long value (each index can be up to 20 bits = ~1 million spheres)
        return ((long)minIndex << 32) | (long)maxIndex;
    }

    // Shader property IDs
    private int sphereBufferID;
    private int sphereCountID;
    private int collisionCountBufferID;
    private int collisionPairsBufferID;
    private int maxCollisionsPerSphereID;

    // Structure to represent a sphere in the compute shader
    struct SphereData
    {
        public Vector3 position;
        public float radius;
    }

    void Awake()
    {
        instance = this;
    }

    // Get a component directly from a sphere ID
    public T GetComponentFromSphereID<T>(int sphereID) where T : Component
    {
        if (sphereID >= 0 && sphereID < sphereObjects.Count)
        {
            return sphereObjects[sphereID].GetComponent<T>();
        }
        return null;
    }

    // Try to get a component from a sphere ID
    public bool TryGetComponentFromSphereID<T>(int sphereID, out T component) where T : Component
    {
        component = null;

        if (sphereID >= 0 && sphereID < sphereObjects.Count)
        {
            component = sphereObjects[sphereID].GetComponent<T>();
            return component != null;
        }

        return false;
    }

    void Start()
    {
        // Cache shader property IDs
        sphereBufferID = Shader.PropertyToID("sphereBuffer");
        sphereCountID = Shader.PropertyToID("sphereCount");
        collisionCountBufferID = Shader.PropertyToID("collisionCounts");
        collisionPairsBufferID = Shader.PropertyToID("collisionPairs");
        maxCollisionsPerSphereID = Shader.PropertyToID("maxCollisionsPerSphere");

        // Initialize CPU arrays
        sphereData = new SphereData[maxSpheres];
        collisionCounts = new int[maxSpheres];
        collisionPairs = new int[maxSpheres * maxCollisionsPerSphere];

        // Create GPU buffers
        sphereBuffer = new ComputeBuffer(maxSpheres, sizeof(float) * 4); // position (3 floats) + radius (1 float)
        collisionCountBuffer = new ComputeBuffer(maxSpheres, sizeof(int));
        collisionPairsBuffer = new ComputeBuffer(maxSpheres * maxCollisionsPerSphere, sizeof(int));

        // Note: Spheres should be added manually using AddSphere()
    }

    // Public methods to manage spheres in the system
    public int AddSphere(Transform sphereTransform, float radius)
    {
        for (int i = 0; i < sphereObjects.Count; i++)
        {
            if (sphereObjects[i].transform == sphereTransform)
            {
                Debug.LogWarning($"Transform {transform.name} is already registered with ID {i}. Returning existing ID.");
                //UnityEditor.EditorApplication.isPaused = true;
                return i;
            }
        }

        // Don't exceed max capacity
        if (sphereObjects.Count >= maxSpheres)
            return -1;

        // If not found, add it as normal
        sphereObjects.Add(sphereTransform);
        sphereRadii.Add(radius);
        return sphereObjects.Count - 1;
    }

    public bool RemoveSphere(Transform sphereTransform)
    {
        int index = sphereObjects.IndexOf(sphereTransform);
        if (index >= 0)
        {
            RemoveSphereAt(index);
            return true;
        }
        return false;
    }

    private void ClearEventsForSphere(int sphereIndex)
    {
        specificCollisionEnterEvents.Remove(sphereIndex);
        specificCollisionStayEvents.Remove(sphereIndex);
        specificCollisionExitEvents.Remove(sphereIndex);
    }

    // Special subscribe method for GameObject convenience
    public void SubscribeToCollisionEnter(GameObject gameObject, CollisionEnterEvent callback)
    {
        int index = GetSphereIndex(gameObject);
        if (index >= 0)
        {
            SubscribeToCollisionEnter(index, callback);
        }
    }

    // Helper to get sphere index from a GameObject
    public int GetSphereIndex(GameObject gameObject)
    {
        for (int i = 0; i < sphereObjects.Count; i++)
        {
            if (sphereObjects[i].gameObject == gameObject)
            {
                return i;
            }
        }
        return -1;
    }    // Methods to subscribe to events for specific sphere indices
    public void SubscribeToCollisionEnter(int sphereIndex, CollisionEnterEvent callback)
    {
        if (!specificCollisionEnterEvents.TryGetValue(sphereIndex, out var existingEvent))
        {
            specificCollisionEnterEvents[sphereIndex] = callback;
        }
        else
        {
            specificCollisionEnterEvents[sphereIndex] += callback;
        }
    }

    public void SubscribeToCollisionStay(int sphereIndex, CollisionStayEvent callback)
    {
        if (!specificCollisionStayEvents.TryGetValue(sphereIndex, out var existingEvent))
        {
            specificCollisionStayEvents[sphereIndex] = callback;
        }
        else
        {
            specificCollisionStayEvents[sphereIndex] += callback;
        }
    }

    public void SubscribeToCollisionExit(int sphereIndex, CollisionExitEvent callback)
    {
        if (!specificCollisionExitEvents.TryGetValue(sphereIndex, out var existingEvent))
        {
            specificCollisionExitEvents[sphereIndex] = callback;
        }
        else
        {
            specificCollisionExitEvents[sphereIndex] += callback;
        }
    }

    // Methods to unsubscribe from events for specific sphere indices
    public void UnsubscribeFromCollisionEnter(int sphereIndex, CollisionEnterEvent callback)
    {
        if (specificCollisionEnterEvents.TryGetValue(sphereIndex, out var existingEvent))
        {
            specificCollisionEnterEvents[sphereIndex] -= callback;
        }
    }

    public void UnsubscribeFromCollisionStay(int sphereIndex, CollisionStayEvent callback)
    {
        if (specificCollisionStayEvents.TryGetValue(sphereIndex, out var existingEvent))
        {
            specificCollisionStayEvents[sphereIndex] -= callback;
        }
    }

    public void UnsubscribeFromCollisionExit(int sphereIndex, CollisionExitEvent callback)
    {
        if (specificCollisionExitEvents.TryGetValue(sphereIndex, out var existingEvent))
        {
            specificCollisionExitEvents[sphereIndex] -= callback;
        }
    }    // Public method to access collision data for a specific sphere
    public List<int> GetCollisionsForSphere(int sphereIndex)
    {
        if (sphereCollisions.TryGetValue(sphereIndex, out List<int> collisions))
        {
            return new List<int>(collisions); // Return a copy to prevent modification
        }
        return new List<int>();
    }

    // Public method to get the number of spheres
    public int GetSphereCount()
    {
        return sphereObjects.Count;
    }

    public void RemoveSphereAt(int index)
    {
        if (index < 0 || index >= sphereObjects.Count)
            return;

        // Remove the sphere from our lists
        sphereObjects.RemoveAt(index);
        sphereRadii.RemoveAt(index);

        // Clear collision data related to this sphere
        ClearCollisionsForRemovedSphere(index);

        // Clear event handlers for this sphere
        ClearEventsForSphere(index);

        // Note: Since sphere indices have changed, any code holding onto sphere indices
        // needs to be aware that they may no longer be valid
    }

    private void ClearCollisionsForRemovedSphere(int removedIndex)
    {
        // Create new collections instead of modifying during iteration
        HashSet<long> newPreviousCollisions = new HashSet<long>();

        // Process previous frame collisions
        foreach (long pairKey in previousFrameCollisions)
        {
            int sphere1 = (int)(pairKey >> 32);
            int sphere2 = (int)(pairKey & 0xFFFFFFFF);

            // Skip pairs involving the removed sphere
            if (sphere1 == removedIndex || sphere2 == removedIndex)
            {
                continue;
            }

            // Adjust indices for spheres after the removed one
            if (sphere1 > removedIndex)
            {
                sphere1 -= 1;
            }

            if (sphere2 > removedIndex)
            {
                sphere2 -= 1;
            }

            // Add to the new collection with potentially adjusted indices
            newPreviousCollisions.Add(GetCollisionPairKey(sphere1, sphere2));
        }

        // Replace the old collection with the new one
        previousFrameCollisions = newPreviousCollisions;

        // Do the same for current frame collisions
        HashSet<long> newCurrentCollisions = new HashSet<long>();

        foreach (long pairKey in currentFrameCollisions)
        {
            int sphere1 = (int)(pairKey >> 32);
            int sphere2 = (int)(pairKey & 0xFFFFFFFF);

            // Skip pairs involving the removed sphere
            if (sphere1 == removedIndex || sphere2 == removedIndex)
            {
                continue;
            }

            // Adjust indices for spheres after the removed one
            if (sphere1 > removedIndex)
            {
                sphere1 -= 1;
            }

            if (sphere2 > removedIndex)
            {
                sphere2 -= 1;
            }

            // Add to the new collection with potentially adjusted indices
            newCurrentCollisions.Add(GetCollisionPairKey(sphere1, sphere2));
        }

        // Replace the old collection with the new one
        currentFrameCollisions = newCurrentCollisions;
    }

    public void UpdateSphereRadius(int sphereIndex, float newRadius)
    {
        if (sphereIndex >= 0 && sphereIndex < sphereRadii.Count)
        {
            sphereRadii[sphereIndex] = newRadius;
        }
    }

    // Clear all spheres
    public void ClearSpheres()
    {
        sphereObjects.Clear();
        sphereRadii.Clear();
        previousFrameCollisions.Clear();
        currentFrameCollisions.Clear();
        sphereCollisions.Clear();
    }

    void FixedUpdate()
    {
        // Update sphere positions
        UpdateSphereData();

        // Detect collisions on GPU
        DetectCollisions();

        // Read back collision data
        ReadCollisionData();
    }

    void UpdateSphereData()
    {
        for (int i = 0; i < sphereObjects.Count; i++)
        {
            sphereData[i].position = sphereObjects[i].position;
            sphereData[i].radius = sphereRadii[i];
        }

        // Upload sphere data to GPU
        sphereBuffer.SetData(sphereData);
    }

    void DetectCollisions()
    {
        // Set shader parameters
        collisionShader.SetBuffer(0, sphereBufferID, sphereBuffer);
        collisionShader.SetInt(sphereCountID, sphereObjects.Count);
        collisionShader.SetBuffer(0, collisionCountBufferID, collisionCountBuffer);
        collisionShader.SetBuffer(0, collisionPairsBufferID, collisionPairsBuffer);
        collisionShader.SetInt(maxCollisionsPerSphereID, maxCollisionsPerSphere);

        // Calculate dispatch size (number of thread groups)
        int threadGroupSize = Mathf.CeilToInt(sphereObjects.Count / 64.0f);
        threadGroupSize = Mathf.Max(1, threadGroupSize);

        // Dispatch the compute shader
        collisionShader.Dispatch(0, threadGroupSize, 1, 1);
    }

    void ReadCollisionData()
    {
        // Read collision counts and pairs back from GPU
        collisionCountBuffer.GetData(collisionCounts);
        collisionPairsBuffer.GetData(collisionPairs);

        // Clear previous collision data and current frame collisions
        sphereCollisions.Clear();
        currentFrameCollisions.Clear();

        // Process collision data
        for (int i = 0; i < sphereObjects.Count; i++)
        {
            int collisionCount = collisionCounts[i];
            List<int> collisions = new List<int>(collisionCount);

            // Get base index for this sphere's collisions
            int baseIndex = i * maxCollisionsPerSphere;

            // Add all collisions for this sphere
            for (int j = 0; j < collisionCount && j < maxCollisionsPerSphere; j++)
            {
                int collidingSphereIndex = collisionPairs[baseIndex + j];
                collisions.Add(collidingSphereIndex);

                // Add to current frame collisions set
                long pairKey = GetCollisionPairKey(i, collidingSphereIndex);
                currentFrameCollisions.Add(pairKey);
            }

            // Store collision list for this sphere
            sphereCollisions[i] = collisions;
        }

        // Process collision events by comparing with previous frame
        ProcessCollisionEvents();

        // Update previous frame collisions for next frame
        HashSet<long> temp = previousFrameCollisions;
        previousFrameCollisions = currentFrameCollisions;
        currentFrameCollisions = temp; // Reuse the old HashSet to avoid garbage collection
    }

    void ProcessCollisionEvents()
    {
        // Check for collision enters (in current frame but not in previous)
        foreach (long pairKey in currentFrameCollisions)
        {
            if (!previousFrameCollisions.Contains(pairKey))
            {
                // Extract sphere indices from pair key
                int sphere1 = (int)(pairKey >> 32);
                int sphere2 = (int)(pairKey & 0xFFFFFFFF);

                // Trigger global event
                OnCollisionEnter?.Invoke(sphere1, sphere2);

                // Trigger specific events for each sphere
                if (specificCollisionEnterEvents.TryGetValue(sphere1, out var event1))
                {
                    event1.Invoke(sphere1, sphere2);
                }

                if (specificCollisionEnterEvents.TryGetValue(sphere2, out var event2))
                {
                    event2.Invoke(sphere2, sphere1);
                }
            }
        }

        // Check for collision stays (in both current and previous)
        foreach (long pairKey in currentFrameCollisions)
        {
            if (previousFrameCollisions.Contains(pairKey))
            {
                int sphere1 = (int)(pairKey >> 32);
                int sphere2 = (int)(pairKey & 0xFFFFFFFF);

                // Trigger global event
                OnCollisionStay?.Invoke(sphere1, sphere2);

                // Trigger specific events for each sphere
                if (specificCollisionStayEvents.TryGetValue(sphere1, out var event1))
                {
                    event1.Invoke(sphere1, sphere2);
                }

                if (specificCollisionStayEvents.TryGetValue(sphere2, out var event2))
                {
                    event2.Invoke(sphere2, sphere1);
                }
            }
        }

        // Check for collision exits (in previous frame but not in current)
        foreach (long pairKey in previousFrameCollisions)
        {
            if (!currentFrameCollisions.Contains(pairKey))
            {
                int sphere1 = (int)(pairKey >> 32);
                int sphere2 = (int)(pairKey & 0xFFFFFFFF);

                // Trigger global event
                OnCollisionExit?.Invoke(sphere1, sphere2);

                // Trigger specific events for each sphere
                if (specificCollisionExitEvents.TryGetValue(sphere1, out var event1))
                {
                    event1.Invoke(sphere1, sphere2);
                }

                if (specificCollisionExitEvents.TryGetValue(sphere2, out var event2))
                {
                    event2.Invoke(sphere2, sphere1);
                }
            }
        }
    }

    void OnDestroy()
    {
        // Clean up GPU resources
        if (sphereBuffer != null) sphereBuffer.Release();
        if (collisionCountBuffer != null) collisionCountBuffer.Release();
        if (collisionPairsBuffer != null) collisionPairsBuffer.Release();

        sphereBuffer = null;
        collisionCountBuffer = null;
        collisionPairsBuffer = null;
    }

    // Get a list of colliding spheres by GameObject
    public List<GameObject> GetCollidingGameObjects(GameObject forObject)
    {
        // Find the sphere index for this GameObject
        int sphereIndex = -1;
        for (int i = 0; i < sphereObjects.Count; i++)
        {
            if (sphereObjects[i].gameObject == forObject)
            {
                sphereIndex = i;
                break;
            }
        }

        if (sphereIndex < 0)
            return new List<GameObject>();

        // Get collision indices
        List<int> collisionIndices = GetCollisionsForSphere(sphereIndex);

        // Convert to GameObjects
        List<GameObject> collidingObjects = new List<GameObject>(collisionIndices.Count);
        foreach (int index in collisionIndices)
        {
            if (index >= 0 && index < sphereObjects.Count)
            {
                collidingObjects.Add(sphereObjects[index].gameObject);
            }
        }

        return collidingObjects;
    }

    // Get a list of colliding spheres with their data
    public List<(GameObject gameObject, float radius, Vector3 position)> GetCollidingSphereData(int sphereIndex)
    {
        if (sphereIndex < 0 || sphereIndex >= sphereObjects.Count)
            return new List<(GameObject, float, Vector3)>();

        List<int> collisionIndices = GetCollisionsForSphere(sphereIndex);
        List<(GameObject, float, Vector3)> results = new List<(GameObject, float, Vector3)>(collisionIndices.Count);

        foreach (int index in collisionIndices)
        {
            if (index >= 0 && index < sphereObjects.Count)
            {
                Transform transform = sphereObjects[index];
                results.Add((transform.gameObject, sphereRadii[index], transform.position));
            }
        }

        return results;
    }
}