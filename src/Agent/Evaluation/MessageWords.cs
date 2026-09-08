namespace Agent.Evaluation;

// The one tokenizer behind the text checks: words are maximal runs of letters or digits,
// compared case-insensitively, so punctuation and the unicode dashes the labels use never
// glue two words together.
internal static class MessageWords
{
    // O(n) in the text length: one pass, one set insert per word.
    public static HashSet<string> Of(string text)
    {
        var words = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int start = -1;

        for (int index = 0; index <= text.Length; index++)
        {
            bool isWordCharacter = index < text.Length && char.IsLetterOrDigit(text[index]);

            if (isWordCharacter && start < 0)
            {
                start = index;
            }
            else if (!isWordCharacter && start >= 0)
            {
                words.Add(text[start..index]);
                start = -1;
            }
        }

        return words;
    }

    // D13 a: a fact is covered when at least half of its words appear, so "Oak Ridge"
    // covers "Oak Ridge Apartments" the way every label spells it. A fact with no words
    // (punctuation only) is never covered.
    public static bool Covers(HashSet<string> words, string fact)
    {
        string[] factWords = Of(fact).ToArray();
        int matched = factWords.Count(words.Contains);

        return factWords.Length > 0 && matched * 2 >= factWords.Length;
    }
}
