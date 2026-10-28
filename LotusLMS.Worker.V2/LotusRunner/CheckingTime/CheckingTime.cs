using LotusLMS.Api;

public class CheckingTimeDTO
{
    public class AutoStudy
    {
        /// <summary>
        /// Chỉ tìm cho lý thuyết để break khi đủ time cho 1 bài chính
        /// </summary>
        public int? iidRoot {get;set;}
        public int iid { get; set; }
        public string name { get; set; }
        public string ntype { get; set; } = "video";
        public TypeAutoStudy typeAutoStudy {get;set;}
    }
    public enum TypeAutoStudy
    {
        AutoStudy,
        PracticeOnly,
        PracticeSpecialOnly,
        Exam
    }
}