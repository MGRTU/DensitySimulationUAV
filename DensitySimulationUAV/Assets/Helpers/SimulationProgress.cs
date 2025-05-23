using System;

/// <summary>
/// !!! UNSTABLE !!! Class that represents a progress of a simulation. So that it would be possible to continue a simulation after a cutoff has happened for some reason.
/// </summary>
[Serializable]
public class SimulationProgress
{
    public string FileName;
    public int TestNumber;
    public int StepNumber;
    public Status SimStatus;

    /// <summary>
    /// Creates an empty SimulationProgress object
    /// </summary>
    public SimulationProgress()
    {
        FileName = string.Empty;
        TestNumber = 0;
        StepNumber = 0;
        SimStatus = Status.None;
    }

    /// <summary>
    /// The state of a simulation
    /// </summary>
    public enum Status
    {
        None = 0,
        InProgress = 1,
        Completed = 2
    }
}
