public class GetToDayKpiTime
{
    public TodayGainedKpiTime TodayGainedKpiTime { get; set; }
}

public class TodayGainedKpiTime
{
    public int TotalGainedKpiTimeInSeconds { get; set; }
    public int TotalGainedKpiTimeInSecondsByOnlineLearning { get; set; }
    public int MaxGainedKpiTimeInSeconds { get; set; }
    public int MaxOnlineLearningTimeInSeconds { get; set; }
    public bool Full
    {
        get
        { return (TotalGainedKpiTimeInSecondsByOnlineLearning + 1000 > MaxOnlineLearningTimeInSeconds) || (TotalGainedKpiTimeInSecondsByOnlineLearning == MaxOnlineLearningTimeInSeconds); }
        set { }
    }
}