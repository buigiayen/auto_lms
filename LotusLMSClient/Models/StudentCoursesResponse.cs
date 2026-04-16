using System.Collections.Generic;
using Newtonsoft.Json;

namespace LotusLMSClient.Models
{
    public class StudentCoursesResponse
    {
        [JsonProperty("result")]
        public List<StudentCourseResult> Result { get; set; }
    }

    public class StudentCourseResult
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("iid")]
        public int Iid { get; set; }

        [JsonProperty("course")]
        public StudentCourseInfo Course { get; set; }

        [JsonProperty("children")]
        public List<StudentCourseChild> Children { get; set; }
    }

    public class StudentCourseChild
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("iid")]
        public string Iid { get; set; }

        [JsonProperty("course")]
        public StudentCourseInfo Course { get; set; }
    }

    public class StudentCourseInfo
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("iid")]
        public int Iid { get; set; }

        [JsonProperty("cp")]
        public double Cp { get; set; }

        [JsonProperty("p")]
        public double P { get; set; }
        [JsonProperty("pf")]
        public double PF { get; set; }

        [JsonProperty("enrolment_plan")]
        public StudentEnrolmentPlan EnrolmentPlan { get; set; }
    }

    public class StudentEnrolmentPlan
    {
        [JsonProperty("iid")]
        public int Iid { get; set; }
    }

    public class DashboardCourseItem
    {
        public int Iid { get; set; }
        public string Name { get; set; }
        public double CpPercent { get; set; }
        public double PHours { get; set; }
        public  double pf { get; set; }
    }

    public class StudentEnrolmentPlansResponse
    {
        [JsonProperty("result")]
        public List<StudentEnrolmentPlanItem> Result { get; set; }
    }

    public class StudentEnrolmentPlanItem
    {
        [JsonProperty("iid")]
        public int Iid { get; set; }
    }
}
