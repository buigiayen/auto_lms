public class GetToDayKpiTime
{
    public TodayGainedKpiTime TodayGainedKpiTime { get; set; }
}

public class TodayGainedKpiTime
{
    // LMS reports elapsed seconds with fractions, including numeric strings.
    public double TotalGainedKpiTimeInSeconds { get; set; }
    public double TotalGainedKpiTimeInSecondsByOnlineLearning { get; set; }
    public double MaxGainedKpiTimeInSeconds { get; set; }
    public double MaxOnlineLearningTimeInSeconds { get; set; }
    public bool Full
    {
        get
        { return (TotalGainedKpiTimeInSecondsByOnlineLearning + 1000 > MaxOnlineLearningTimeInSeconds) || (TotalGainedKpiTimeInSecondsByOnlineLearning == MaxOnlineLearningTimeInSeconds); }
        set { }
    }
}
