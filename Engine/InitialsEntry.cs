using System;

namespace AVACastle.Engine;

/// <summary>
/// Arcade-style initials entry: three letters, one under the cursor at a time. Letters can be typed
/// directly or dialled up and down through the alphabet.
/// </summary>
public class InitialsEntry
{
    public const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789. ";

    private readonly char[] _letters;

    public int Cursor { get; private set; }
    public bool IsDone { get; private set; }
    public string Initials => new(_letters);

    public InitialsEntry(string start)
    {
        _letters = (start + "AAA")[..HighScoreTable.InitialsLength].ToCharArray();
        for (int i = 0; i < _letters.Length; i++)
        {
            if (Alphabet.IndexOf(_letters[i]) < 0)
                _letters[i] = 'A';
        }
    }

    /// <summary>Steps the letter under the cursor through the alphabet, wrapping at either end.</summary>
    public void Cycle(int direction)
    {
        int index = Alphabet.IndexOf(_letters[Cursor]);
        index = (index + direction % Alphabet.Length + Alphabet.Length) % Alphabet.Length;
        _letters[Cursor] = Alphabet[index];
    }

    /// <summary>Sets the letter under the cursor and moves on, as typing does.</summary>
    public void Type(char letter)
    {
        letter = char.ToUpperInvariant(letter);
        if (Alphabet.IndexOf(letter) < 0) return;
        _letters[Cursor] = letter;
        Advance();
    }

    /// <summary>Accepts the letter under the cursor; accepting the last one finishes the entry.</summary>
    public void Advance()
    {
        if (Cursor < _letters.Length - 1)
            Cursor++;
        else
            IsDone = true;
    }

    public void Move(int direction) => Cursor = Math.Clamp(Cursor + direction, 0, _letters.Length - 1);

    public void Back() => Move(-1);

    public void Finish() => IsDone = true;
}
