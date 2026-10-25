public class GetQuestionBankStatus
{
    public Questions? Questions { get; set; }
    public string? QuestionCategory { get; set; }
    public int Version { get; set; }
}

public class Questions
{
    public List<ResultQuestions?>? Result { get; set; }
}

public class ResultQuestions
{
    public string? QuestionCategory { get; set; }
    public string? Category { get; set; }
    public string Iid { get; set; }
    public string Id { get; set; }
    public string Ntype { get; set; }
    public int Type { get; set; }
    public string Content { get; set; }
    public List<ContentImageFiles> ContentImageFiles { get; set; }
    public string TplType { get; set; }
    public int Sw { get; set; }
    public string AnswerAsHint { get; set; }
    public List<int> Answers { get; set; }
    public string HelpText { get; set; }
    public List<McAnswers> McAnswers { get; set; }
    public string FeedbackTrue { get; set; }
    public string FeedbackFalse { get; set; }
    public List<string> Tags { get; set; }
    public int Difficulty { get; set; }
    public int Shufflable { get; set; }
    public List<Answers2> Answers2 { get; set; }
    public int KpiTime { get; set; }
    public string FileId { get; set; }
    public bool ContainsMathFormulas { get; set; }
    public int FailIfAnswerWrongly { get; set; }
    public string Hash { get; set; }
    public bool HasAudio { get; set; }
    public _Expand _Expand { get; set; }
    public int Acs { get; set; }
    public int Depth { get; set; }
}

public class _Expand
{
    public List<Organizations> Organizations { get; set; }
}

public class Organizations
{
    public string Name { get; set; }
    public string Code { get; set; }
    public int Iid { get; set; }
    public string Ntype { get; set; }
    public string Type { get; set; }
    public string Pid { get; set; }
    public string Id { get; set; }
}

public class Answers2
{
    public string Text { get; set; }
    public int IsAnswer { get; set; }
}

public class McAnswers
{
    public string Text { get; set; }
    public int IsAnswer { get; set; }
}

public class ContentImageFiles
{
    public object Link { get; set; }
}
