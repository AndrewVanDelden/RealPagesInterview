using Agent.Evaluation;
using Xunit;

namespace Agent.Tests.Evaluation;

public class LanguageDetectorTests
{
    [Fact]
    public void Detect_EnglishText_English()
    {
        Assert.Equal(MessageLanguage.English, LanguageDetector.Detect("Hi Taylor, welcome to Oak Ridge! Tours are available this week. Reply STOP to opt out."));
    }

    [Fact]
    public void Detect_SpanishText_Spanish()
    {
        Assert.Equal(MessageLanguage.Spanish, LanguageDetector.Detect("Hola Lucía, gracias por tu interés en Oak Ridge. ¿Quieres agendar una visita esta semana? Responde STOP para cancelar."));
    }

    [Fact]
    public void Detect_NoStopWords_Null()
    {
        Assert.Null(LanguageDetector.Detect("Taylor. Oak Ridge. STOP."));
    }

    [Fact]
    public void Detect_EmptyText_Null()
    {
        Assert.Null(LanguageDetector.Detect(string.Empty));
    }

    [Fact]
    public void Detect_EqualCounts_Null()
    {
        Assert.Null(LanguageDetector.Detect("the para"));
    }

    [Fact]
    public void Detect_IsCaseInsensitive()
    {
        Assert.Equal(MessageLanguage.English, LanguageDetector.Detect("THE TOUR IS FOR YOU"));
    }

    [Theory]
    [InlineData("en", MessageLanguage.English)]
    [InlineData("ES", MessageLanguage.Spanish)]
    [InlineData("en-US", MessageLanguage.English)]
    [InlineData("es_MX", MessageLanguage.Spanish)]
    public void TryParseTag_KnownTag_ReturnsTheLanguage(string tag, MessageLanguage expected)
    {
        Assert.True(LanguageDetector.TryParseTag(tag, out MessageLanguage language));
        Assert.Equal(expected, language);
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("")]
    [InlineData("-")]
    public void TryParseTag_UnknownTag_False(string tag)
    {
        Assert.False(LanguageDetector.TryParseTag(tag, out _));
    }
}
