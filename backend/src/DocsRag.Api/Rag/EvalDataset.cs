using DocsRag.Api.Models;

namespace DocsRag.Api.Rag;

/// <summary>
/// The "golden set" for /eval: a small, hand-labelled list of questions, each paired
/// with the GDPR article (the source file) that ought to be retrieved to answer it.
/// This is the ground truth the retrieval metrics are scored against.
/// </summary>
public static class EvalDataset
{
    public static readonly IReadOnlyList<EvalCase> Gdpr =
    [
        new("Do I need consent to process personal data?", "art-07.md"),
        new("What conditions apply to a child's consent for online services?", "art-08.md"),
        new("What information must be provided when collecting personal data?", "art-13.md"),
        new("What is the data subject's right of access?", "art-15.md"),
        new("How can I correct inaccurate personal data about me?", "art-16.md"),
        new("What is the right to be forgotten?", "art-17.md"),
        new("What is the right to data portability?", "art-20.md"),
        new("Can I object to the processing of my personal data?", "art-21.md"),
        new("When must a personal data breach be notified to the authority?", "art-33.md"),
        new("When is a data protection impact assessment required?", "art-35.md"),
    ];
}
