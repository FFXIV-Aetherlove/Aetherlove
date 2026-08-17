using System;
using System.Collections.Generic;
using AetherOS.Apps.Eordle.Words;

namespace AetherOS.Apps.Eordle;

public enum EordleTile
{
    Absent,
    Present,
    Correct,
}

public enum EordleKeyState
{
    Unknown,
    Absent,
    Present,
    Correct,
}

public enum EordleSubmit
{
    TooShort,
    NotAWord,
    Accepted,
    Solved,
    Failed,
}

public enum EordleOutcome
{
    Playing,
    Solved,
    Failed,
}

/// <summary>The marathon: consecutive five-letter words, six guesses each, no clock limit. The first
/// word that survives all six guesses ends the run.</summary>
public sealed class EordleGame
{
    public const int MaxGuesses = 6;
    public const int WordLength = EordleWords.Length;

    private const int AlphabetSize = 26;

    private readonly Random random = new();
    private readonly HashSet<string> usedAnswers = new();
    private readonly List<string> rows = new();
    private readonly List<EordleTile[]> rowStates = new();
    private readonly EordleKeyState[] keyStates = new EordleKeyState[AlphabetSize];

    public WordLanguage Language { get; private set; }

    public string Answer { get; private set; } = string.Empty;

    public string Entry { get; private set; } = string.Empty;

    public int Score { get; private set; }

    public int WordsSolved { get; private set; }

    public int TotalGuesses { get; private set; }

    public double RunSeconds { get; private set; }

    public double WordSeconds { get; private set; }

    public EordleOutcome Outcome { get; private set; } = EordleOutcome.Playing;

    public int LastWordPoints { get; private set; }

    public int LastWordGuesses { get; private set; }

    /// <summary>Fewest guesses any solved word took this run; 0 until something is solved.</summary>
    public int BestWordGuesses { get; private set; }

    public IReadOnlyList<string> Rows => this.rows;

    public IReadOnlyList<EordleTile[]> RowStates => this.rowStates;

    public EordleKeyState KeyState(char letter) => this.keyStates[letter - 'A'];

    public void Start(WordLanguage language)
    {
        this.Language = language;
        this.Score = 0;
        this.WordsSolved = 0;
        this.TotalGuesses = 0;
        this.RunSeconds = 0.0;
        this.LastWordPoints = 0;
        this.LastWordGuesses = 0;
        this.BestWordGuesses = 0;
        this.usedAnswers.Clear();
        BeginWord();
    }

    public void Tick(double delta)
    {
        if (this.Outcome != EordleOutcome.Playing)
        {
            return;
        }
        this.RunSeconds += delta;
        this.WordSeconds += delta;
    }

    public void TypeLetter(char letter)
    {
        if (this.Outcome != EordleOutcome.Playing || this.Entry.Length >= WordLength
            || letter is < 'A' or > 'Z')
        {
            return;
        }
        this.Entry += letter;
    }

    public void Backspace()
    {
        if (this.Outcome == EordleOutcome.Playing && this.Entry.Length > 0)
        {
            this.Entry = this.Entry[..^1];
        }
    }

    public EordleSubmit Submit()
    {
        if (this.Outcome != EordleOutcome.Playing)
        {
            return EordleSubmit.TooShort;
        }
        if (this.Entry.Length < WordLength)
        {
            return EordleSubmit.TooShort;
        }
        if (!EordleWords.IsValid(this.Language, this.Entry))
        {
            return EordleSubmit.NotAWord;
        }

        var guess = this.Entry;
        this.Entry = string.Empty;
        var states = Evaluate(this.Answer, guess);
        this.rows.Add(guess);
        this.rowStates.Add(states);
        this.TotalGuesses++;
        UpdateKeyStates(guess, states);

        if (guess == this.Answer)
        {
            CompleteWord();
            return EordleSubmit.Solved;
        }
        if (this.rows.Count >= MaxGuesses)
        {
            this.Outcome = EordleOutcome.Failed;
            return EordleSubmit.Failed;
        }
        return EordleSubmit.Accepted;
    }

    /// <summary>Moves on to the next word after a solve; the board and keyboard reset, the score stays.</summary>
    public void NextWord()
    {
        if (this.Outcome == EordleOutcome.Solved)
        {
            BeginWord();
        }
    }

    private void BeginWord()
    {
        this.rows.Clear();
        this.rowStates.Clear();
        Array.Clear(this.keyStates);
        this.Entry = string.Empty;
        this.WordSeconds = 0.0;
        this.Outcome = EordleOutcome.Playing;
        PickAnswer();
    }

    private void PickAnswer()
    {
        var answers = EordleWords.AnswersFor(this.Language);
        if (this.usedAnswers.Count >= answers.Count)
        {
            this.usedAnswers.Clear();
        }
        string pick;
        do
        {
            pick = answers[this.random.Next(answers.Count)];
        }
        while (!this.usedAnswers.Add(pick));
        this.Answer = pick;
    }

    private void CompleteWord()
    {
        this.LastWordGuesses = this.rows.Count;
        this.LastWordPoints = EordleScoring.WordPoints(this.rows.Count, this.WordSeconds);
        this.Score += this.LastWordPoints;
        this.WordsSolved++;
        if (this.BestWordGuesses == 0 || this.rows.Count < this.BestWordGuesses)
        {
            this.BestWordGuesses = this.rows.Count;
        }
        this.Outcome = EordleOutcome.Solved;
    }

    /// <summary>The standard two-pass evaluation: exact places first, then leftover letter counts pay
    /// out the present-elsewhere marks so duplicates never over-report.</summary>
    private static EordleTile[] Evaluate(string answer, string guess)
    {
        var states = new EordleTile[WordLength];
        Span<int> remaining = stackalloc int[AlphabetSize];
        for (var i = 0; i < WordLength; i++)
        {
            if (guess[i] == answer[i])
            {
                states[i] = EordleTile.Correct;
            }
            else
            {
                remaining[answer[i] - 'A']++;
            }
        }
        for (var i = 0; i < WordLength; i++)
        {
            if (states[i] == EordleTile.Correct)
            {
                continue;
            }
            var slot = guess[i] - 'A';
            if (remaining[slot] > 0)
            {
                remaining[slot]--;
                states[i] = EordleTile.Present;
            }
            else
            {
                states[i] = EordleTile.Absent;
            }
        }
        return states;
    }

    private void UpdateKeyStates(string guess, EordleTile[] states)
    {
        for (var i = 0; i < WordLength; i++)
        {
            var slot = guess[i] - 'A';
            var incoming = states[i] switch
            {
                EordleTile.Correct => EordleKeyState.Correct,
                EordleTile.Present => EordleKeyState.Present,
                _ => EordleKeyState.Absent,
            };
            if (incoming > this.keyStates[slot])
            {
                this.keyStates[slot] = incoming;
            }
        }
    }
}
