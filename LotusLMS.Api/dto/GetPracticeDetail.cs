
public class GetPracticeDetail
{
    public string Name { get; set; }
    public int ExamScoIid { get; set; }
    public int SyllabusIid { get; set; }
    public List<string> Modes { get; set; }
    public string CpCalculationMethod { get; set; }
    public PracticeTagOptions PracticeTagOptions { get; set; }
    public string TargetUser { get; set; }
    public string Status { get; set; }
    public string PracticeVersion { get; set; }
    public string Ntype { get; set; }
    public int IsClone { get; set; }
    public int CloneFrom { get; set; }
    public int MinLearningTime { get; set; }
    public U U { get; set; }
    public int Ts { get; set; }
    public string School { get; set; }
    public string Dmn { get; set; }
    public int Iid { get; set; }
    public int UpdatedTs { get; set; }
    public string Id { get; set; }
    public _expand _expand { get; set; }
}

public class _expand
{
    public ExamSco ExamSco { get; set; }
}

public class ExamSco
{
    public string Ntype { get; set; }
    public List<int> Organizations { get; set; }
    public List<int> AncestorOrganizations { get; set; }
    public string Type { get; set; }
    public string Name { get; set; }
    public string Duration { get; set; }
    public int TotalExamScore { get; set; }
    public int NrOfExamQuestions { get; set; }
    public int Shared { get; set; }
    public string TplType { get; set; }
    public string Code { get; set; }
    public string Status { get; set; }
    public List<int> Blueprint { get; set; }
    public int Dirty { get; set; }
    public List<int> QuestionBanks { get; set; }
    public int IsClone { get; set; }
    public int CloneFrom { get; set; }
    public U U { get; set; }
    public int Ts { get; set; }
    public string School { get; set; }
    public string Dmn { get; set; }
    public int Iid { get; set; }
    public int UpdatedTs { get; set; }
    public string Id { get; set; }
    public List<TemplateDetails> TemplateDetails { get; set; }
    public int TotalScore { get; set; }
}

public class TemplateDetails
{
    public string Ntype { get; set; }
    public List<int> Organizations { get; set; }
    public List<int> AncestorOrganizations { get; set; }
    public string Type { get; set; }
    public string Name { get; set; }
    public string Duration { get; set; }
    public int TotalExamScore { get; set; }
    public int NrOfExamQuestions { get; set; }
    public int Shared { get; set; }
    public string TplType { get; set; }
    public string Code { get; set; }
    public string Status { get; set; }
    public List<int> Blueprint { get; set; }
    public int Dirty { get; set; }
    public List<int> QuestionBanks { get; set; }
    public int IsClone { get; set; }
    public int CloneFrom { get; set; }
    public U U { get; set; }
    public int Ts { get; set; }
    public string School { get; set; }
    public string Dmn { get; set; }
    public int Iid { get; set; }
    public int UpdatedTs { get; set; }
    public string Id { get; set; }
    public List<ExerciseTemplate> ExerciseTemplate { get; set; }
    public int IsSimple { get; set; }
    public List<int> ExerciseTemplateIids { get; set; }
    public int PassingScore { get; set; }
}

public class ExerciseTemplate
{
    public string Name { get; set; }
    public int Score { get; set; }
    public List<Groups> Groups { get; set; }
    public int ExamTemplateIid { get; set; }
    public string Status { get; set; }
    public int IsClone { get; set; }
    public int CloneFrom { get; set; }
    public U U { get; set; }
    public int Ts { get; set; }
    public string School { get; set; }
    public string Dmn { get; set; }
    public int Iid { get; set; }
    public int UpdatedTs { get; set; }
    public string Id { get; set; }
}

public class Groups
{
    public int DisplayByGroup { get; set; }
    public int Score { get; set; }
    public int NrOfQuestions { get; set; }
    public List<int> QuestionType { get; set; }
}

public class U
{
    public string Id { get; set; }
    public int Iid { get; set; }
    public string Name { get; set; }
    public string Code { get; set; }
    public string Mail { get; set; }
}

public class PracticeTagOptions
{
    public int QuestionsPerEachPracticeOption { get; set; }
    public int ShowResultScreen { get; set; }
    public string DisplayListOfQuestionsMode { get; set; }
    public int DisplayQuestionsOverviewInDashboard { get; set; }
}