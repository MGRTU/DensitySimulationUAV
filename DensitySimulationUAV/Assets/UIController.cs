using System;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class UIController : MonoBehaviour
{
    public TMP_Text ActiveFlightText;
    public TMP_Text TimeScaleText;
    public TMP_Text PercentageText;
    public TMP_Text FpsText;


    public TMP_InputField Rangekm2InputField;
    public TMP_InputField Collisionrangekm2InputField;
    public TMP_InputField StartDensityInputField;
    public TMP_InputField EndDensityInputField;
    public TMP_InputField StepInputField;
    public TMP_InputField TimeminsInputField;
    public TMP_InputField MaxTimeScaleInputField;
    public TMP_InputField FlightLevelHeightInputField;
    public Toggle AngleHeightToggle;
    public Toggle FlightLevelCollCutoffToggle;
    public TMP_Dropdown Dropdown;
    public TMP_Dropdown Dropdown2;

    public static UIController Instance;
    void Start()
    {
        Instance = this;
        Invoke(nameof(SetInputs), 1f);
    }
    public void SetInputs()
    {
        Rangekm2InputField.text = TestScheduler.Instance.RangeKm2.ToString();
        Collisionrangekm2InputField.text = TestScheduler.Instance.CollisionRangeKm2.ToString();
        StartDensityInputField.text = TestScheduler.Instance.StartDensity.ToString();
        EndDensityInputField.text = TestScheduler.Instance.EndDensity.ToString();
        StepInputField.text = TestScheduler.Instance.Step.ToString();
        TimeminsInputField.text = TestScheduler.Instance.TimeFrameMinutes.ToString();
        MaxTimeScaleInputField.text = TestScheduler.Instance.MaxTimeScale.ToString();
    }
    public void ReadInputs()
    {
        Debug.Log("Readinputs");
        TestScheduler.Instance.RangeKm2 = Int32.Parse(Rangekm2InputField.text);
        TestScheduler.Instance.CollisionRangeKm2 = Int32.Parse(Collisionrangekm2InputField.text);
        TestScheduler.Instance.StartDensity = Int32.Parse(StartDensityInputField.text);
        TestScheduler.Instance.EndDensity = Int32.Parse(EndDensityInputField.text);
        TestScheduler.Instance.Step = Int32.Parse(StepInputField.text);
        TestScheduler.Instance.TimeFrameMinutes = Int32.Parse(TimeminsInputField.text);
        TestScheduler.Instance.MaxTimeScale = Int32.Parse(MaxTimeScaleInputField.text);
        TestScheduler.Instance.FlightLevelHeight = float.Parse(FlightLevelHeightInputField.text);
        TestScheduler.Instance.AngleHeight = AngleHeightToggle.isOn;
        TestScheduler.Instance.DropdownOption1 = Dropdown.value;
        TestScheduler.Instance.DropdownOption2 = Dropdown2.value;
    }

    // Update is called once per frame
    void Update()
    {
        ActiveFlightText.text = $"Active flights: {TestScheduler.Instance.ActiveFlights}";
        TimeScaleText.text = $"Time scale: {Time.timeScale}";
        var something = ((float)TestScheduler.Instance.EndDensity - (float)TestScheduler.Instance.StartDensity + (float)TestScheduler.Instance.Step); //diapazons
        var something2 = something / (float)TestScheduler.Instance.Step; //soļu skaits
        var something3 = 100 / something2; //procenti uz soli
        var something4 =  ((float)TestScheduler.Instance.CurrentDensity - (float)TestScheduler.Instance.StartDensity) / something * 100; //procenti pilnie
        var something5 = (float)TestScheduler.Instance.IntervalTime /
            ((float)TestScheduler.Instance.TimeFrameMinutes * 60) * something3;
        //PercentageText.text = $"Percentage: {something4 + something5} Test: {TestScheduler.Instance.Progress.TestNumber + 1} of {TestScheduler.Instance.Options.OptionsArray.Length}";
        PercentageText.text = $"Test: {DroneSpawner.Instance.CurrentTest + 1} of {DroneSpawner.Instance.TotalTests+1}";
        FpsText.text = $"Fps: {1.0f / Time.unscaledDeltaTime}";
    }
}
