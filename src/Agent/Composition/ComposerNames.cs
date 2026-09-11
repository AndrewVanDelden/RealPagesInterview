namespace Agent.Composition;

// Playbook step 42: the composer vocabulary named once. These are the same two
// spellings `--composer` accepts and the same two the diagnostics report, so the flag a run
// was given and the implementation a record names can be compared without translating
// between two hand-spelled lists.
public static class ComposerNames
{
    public const string Template = "template";

    public const string OpenAi = "openai";
}
