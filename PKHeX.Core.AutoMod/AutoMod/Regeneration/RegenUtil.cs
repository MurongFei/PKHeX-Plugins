﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PKHeX.Core.AutoMod;

public static class RegenUtil
{
    public static bool GetTrainerInfo(IEnumerable<string> lines, byte format, out ITrainerInfo tr)
    {
        var sti = new SimpleTrainerInfo { Generation = format };

        var split = Split(lines);
        bool any = false;
        int TID7 = -1;
        int SID7 = -1;
        foreach (var (key, value) in split)
        {
            switch (key)
            {
                case "OT":
                    sti.OT = value;
                    break;
                case "TID" when int.TryParse(value, out int tid) && tid >= 0:
                    TID7 = tid;
                    break;
                case "SID" when int.TryParse(value, out int sid) && sid >= 0:
                    SID7 = sid;
                    break;
                case "OTGender":
                    sti.Gender = value is "Female" or "F" ? (byte)1 : (byte)0;
                    break;
                default:
                    continue;
            }

            any = true;
        }

        tr = sti;
        if (!any || (TID7 < 0 && SID7 < 0))
            return any;

        TID7 = Math.Max(TID7, 0);
        SID7 = Math.Max(SID7, 0);
        const int mil = 1_000_000;
        uint repack = ((uint)SID7 * mil) + (uint)TID7;
        tr.TID16 = format < 7 ? (ushort)TID7 : (ushort)(repack & 0xFFFF);
        tr.SID16 = format < 7 ? (ushort)SID7 : (ushort)(repack >> 16);
        return true;
    }

    private const char Splitter = ':';
    public const char EncounterFilterPrefix = '~';

    public static IReadOnlyList<StringInstruction> GetEncounterFilters(IEnumerable<string> lines)
    {
        var valid = lines.Where(z => z.StartsWith(EncounterFilterPrefix.ToString()) && !z.Contains("Version")).ToList();
        return CleanFilters(valid);
    }

    public static IReadOnlyList<StringInstruction> GetVersionFilters(IEnumerable<string> lines)
    {
        var valid = lines.Where(z => z.StartsWith(EncounterFilterPrefix.ToString()) && z.Contains("Version")).ToList();
        return CleanFilters(valid);
    }

    private static StringInstruction[] CleanFilters(List<string> lines)
    {
        if (lines.Count == 0)
            return [];

        var cleaned = lines.Select(z => z.TrimStart(EncounterFilterPrefix));
        var filters = StringInstruction.GetFilters(cleaned).ToArray();
        BatchEditing.ScreenStrings(filters);
        return filters;
    }

    public static IEnumerable<KeyValuePair<string, string>> Split(IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            var index = line.IndexOf(Splitter);
            if (index < 0)
                continue;

            var key = line[..index];
            var value = line.Substring(index + 1, line.Length - key.Length - 1).Trim();
            yield return new KeyValuePair<string, string>(key, value);
        }
    }

    public static string GetSummary(RegenSetting extra) => extra.GetSummary();

    public static string GetSummary(ITrainerInfo trainer)
    {
        int tid = trainer.TID16;
        int sid = trainer.SID16;
        if (trainer.Generation >= 7)
        {
            const int mil = 1_000_000;
            uint repack = ((uint)sid << 16) + (uint)tid;
            tid = (int)(repack % mil);
            sid = (int)(repack / mil);
        }

        var sb = new StringBuilder();
        sb.AppendLine($"OT: {trainer.OT}");
        sb.AppendLine($"OTGender: {(trainer.Gender == 1 ? "Female" : "Male")}");
        sb.AppendLine($"TID: {tid}");
        sb.AppendLine($"SID: {sid}");
        return sb.ToString();
    }

    public static string GetSummary(StringInstructionSet set)
    {
        var result = new List<string>();
        foreach (var s in set.Filters)
            result.Add($"{StringInstruction.Prefixes[(int)s.Comparer]}{s.PropertyName}={s.PropertyValue}");

        foreach (var s in set.Instructions)
            result.Add($".{s.PropertyName}={s.PropertyValue}");

        return string.Join(Environment.NewLine, result);
    }

    public static string GetSummary(IEnumerable<StringInstruction> filters, char prefix = EncounterFilterPrefix)
    {
        var result = new List<string>();
        foreach (var s in filters)
            result.Add($"{prefix}{StringInstruction.Prefixes[(int)s.Comparer]}{s.PropertyName}={s.PropertyValue}");

        return string.Join(Environment.NewLine, result);
    }

    /// <summary>
    /// Clone trainerdata and mutate the language and then return the clone
    /// </summary>
    /// <param name="tr">Trainerdata to clone</param>
    /// <param name="lang">language to mutate</param>
    /// <param name="ver"></param>
    public static ITrainerInfo MutateLanguage(this ITrainerInfo tr, LanguageID? lang, GameVersion ver)
    {
        if (lang is LanguageID.UNUSED_6 or LanguageID.Hacked or null)
            return tr;

        if (tr is PokeTrainerDetails p)
        {
            var clone = new PokeTrainerDetails(p.Entity.Clone()) { Language = (int)lang };
            clone.OT = MutateOT(clone.OT, lang, ver);
            return clone;
        }
        if (tr is SimpleTrainerInfo s)
        {
            var version = Array.Find(GameUtil.GameVersions, z => ver.Contains(z) && z != GameVersion.BU);
            return new SimpleTrainerInfo(version)
            {
                OT = MutateOT(s.OT, lang, version),
                TID16 = s.TID16,
                SID16 = s.SID16,
                Gender = s.Gender,
                Language = (int)lang,
                Context = s.Context,
                ConsoleRegion = s.ConsoleRegion != 0 ? s.ConsoleRegion : (byte)1,
                Region = s.Region != 0 ? s.Region : (byte)7,
                Country = s.Country != 0 ? s.Country : (byte)49,
                Generation = s.Generation,
            };
        }
        return tr;
    }

    private static string MutateOT(string OT, LanguageID? lang, GameVersion game)
    {
        if (lang == null)
            return OT;

        var max = Legal.GetMaxLengthOT(game.GetGeneration(), (LanguageID)lang);
        OT = OT[..Math.Min(OT.Length, max)];
        if (GameVersion.GG.Contains(game) || game.GetGeneration() >= 8) // switch keyboard only has latin characters, --don't mutate
            return OT;

        var full = lang is LanguageID.Japanese or LanguageID.Korean or LanguageID.ChineseS or LanguageID.ChineseT;
        if (full && GlyphLegality.ContainsHalfWidth(OT))
            return GlyphLegality.StringConvert(OT, StringConversionType.FullWidth);

        return !full && GlyphLegality.ContainsFullWidth(OT) ? GlyphLegality.StringConvert(OT, StringConversionType.HalfWidth) : OT;
    }

    public static string MutateNickname(string nick, LanguageID? lang, GameVersion game)
    {
        // Length checks are handled later in SetSpeciesLevel
        if (game.GetGeneration() >= 8 || lang == null)
            return nick;

        var full = lang is LanguageID.Japanese or LanguageID.Korean or LanguageID.ChineseS or LanguageID.ChineseT;
        return full switch
        {
            true when GlyphLegality.ContainsHalfWidth(nick) => GlyphLegality.StringConvert(nick, StringConversionType.FullWidth),
            false when GlyphLegality.ContainsFullWidth(nick) => GlyphLegality.StringConvert(nick, StringConversionType.HalfWidth),
            _ => nick,
        };
    }

    public static int GetRegenAbility(ushort species, byte gen, AbilityRequest ar)
    {
        var pi = GameData.GetPersonal(GetGameVersionFromGen(gen))[species];
        var abils_ct = pi.AbilityCount;
        return pi is not IPersonalAbility12 a ? -1 : ar switch
        {
            AbilityRequest.Any => -1,
            AbilityRequest.First => a.Ability1,
            AbilityRequest.Second => a.Ability2,
            AbilityRequest.NotHidden => a.Ability1,
            AbilityRequest.PossiblyHidden => a.Ability1,
            AbilityRequest.Hidden
                => abils_ct > 2 && pi is IPersonalAbility12H h ? h.AbilityH : -1,
            _ => throw new Exception($"Invalid AbilityRequest: {ar}"),
        };
    }

    public static GameVersion GetGameVersionFromGen(byte gen) => gen switch
    {
        1 => GameVersion.RB,
        2 => GameVersion.C,
        3 => GameVersion.E,
        4 => GameVersion.Pt,
        5 => GameVersion.B2W2,
        6 => GameVersion.ORAS,
        7 => GameVersion.USUM,
        8 => GameVersion.SWSH,
        9 => GameVersion.SV,
        _ => throw new Exception($"Invalid generation: {gen}"),
    };
}