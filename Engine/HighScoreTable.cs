using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AVACastle.Engine;

public record HighScoreEntry(string Initials, int Score, int Castle);

/// <summary>
/// The top-ten table, kept in the user's application-data folder. A fresh table starts with modest
/// made-up scores, the way arcade machines shipped, so there is something to beat.
/// </summary>
public class HighScoreTable
{
    public const int Size = 10;
    public const int InitialsLength = 3;

    private static readonly string Folder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AVACastle");
    private static readonly string TablePath = Path.Combine(Folder, "highscores.txt");

    // The single-number file earlier versions kept; its score is carried into the table once.
    private static readonly string OldHighScorePath = Path.Combine(Folder, "highscore.txt");

    private static readonly string[] DefaultInitials = { "AVA", "STR", "CAS", "TLE", "RNG", "CAN", "MIN", "VEC", "TOR", "ZAP" };

    private readonly List<HighScoreEntry> _entries = new();

    public IReadOnlyList<HighScoreEntry> Entries => _entries;
    public int TopScore => _entries.Count > 0 ? _entries[0].Score : 0;

    /// <summary>The initials entered last time, offered as the starting point next time.</summary>
    public string LastInitials { get; private set; } = "AAA";

    public static HighScoreTable Load()
    {
        var table = new HighScoreTable();
        try
        {
            if (File.Exists(TablePath))
            {
                foreach (var line in File.ReadAllLines(TablePath))
                {
                    if (line.StartsWith("#last "))
                    {
                        table.LastInitials = Clean(line[6..]);
                        continue;
                    }

                    var parts = line.Split('|');
                    if (parts.Length == 3 && int.TryParse(parts[1], out int score) && int.TryParse(parts[2], out int castle))
                        table._entries.Add(new HighScoreEntry(Clean(parts[0]), score, castle));
                }
            }
            else
            {
                table.FillDefaults();
                if (File.Exists(OldHighScorePath) &&
                    int.TryParse(File.ReadAllText(OldHighScorePath).Trim(), out int old) && old > 0)
                {
                    table.Insert("???", old, 0);
                }
            }
        }
        catch
        {
            // An unreadable table just starts fresh.
            table._entries.Clear();
            table.FillDefaults();
        }

        table.Sort();
        return table;
    }

    private void FillDefaults()
    {
        for (int i = 0; i < Size; i++)
            _entries.Add(new HighScoreEntry(DefaultInitials[i], 10000 - i * 1000, Math.Max(1, 5 - i / 2)));
    }

    public bool Qualifies(int score) =>
        score > 0 && (_entries.Count < Size || score > _entries[^1].Score);

    /// <summary>Adds an entry and returns its place (0 = top), or -1 if it didn't make the table.</summary>
    public int Insert(string initials, int score, int castle)
    {
        if (!Qualifies(score)) return -1;

        // A new score goes above an equal older one: it was set more recently.
        int index = _entries.FindIndex(e => score >= e.Score);
        if (index < 0) index = _entries.Count;

        _entries.Insert(index, new HighScoreEntry(Clean(initials), score, castle));
        if (_entries.Count > Size)
            _entries.RemoveRange(Size, _entries.Count - Size);

        if (initials != "???")
            LastInitials = Clean(initials);
        return index;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var lines = new List<string> { $"#last {LastInitials}" };
            lines.AddRange(_entries.Select(e => $"{e.Initials}|{e.Score}|{e.Castle}"));

            // Write then swap, so a crash mid-write can't leave a half-written table.
            string temp = TablePath + ".tmp";
            File.WriteAllLines(temp, lines);
            File.Move(temp, TablePath, overwrite: true);
        }
        catch
        {
            // A read-only home folder just means the table isn't remembered.
        }
    }

    private void Sort() => _entries.Sort((a, b) => b.Score.CompareTo(a.Score));

    private static string Clean(string initials)
    {
        var chars = initials.ToUpperInvariant()
            .Where(c => InitialsEntry.Alphabet.Contains(c) || c == '?')
            .Take(InitialsLength)
            .ToArray();
        return new string(chars).PadRight(InitialsLength, ' ');
    }
}
