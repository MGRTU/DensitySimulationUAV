using System;

/// <summary>
/// An array of SimulationOptions objects. Used for multiple test chaining
/// </summary>
[Serializable]
public class SimulationOptionsArray
{
    public SimulationOptions[] OptionsArray = Array.Empty<SimulationOptions>();
}
