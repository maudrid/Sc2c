// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Crateris.Core.Tests;

/// <summary>
/// The test topics (tests/topics.json, tools/run_tests.py) stay whole:
/// every test class is in one known topic, every topic has tests, and the
/// topic map (which files each topic's tests depend on, made by
/// tools/run_tests.py --map) has every topic. A file the map doesn't know
/// runs every topic with --changed, and these checks run with every
/// --changed run.
/// </summary>
[Trait("Topic", "world")]
public class TopicTests
{
    private static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "tests", "topics.json")))
                return dir.FullName;
        }
        throw new InvalidOperationException("tests/topics.json isn't above the test's folder");
    }

    private static readonly Lazy<JsonElement> Topics = new(() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(Root(), "tests", "topics.json"))).RootElement);

    /// <summary>A glob as tools/run_tests.py reads it: ** any folders, * anything in one name.</summary>
    public static Regex Glob(string glob)
    {
        var pattern = Regex.Escape(glob).Replace(@"\*\*/", "(.*/)?").Replace(@"\*\*", ".*").Replace(@"\*", "[^/]*")
            .Replace(@"\?", "[^/]");
        return new Regex("^" + pattern + "$");
    }

    private static IEnumerable<Type> TestClasses() => typeof(TopicTests).Assembly.GetTypes()
        .Where(t => t.IsClass && !t.IsAbstract && t.GetMethods().Any(m =>
            m.GetCustomAttributes().Any(a => a is FactAttribute)));

    // A trait's name and value are in the attribute's data (TraitAttribute doesn't keep them).
    private static string? TopicOf(Type t) => t.GetCustomAttributesData()
        .Where(d => d.AttributeType == typeof(TraitAttribute) && (string?)d.ConstructorArguments[0].Value == "Topic")
        .Select(d => (string?)d.ConstructorArguments[1].Value)
        .SingleOrDefault();

    [Fact]
    public void EveryTestClassIsInOneKnownTopic()
    {
        var known = Topics.Value.GetProperty("topics").EnumerateObject().Select(t => t.Name).ToHashSet();
        var classes = TestClasses().ToList();
        Assert.True(classes.Count > 40, $"only {classes.Count} test classes");
        foreach (var t in classes)
        {
            string? topic = TopicOf(t);
            Assert.True(topic != null && known.Contains(topic), $"{t.Name} has no topic in tests/topics.json ({topic})");
        }
        var used = classes.Select(TopicOf).ToHashSet();
        Assert.All(known, k => Assert.True(used.Contains(k), $"topic {k} has no tests"));
    }

    [Fact]
    public void TheTopicMapCoversEveryTopic()
    {
        // tests/topic-map.json (tools/run_tests.py --map): which files each
        // topic's tests depend on. A file it doesn't know runs every topic.
        // Not while the map is being made: it may be the one being mended.
        if (Golden.Mapping)
            return;
        var map = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root(), "tests", "topic-map.json"))).RootElement;
        var known = Topics.Value.GetProperty("topics").EnumerateObject().Select(t => t.Name).OrderBy(n => n);
        Assert.Equal(known, map.GetProperty("topics").EnumerateObject().Select(t => t.Name).OrderBy(n => n));
        foreach (var t in map.GetProperty("topics").EnumerateObject())
            Assert.True(t.Value.GetArrayLength() > 5, $"topic {t.Name} depends on only {t.Value.GetArrayLength()} files");
        foreach (var g in Topics.Value.GetProperty("all").EnumerateArray())
            Assert.True(Directory.EnumerateFiles(Root(), "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(Root(), f).Replace('\\', '/')).Any(f => Glob(g.GetString()!).IsMatch(f)),
                $"tests/topics.json's all names {g}, which matches no file");
    }

    [Fact]
    public void GlobsReadAsTheRunnerReadsThem()
    {
        Assert.Matches(Glob("src/Crateris.Core/Uqm/**"), "src/Crateris.Core/Uqm/UqmMath.cs");
        Assert.Matches(Glob("src/**/x.cs"), "src/x.cs");
        Assert.Matches(Glob("tests/golden/ip-*.json"), "tests/golden/ip-flight.json");
        Assert.DoesNotMatch(Glob("tests/golden/ip-*.json"), "tests/golden/sub/ip-flight.json");
        Assert.DoesNotMatch(Glob("src/Crateris.Core/Uqm/*"), "src/Crateris.Core/Uqm/a/b.cs");
    }
}
